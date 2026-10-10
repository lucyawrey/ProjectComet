using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using Comet.Protocol;
using Comet.Protocol.Framing;
using Comet.Protocol.Messages;
using Comet.Server.Connections;
using Comet.Server.Ticking;

namespace Comet.Server.Tests;

/// <summary>A server <see cref="Connection"/> over a loopback socket, read by a plain client WebSocket.</summary>
public sealed class ConnectionTests : IAsyncLifetime
{
    private TcpListener _listener = null!;
    private TcpClient _clientTcp = null!;
    private TcpClient _serverTcp = null!;
    private WebSocket _client = null!;
    private readonly ConnectionRegistry _registry = new();
    private Connection _connection = null!;
    private Task _running = null!;

    public async ValueTask InitializeAsync()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _clientTcp = new TcpClient();
        var accepting = _listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken).AsTask();
        await _clientTcp.ConnectAsync((IPEndPoint)_listener.LocalEndpoint, TestContext.Current.CancellationToken);
        _serverTcp = await accepting;

        _client = WebSocket.CreateFromStream(_clientTcp.GetStream(), new WebSocketCreationOptions { IsServer = false });
        var server = WebSocket.CreateFromStream(_serverTcp.GetStream(), new WebSocketCreationOptions { IsServer = true });
        _connection = new Connection(server, _registry, new NoHandler(), new TickLoop(30, _ => { }));
        _running = _connection.RunAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Abort();
        await _running;
        _clientTcp.Dispose();
        _serverTcp.Dispose();
        _listener.Stop();
    }

    [Fact]
    public async Task EventsOverOneFrameSpreadOverTickFramesUnderTheClientLimit()
    {
        // Over 100 KB of events in one tick, like 60 players each saying three of the longest chat lines at once
        // (the code review's S1), and a state for each of them.
        const int events = 12_000;
        for (var i = 0; i < events; i++)
        {
            _connection.SendEvent(MessageIds.Pong, new Pong { ClientTime = long.MaxValue - i });
        }
        for (uint i = 0; i < 60; i++)
        {
            _connection.EntityStates.Set(i, new EntityState { EntityId = i });
        }

        var received = new List<long>();
        var states = 0;
        var buffer = new byte[1024 * 1024];
        for (uint tick = 1; received.Count < events || states < 60; tick++)
        {
            Assert.True(tick < 10, $"only {received.Count} events and {states} states after {tick} ticks");
            while (_connection.TickFrameInFlight)
            {
                await Task.Delay(1, TestContext.Current.CancellationToken);
            }

            _connection.Flush(tick);
            var length = await ReceiveFrame(buffer);
            Assert.True(length <= FrameReader.MaxServerFrameSize, $"a {length} byte frame");
            var reader = FrameReader.Create(buffer.AsMemory(0, length));
            while (reader.TryReadNext(out var id, out var payload))
            {
                if (id == MessageIds.Pong)
                {
                    received.Add(FrameReader.Decode<Pong>(payload).ClientTime);
                }
                else if (id == MessageIds.EntityState)
                {
                    states++;
                }
            }
        }

        Assert.Equal(Enumerable.Range(0, events).Select(i => long.MaxValue - i), received);
    }

    [Fact]
    public async Task AFloodOfEmptyFramesDropsTheClient()
    {
        // Frames with only a header carry no messages, but still cost the server to receive (the code review's S7).
        var empty = new byte[MessageWriter.TickSize];
        try
        {
            for (var i = 0; i < 2 * Connection.MessageBurst; i++)
            {
                await _client.SendAsync(empty, WebSocketMessageType.Binary, endOfMessage: true, TestContext.Current.CancellationToken);
            }
        }
        catch (WebSocketException)
        {
            // Dropped while still sending.
        }

        await _running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, _registry.Stats.ProtocolErrors);
    }

    private async Task<int> ReceiveFrame(byte[] buffer)
    {
        var length = 0;
        while (true)
        {
            var result = await _client.ReceiveAsync(buffer.AsMemory(length), TestContext.Current.CancellationToken);
            length += result.Count;
            if (result.EndOfMessage)
            {
                return length;
            }
        }
    }

    private sealed class NoHandler : IConnectionHandler
    {
        public void OnConnected(Connection connection) { }

        public void OnMessage(Connection connection, uint clientTick, ushort messageId, ReadOnlyMemory<byte> payload) { }

        public void OnDisconnected(Connection connection) { }
    }
}
