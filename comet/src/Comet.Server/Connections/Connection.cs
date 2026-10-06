using System.Buffers;
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
/// sending, the flush is skipped and the state keeps being replaced with newer updates.
/// Pings are answered immediately in their own frame, so round trips measure the network
/// rather than the wait for the next tick.
/// </para>
/// <para>
/// Threads: <see cref="SendEvent"/>, <see cref="EntityStates"/> and <see cref="Flush"/> belong to
/// the tick thread; <see cref="SendImmediate"/> to the connection's receive flow (handler callbacks).
/// </para>
/// </summary>
public sealed class Connection
{
    public const int MaxIncomingFrameSize = 4096;

    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);

    private readonly WebSocket _socket;
    private readonly ConnectionRegistry _registry;
    private readonly IConnectionHandler _handler;
    private readonly TickLoop _tickLoop;
    private readonly Channel<Outgoing> _outgoing = Channel.CreateUnbounded<Outgoing>(
        new UnboundedChannelOptions { SingleReader = true });

    // Tick thread.
    private readonly MessageWriter _events = new();
    private readonly MessageWriter _tickFrame = new();
    private int _tickFrameInFlight;

    // Receive flow.
    private readonly MessageWriter _immediateFrame = new(64);
    private readonly byte[] _receiveBuffer = new byte[MaxIncomingFrameSize];

    internal Connection(WebSocket socket, ConnectionRegistry registry, IConnectionHandler handler, TickLoop tickLoop)
    {
        _socket = socket;
        _registry = registry;
        _handler = handler;
        _tickLoop = tickLoop;
        Id = registry.NextId();
    }

    public uint Id { get; }

    /// <summary>The game's own data for this connection.</summary>
    public object? Tag { get; set; }

    /// <summary>Pending entity updates, latest only. Tick thread.</summary>
    public LatestOnlyQueue<EntityState> EntityStates { get; } = new();

    /// <summary>Queues an event for the next tick frame. Events are never dropped or merged. Tick thread.</summary>
    public void SendEvent<T>(ushort messageId, in T message) => _events.Write(messageId, message);

    /// <summary>Sends a message now, in its own frame. Receive flow only (handler callbacks).</summary>
    public void SendImmediate<T>(ushort messageId, in T message)
    {
        _immediateFrame.BeginFrame(_tickLoop.CurrentTick);
        _immediateFrame.Write(messageId, message);
        var rented = ArrayPool<byte>.Shared.Rent(_immediateFrame.Length);
        _immediateFrame.WrittenSpan.CopyTo(rented);
        if (!_outgoing.Writer.TryWrite(new Outgoing(rented, _immediateFrame.Length)))
        {
            ArrayPool<byte>.Shared.Return(rented); // closed
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
        _tickFrame.Append(_events);
        _events.Clear();
        _registry.Stats.AddStateReplaced(EntityStates.TakeReplacedCount());
        EntityStates.Drain(_tickFrame, static (frame, state) => frame.Write(MessageIds.EntityState, state));

        if (_tickFrame.MessageCount == 0 || !_outgoing.Writer.TryWrite(new Outgoing(null, _tickFrame.Length)))
        {
            Volatile.Write(ref _tickFrameInFlight, 0);
        }
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
        else if (clientClosed)
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
        while (reader.TryReadNext(out var messageId, out var payload))
        {
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

    private async Task SendLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in _outgoing.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    var data = item.Rented is null
                        ? _tickFrame.WrittenMemory[..item.Length]
                        : item.Rented.AsMemory(0, item.Length);
                    await _socket.SendAsync(data, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken);
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
                    }
                }
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

    private async Task CloseQuietlyAsync(WebSocketCloseStatus status)
    {
        try
        {
            await _socket.CloseOutputAsync(status, null, CancellationToken.None);
        }
        catch (Exception e) when (e is WebSocketException or ObjectDisposedException)
        {
        }
    }

    /// <summary>A queued frame: an immediate frame in a rented buffer, or (when <see cref="Rented"/> is null) the tick frame.</summary>
    private readonly record struct Outgoing(byte[]? Rented, int Length);
}
