using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Comet.Client.Tests;

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

    private async Task<NetworkStream> AcceptSilentlyAsync()
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
        return stream;
    }

    private static byte[] ServerFrame(int length)
    {
        var frame = new byte[4 + length];
        frame[0] = 0x82;
        frame[1] = 126;
        frame[2] = (byte)(length >> 8);
        frame[3] = (byte)length;
        return frame;
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
        Assert.Equal(CloseReason.None, transport.CloseReason);
        Assert.Null(transport.Error);
    }

    [Fact]
    public async Task AnAddressThatIsntAWebSocketFailsWithAReason()
    {
        var transport = new WebSocketTransport(new Uri("http://127.0.0.1:1/ws"));

        await transport.Completion.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(TransportState.Closed, transport.State);
        Assert.Equal(CloseReason.ConnectFailed, transport.CloseReason);
        Assert.NotNull(transport.Error);
    }

    [Fact]
    public async Task AServerThatNeverAnswersTheHandshakeTimesOut()
    {
        var transport = new WebSocketTransport(Url, connectTimeout: TimeSpan.FromMilliseconds(300));

        await transport.Completion.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(TransportState.Closed, transport.State);
        Assert.Equal(CloseReason.ConnectTimedOut, transport.CloseReason);
    }

    [Fact]
    public async Task FramesNobodyTakesPastTheCapCloseTheConnection()
    {
        var accepting = AcceptSilentlyAsync();
        var transport = new WebSocketTransport(Url);
        var stream = await accepting;
        await WaitUntilOpen(transport);

        var frame = ServerFrame(60 * 1024);
        try
        {
            for (var sent = 0; sent <= TransportLimits.MaxQueuedBytes + frame.Length; sent += frame.Length)
            {
                await stream.WriteAsync(frame, TestContext.Current.CancellationToken);
            }
        }
        catch (IOException) { }

        await transport.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(CloseReason.FellBehind, transport.CloseReason);
        Assert.False(transport.TryReceive(out _), "The frames waiting were kept, to be handled all at once.");
    }
}
