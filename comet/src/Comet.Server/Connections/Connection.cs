using System.Buffers;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Threading.Channels;
using Comet.Protocol;
using Comet.Protocol.Framing;
using Comet.Protocol.Messages;
using Comet.Server.Ticking;

namespace Comet.Server.Connections;

/// <summary>
/// One client's WebSocket connection.
/// <para>
/// Outgoing: each tick, <see cref="Flush"/> builds one frame from the queued events and the
/// latest-only state queues. At most one tick frame is in flight; if the previous one is still
/// sending, the flush is skipped and the state keeps being replaced with newer updates. A tick frame
/// stays under the clients' frame limit: events past <see cref="TickFrameEventBudget"/>, and states
/// that don't fit, wait for the next tick, in order; states wait while any events do (so a burst of events
/// pauses everyone's movement for a few ticks; holding back only new entities' states could replace this).
/// Pings are answered immediately in their own frame, so round trips measure the network
/// rather than the wait for the next tick.
/// </para>
/// <para>
/// Limits (a client's traffic is never trusted): incoming messages are rate-limited (each frame, or piece of
/// one, costs at least a message), and a client over the limit is dropped; at most <see cref="MaxPendingImmediate"/> immediate frames wait to send, beyond which pongs are
/// dropped; a client that stops reading is dropped once its unsent events pass <see cref="MaxPendingEventBytes"/>
/// or a send has waited <see cref="SendTimeout"/>.
/// </para>
/// <para>
/// Threads: <see cref="SendEvent"/>, <see cref="EntityStates"/> and <see cref="Flush"/> belong to
/// the tick thread; <see cref="SendImmediate"/> to the connection's receive flow (handler callbacks).
/// </para>
/// </summary>
public sealed class Connection
{
    public const int MaxIncomingFrameSize = 4096;

    /// <summary>Messages a client may send per second on average, well above an honest client's (about 20).</summary>
    public const double MaxMessagesPerSecond = 120;

    /// <summary>How many messages may arrive at once, e.g. the backlog delivered after a TCP stall.</summary>
    public const double MessageBurst = 240;

    /// <summary>Immediate frames (pongs) waiting to send; more are dropped.</summary>
    public const int MaxPendingImmediate = 16;

    /// <summary>Unsent event bytes before the client is taken to have stopped reading and is dropped.</summary>
    public const int MaxPendingEventBytes = 256 * 1024;

    /// <summary>Event bytes one tick frame carries at most, half the clients' frame limit.</summary>
    public const int TickFrameEventBudget = FrameReader.MaxServerFrameSize / 2;

    /// <summary>A tick frame takes no more entity states past this size, which leaves room for the last one under the clients' limit.</summary>
    public const int TickFrameStateLimit = FrameReader.MaxServerFrameSize - 1024;

    public static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);

    private readonly WebSocket _socket;
    private readonly ConnectionRegistry _registry;
    private readonly IConnectionHandler _handler;
    private readonly TickLoop _tickLoop;
    private readonly Channel<Outgoing> _outgoing = Channel.CreateUnbounded<Outgoing>(
        new UnboundedChannelOptions { SingleReader = true });

    // Tick thread.
    private readonly MessageWriter _events;
    private readonly Queue<int> _eventSizes = new();
    private readonly MessageWriter _tickFrame;
    private int _tickFrameInFlight;
    private bool _dropped;

    // Receive flow.
    private readonly MessageWriter _immediateFrame;
    private readonly byte[] _receiveBuffer = new byte[MaxIncomingFrameSize];
    private double _messageAllowance = MessageBurst;
    private long _allowanceTime = Stopwatch.GetTimestamp();

    // Both.
    private int _pendingImmediate;
    private volatile bool _shuttingDown;

    internal Connection(WebSocket socket, ConnectionRegistry registry, IConnectionHandler handler, TickLoop tickLoop)
    {
        _socket = socket;
        _registry = registry;
        _handler = handler;
        _tickLoop = tickLoop;
        _events = new MessageWriter(options: registry.SerializerOptions);
        _tickFrame = new MessageWriter(options: registry.SerializerOptions);
        _immediateFrame = new MessageWriter(64, registry.SerializerOptions);
        Id = registry.NextId();
    }

    public uint Id { get; }

    /// <summary>The game's own data for this connection.</summary>
    public object? Tag { get; set; }

    /// <summary>Whether the last tick frame is still sending (a flush now would be skipped).</summary>
    internal bool TickFrameInFlight => Volatile.Read(ref _tickFrameInFlight) != 0;

    /// <summary>Pending entity updates, latest only. Tick thread.</summary>
    public LatestOnlyQueue<EntityState> EntityStates { get; } = new();

    /// <summary>Queues an event for the next tick frame. Events are never dropped or merged. Tick thread.</summary>
    public void SendEvent<T>(ushort messageId, in T message)
    {
        if (_dropped)
        {
            return;
        }

        if (_events.Length > MaxPendingEventBytes)
        {
            // The client has stopped reading: drop it rather than keep everything for it.
            _dropped = true;
            _events.Clear();
            _eventSizes.Clear();
            _registry.Stats.AddSlowClientDropped();
            _socket.Abort();
            return;
        }

        var length = _events.Length;
        _events.Write(messageId, message);
        _eventSizes.Enqueue(_events.Length - length);
    }

    /// <summary>Sends a message now, in its own frame. Receive flow only (handler callbacks).</summary>
    public void SendImmediate<T>(ushort messageId, in T message)
    {
        if (Interlocked.Increment(ref _pendingImmediate) > MaxPendingImmediate)
        {
            Interlocked.Decrement(ref _pendingImmediate);
            return;
        }

        _immediateFrame.BeginFrame(_tickLoop.CurrentTick);
        _immediateFrame.Write(messageId, message);
        var rented = ArrayPool<byte>.Shared.Rent(_immediateFrame.Length);
        _immediateFrame.WrittenSpan.CopyTo(rented);
        if (!_outgoing.Writer.TryWrite(new Outgoing(rented, _immediateFrame.Length)))
        {
            ArrayPool<byte>.Shared.Return(rented); // closed
            Interlocked.Decrement(ref _pendingImmediate);
        }
    }

    /// <summary>Builds and sends this tick's frame, unless the previous one is still sending. Tick thread.</summary>
    public void Flush(uint tick)
    {
        if (Interlocked.CompareExchange(ref _tickFrameInFlight, 1, 0) != 0)
        {
            _registry.Stats.AddFlushSkipped();
            return;
        }

        _tickFrame.BeginFrame(tick);
        AppendEvents();
        _registry.Stats.AddStateReplaced(EntityStates.TakeReplacedCount());

        // States wait while events do, so an entity's state never arrives before the events about it (its spawn).
        if (_eventSizes.Count == 0)
        {
            EntityStates.Drain(_tickFrame, static (frame, state) =>
            {
                if (frame.Length > TickFrameStateLimit)
                {
                    return false;
                }
                frame.Write(MessageIds.EntityState, state);
                return true;
            });
        }

        if (_tickFrame.MessageCount == 0 || !_outgoing.Writer.TryWrite(new Outgoing(null, _tickFrame.Length)))
        {
            Volatile.Write(ref _tickFrameInFlight, 0);
        }
    }

    /// <summary>Moves the oldest events, up to <see cref="TickFrameEventBudget"/> bytes, into the tick frame.</summary>
    private void AppendEvents()
    {
        var length = 0;
        var count = 0;
        foreach (var size in _eventSizes)
        {
            // A single event over the budget still goes, alone, rather than blocking the queue.
            if (count > 0 && length + size > TickFrameEventBudget)
            {
                break;
            }
            length += size;
            count++;
        }

        _tickFrame.Append(_events, length, count);
        _events.RemoveStart(length, count);
        for (var i = 0; i < count; i++)
        {
            _eventSizes.Dequeue();
        }
    }

    /// <summary>Drops the connection at once, without a close frame, e.g. a client the game has given up on. Any thread.</summary>
    public void Disconnect() => _socket.Abort();

    /// <summary>
    /// Closes the connection because the server is stopping: sends what's queued, then a close frame, and
    /// aborts if the client hasn't answered within the close timeout.
    /// </summary>
    internal void Shutdown()
    {
        _shuttingDown = true;
        _outgoing.Writer.TryComplete();
        _ = Task.Delay(CloseTimeout).ContinueWith(_ => _socket.Abort(), TaskScheduler.Default);
    }

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        // The handler sets up its data before the tick thread can see the connection.
        _handler.OnConnected(this);
        _registry.Add(this);
        var sending = SendLoopAsync(cancellationToken);
        var clientClosed = false;
        try
        {
            await ReceiveLoopAsync(cancellationToken);
            clientClosed = true;
        }
        catch (ProtocolException)
        {
            // Don't wait for a misbehaving client to read a close frame.
            _registry.Stats.AddProtocolError();
            _socket.Abort();
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException)
        {
            // The client went away.
        }
        finally
        {
            _registry.Remove(this);
            _outgoing.Writer.TryComplete();
            _handler.OnDisconnected(this);
        }

        // A WebSocket allows one send at a time, and closing counts as a send, so close only
        // after the send loop has finished; give up on a client that has stopped reading.
        if (await Task.WhenAny(sending, Task.Delay(CloseTimeout, CancellationToken.None)) != sending)
        {
            _socket.Abort();
            await sending;
        }
        else if (clientClosed && _socket.State == WebSocketState.CloseReceived)
        {
            await CloseQuietlyAsync(WebSocketCloseStatus.NormalClosure);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var length = 0;
        while (true)
        {
            var result = await _socket.ReceiveAsync(_receiveBuffer.AsMemory(length), cancellationToken);

            // Each piece of a frame costs a message, so floods of empty frames or tiny fragments are limited too.
            Charge();
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return;
            }
            if (result.MessageType != WebSocketMessageType.Binary)
            {
                throw new ProtocolException("Frames must be binary.");
            }

            length += result.Count;
            if (result.EndOfMessage)
            {
                HandleFrame(_receiveBuffer.AsMemory(0, length));
                length = 0;
            }
            else if (length == _receiveBuffer.Length)
            {
                throw new ProtocolException($"Frame is larger than {MaxIncomingFrameSize} bytes.");
            }
        }
    }

    private void HandleFrame(ReadOnlyMemory<byte> frame)
    {
        _registry.Stats.AddReceived(frame.Length);
        var reader = FrameReader.Create(frame);
        var first = true;
        while (reader.TryReadNext(out var messageId, out var payload))
        {
            // The frame's first message was paid for when it arrived.
            if (!first)
            {
                Charge();
            }
            first = false;

            if (messageId == MessageIds.Ping)
            {
                var ping = FrameReader.Decode<Ping>(payload);
                SendImmediate(MessageIds.Pong, new Pong { ClientTime = ping.ClientTime });
            }
            else
            {
                _handler.OnMessage(this, reader.Tick, messageId, payload);
            }
        }
    }

    /// <summary>Takes one message from the allowance, which refills at <see cref="MaxMessagesPerSecond"/>.</summary>
    private void Charge()
    {
        var now = Stopwatch.GetTimestamp();
        _messageAllowance = Math.Min(MessageBurst, _messageAllowance + Stopwatch.GetElapsedTime(_allowanceTime, now).TotalSeconds * MaxMessagesPerSecond);
        _allowanceTime = now;
        if (--_messageAllowance < 0)
        {
            throw new ProtocolException("Too many messages.");
        }
    }

    private async Task SendLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await foreach (var item in _outgoing.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    var data = item.Rented is null
                        ? _tickFrame.WrittenMemory[..item.Length]
                        : item.Rented.AsMemory(0, item.Length);
                    // A client that stops reading fills its socket's buffers, and the send waits; give up on it.
                    timeout.CancelAfter(SendTimeout);
                    await _socket.SendAsync(data, WebSocketMessageType.Binary, endOfMessage: true, timeout.Token);
                    timeout.CancelAfter(Timeout.InfiniteTimeSpan);
                    _registry.Stats.AddSent(item.Length);
                }
                finally
                {
                    if (item.Rented is null)
                    {
                        Volatile.Write(ref _tickFrameInFlight, 0);
                    }
                    else
                    {
                        ArrayPool<byte>.Shared.Return(item.Rented);
                        Interlocked.Decrement(ref _pendingImmediate);
                    }
                }
            }

            if (_shuttingDown)
            {
                // The client answers the close, which ends the receive loop.
                await CloseQuietlyAsync(WebSocketCloseStatus.EndpointUnavailable, "The server is shutting down.");
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // The client went away; ending the socket also ends the receive loop.
            _socket.Abort();
        }
        finally
        {
            // Return any rented buffers still queued.
            while (_outgoing.Reader.TryRead(out var item))
            {
                if (item.Rented is not null)
                {
                    ArrayPool<byte>.Shared.Return(item.Rented);
                }
            }
        }
    }

    private async Task CloseQuietlyAsync(WebSocketCloseStatus status, string? reason = null)
    {
        try
        {
            await _socket.CloseOutputAsync(status, reason, CancellationToken.None);
        }
        catch (Exception e) when (e is WebSocketException or ObjectDisposedException)
        {
        }
    }

    /// <summary>A queued frame: an immediate frame in a rented buffer, or (when <see cref="Rented"/> is null) the tick frame.</summary>
    private readonly record struct Outgoing(byte[]? Rented, int Length);
}
