using System.Diagnostics;
using System.Net.WebSockets;
using Comet.Protocol;
using Comet.Protocol.Framing;
using Comet.Protocol.Messages;
using StackBench.Metrics;

namespace StackBench.Bots;

/// <summary>
/// One simulated player: walks a seeded random path, reports its position at a fixed rate, pings
/// for round trips, and records the gaps between frames carrying entity updates.
/// </summary>
public sealed class Bot(int index, BotOptions options, BenchWindow window)
{
    private const float Speed = 5f;      // metres per second
    private const float HalfArea = 100f; // walks within a 200 m square
    private const int ReceiveBufferSize = 64 * 1024;

    private readonly Random _random = new(HashCode.Combine(options.Seed, index));
    private readonly ClientWebSocket _socket = new();
    private float _x, _z, _facing;
    private uint _lastServerTick;
    private long _lastUpdateFrame;

    public LatencyRecorder RoundTrip { get; } = new();
    public LatencyRecorder UpdateGap { get; } = new();
    public long BytesReceived { get; private set; }
    public long BytesSent { get; private set; }

    /// <summary>Failed to connect, or lost the connection before the window ended.</summary>
    public bool Failed { get; private set; }

    public async Task RunAsync(CancellationToken stop)
    {
        _x = (float)(_random.NextDouble() * 2 - 1) * HalfArea;
        _z = (float)(_random.NextDouble() * 2 - 1) * HalfArea;
        _facing = (float)(_random.NextDouble() * Math.Tau);

        try
        {
            await _socket.ConnectAsync(options.Url, stop);
        }
        catch (Exception e) when (e is WebSocketException or HttpRequestException or OperationCanceledException)
        {
            Failed = !stop.IsCancellationRequested;
            return;
        }

        var receiving = ReceiveLoopAsync();
        try
        {
            await SendLoopAsync(stop);
            await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        }
        catch (Exception e) when (e is WebSocketException or ObjectDisposedException)
        {
            MarkLostIfInWindow();
        }
        await receiving;
        _socket.Dispose();
    }

    private async Task SendLoopAsync(CancellationToken stop)
    {
        var frame = new MessageWriter();
        var period = TimeSpan.FromSeconds(1 / options.ReportRate);
        var pingPeriod = Stopwatch.Frequency / options.PingRate;
        var nextPing = Stopwatch.GetTimestamp() + _random.NextDouble() * pingPeriod;
        using var timer = new PeriodicTimer(period);

        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                Walk((float)period.TotalSeconds, out var velocityX, out var velocityZ);
                frame.BeginFrame(_lastServerTick);
                frame.Write(MessageIds.PositionReport, new PositionReport
                {
                    X = _x,
                    Z = _z,
                    VelocityX = velocityX,
                    VelocityZ = velocityZ,
                    Facing = _facing,
                });

                var now = Stopwatch.GetTimestamp();
                if (now >= nextPing)
                {
                    frame.Write(MessageIds.Ping, new Ping { ClientTime = now });
                    nextPing += pingPeriod;
                }

                await _socket.SendAsync(frame.WrittenMemory, WebSocketMessageType.Binary, endOfMessage: true, stop);
                if (window.IsMeasuring(now))
                {
                    BytesSent += frame.Length;
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[ReceiveBufferSize];
        var length = 0;
        try
        {
            while (true)
            {
                var result = await _socket.ReceiveAsync(buffer.AsMemory(length), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    MarkLostIfInWindow();
                    return;
                }
                length += result.Count;
                if (result.EndOfMessage)
                {
                    HandleFrame(buffer.AsMemory(0, length));
                    length = 0;
                }
                else if (length == buffer.Length)
                {
                    throw new ProtocolException("Server frame too large.");
                }
            }
        }
        catch (Exception e) when (e is WebSocketException or ProtocolException or ObjectDisposedException)
        {
            MarkLostIfInWindow();
        }
    }

    private void HandleFrame(ReadOnlyMemory<byte> data)
    {
        var now = Stopwatch.GetTimestamp();
        var measuring = window.IsMeasuring(now);
        var reader = FrameReader.Create(data);
        var hasUpdates = false;
        _lastServerTick = reader.Tick;

        while (reader.TryReadNext(out var messageId, out var payload))
        {
            switch (messageId)
            {
                case MessageIds.EntityState:
                    hasUpdates = true;
                    break;
                case MessageIds.Pong when measuring:
                    RoundTrip.Record(Stopwatch.GetElapsedTime(FrameReader.Decode<Pong>(payload).ClientTime, now));
                    break;
            }
        }

        if (measuring)
        {
            BytesReceived += data.Length;
        }
        if (hasUpdates)
        {
            if (measuring && _lastUpdateFrame != 0)
            {
                UpdateGap.Record(Stopwatch.GetElapsedTime(_lastUpdateFrame, now));
            }
            _lastUpdateFrame = now;
        }
    }

    /// <summary>A random walk: turn a little each step, and turn back at the edge of the area.</summary>
    private void Walk(float seconds, out float velocityX, out float velocityZ)
    {
        _facing += (float)((_random.NextDouble() * 2 - 1) * 1.5 * seconds);
        if (Math.Abs(_x) > HalfArea || Math.Abs(_z) > HalfArea)
        {
            _facing = MathF.Atan2(-_x, -_z); // head back towards the centre
        }
        velocityX = MathF.Sin(_facing) * Speed;
        velocityZ = MathF.Cos(_facing) * Speed;
        _x += velocityX * seconds;
        _z += velocityZ * seconds;
    }

    private void MarkLostIfInWindow()
    {
        if (!window.HasEnded(Stopwatch.GetTimestamp()))
        {
            Failed = true;
        }
    }
}
