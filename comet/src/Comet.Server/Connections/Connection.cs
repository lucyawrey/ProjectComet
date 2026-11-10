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
/// One client's WebSocket connection. <see cref="SendEvent"/>, <see cref="EntityStates"/> and <see cref="Flush"/> belong to the tick thread;
/// <see cref="SendImmediate"/> to the connection's receive flow (handler callbacks).
/// </summary>
public sealed class Connection
{
    /// <summary>The largest frame a client may send, in bytes; a bigger one drops the client.</summary>
    public const int MaxIncomingFrameSize = 4096;

    public const double MaxMessagesPerSecond = 120;

    public const double MessageBurst = 240;

    public const int MaxPendingImmediate = 16;

    public const int MaxPendingEventBytes = 256 * 1024;

    public const int TickFrameEventBudget = FrameReader.MaxServerFrameSize / 2;

    public const int TickFrameStateLimit = FrameReader.MaxServerFrameSize - 1024;

    public static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);

    private readonly WebSocket _socket;
    private readonly ConnectionRegistry _registry;
    private readonly IConnectionHandler _handler;
    private readonly TickLoop _tickLoop;
    private readonly Channel<Outgoing> _outgoing = Channel.CreateUnbounded<Outgoing>(new UnboundedChannelOptions { SingleReader = true });

    private readonly MessageWriter _events;
    private readonly Queue<int> _eventSizes = new();
    private readonly MessageWriter _tickFrame;
    private int _tickFrameInFlight;
    private bool _dropped;

    private readonly MessageWriter _immediateFrame;
    private readonly byte[] _receiveBuffer = new byte[MaxIncomingFrameSize];
    private double _messageAllowance = MessageBurst;
    private long _allowanceTime = Stopwatch.GetTimestamp();

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

    public object? Tag { get; set; }

    internal bool TickFrameInFlight => Volatile.Read(ref _tickFrameInFlight) != 0;

    public LatestOnlyQueue<EntityState> EntityStates { get; } = new();

    public void SendEvent<T>(ushort messageId, in T message)
    {
        if (_dropped)
        {
            return;
        }

        if (_events.Length > MaxPendingEventBytes)
        {
            _dropped = true;
            _events.Clear();
            _eventSizes.Clear();
            _socket.Abort();
            return;
        }

        var length = _events.Length;
        _events.Write(messageId, message);
        _eventSizes.Enqueue(_events.Length - length);
    }

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
            ArrayPool<byte>.Shared.Return(rented);
            Interlocked.Decrement(ref _pendingImmediate);
        }
    }

    public void Flush(uint tick)
    {
        if (Interlocked.CompareExchange(ref _tickFrameInFlight, 1, 0) != 0)
        {
            return;
        }

        _tickFrame.BeginFrame(tick);
        AppendEvents();

        // States wait while events do, so an entity's state never arrives before the events about it (its spawn);
        // a burst of events therefore pauses everyone's movement for a few ticks.
        if (_eventSizes.Count == 0)
        {
            EntityStates.Drain(
                _tickFrame,
                static (frame, state) =>
                {
                    if (frame.Length > TickFrameStateLimit)
                    {
                        return false;
                    }
                    frame.Write(MessageIds.EntityState, state);
                    return true;
                }
            );
        }

        if (_tickFrame.MessageCount == 0 || !_outgoing.Writer.TryWrite(new Outgoing(null, _tickFrame.Length)))
        {
            Volatile.Write(ref _tickFrameInFlight, 0);
        }
    }

    private void AppendEvents()
    {
        var length = 0;
        var count = 0;
        foreach (var size in _eventSizes)
        {
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

    public void Disconnect() => _socket.Abort();

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
        var reader = FrameReader.Create(frame);
        var first = true;
        while (reader.TryReadNext(out var messageId, out var payload))
        {
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

    private void Charge()
    {
        var now = Stopwatch.GetTimestamp();
        _messageAllowance = Math.Min(
            MessageBurst,
            _messageAllowance + Stopwatch.GetElapsedTime(_allowanceTime, now).TotalSeconds * MaxMessagesPerSecond
        );
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
                    var data = item.Rented is null ? _tickFrame.WrittenMemory[..item.Length] : item.Rented.AsMemory(0, item.Length);
                    timeout.CancelAfter(SendTimeout);
                    await _socket.SendAsync(data, WebSocketMessageType.Binary, endOfMessage: true, timeout.Token);
                    timeout.CancelAfter(Timeout.InfiniteTimeSpan);
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
                await CloseQuietlyAsync(WebSocketCloseStatus.EndpointUnavailable, "The server is shutting down.");
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            _socket.Abort();
        }
        finally
        {
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
        catch (Exception e) when (e is WebSocketException or ObjectDisposedException) { }
    }

    private readonly record struct Outgoing(byte[]? Rented, int Length);
}
