using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Comet.Client.Tests;

/// <summary>The desktop transport's close and error paths, against a server that accepts and then says nothing.</summary>
public sealed class WebSocketTransportTests : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly List<TcpClient> _accepted = [];

    public WebSocketTransportTests() => _listener.Start();

    private Uri Url => new($"ws://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/ws");

    public void Dispose()
    {
        _accepted.ForEach(c => c.Dispose());
        _listener.Stop();
    }

    // Completes the WebSocket handshake, then never reads or writes again (a hung or partitioned server).
    private async Task AcceptSilentlyAsync()
    {
        var client = await _listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        _accepted.Add(client);
        var stream = client.GetStream();
        var request = new StringBuilder();
        var buffer = new byte[1];
        while (!request.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            await stream.ReadExactlyAsync(buffer, TestContext.Current.CancellationToken);
            request.Append((char)buffer[0]);
        }

        var key = request.ToString().Split("\r\n").First(l => l.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))[18..].Trim();
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        var response = $"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(response), TestContext.Current.CancellationToken);
    }

    private static async Task WaitUntilOpen(WebSocketTransport transport)
    {
        for (var i = 0; i < 200 && transport.State == TransportState.Connecting; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Equal(TransportState.Open, transport.State);
    }

    [Fact]
    public async Task DisposingAnOpenConnectionClosesIt()
    {
        var accepting = AcceptSilentlyAsync();
        var transport = new WebSocketTransport(Url);
        await accepting;
        await WaitUntilOpen(transport);

        transport.Dispose();

        await transport.Completion.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(TransportState.Closed, transport.State);
    }

    [Fact]
    public async Task ACloseTheServerNeverAnswersGivesUpAndCloses()
    {
        var accepting = AcceptSilentlyAsync();
        var transport = new WebSocketTransport(Url, closeTimeout: TimeSpan.FromMilliseconds(200));
        await accepting;
        await WaitUntilOpen(transport);

        transport.Close();

        await transport.Completion.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(TransportState.Closed, transport.State);
        Assert.Null(transport.Error); // closing was our choice
    }

    [Fact]
    public async Task AnAddressThatIsntAWebSocketFailsWithAReason()
    {
        var transport = new WebSocketTransport(new Uri("http://127.0.0.1:1/ws"));

        await transport.Completion.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(TransportState.Closed, transport.State);
        Assert.NotNull(transport.Error);
    }
}
