using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Numerics;
using Comet.Protocol;
using Comet.Protocol.Framing;
using Comet.Protocol.Messages;
using Comet.Simulation;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;

namespace ShapeLand.Bots;

/// <summary>How a bot behaves.</summary>
public sealed class BotSettings
{
    /// <summary>Seconds between chat lines on average; 0 for none.</summary>
    public double ChatEverySeconds { get; set; } = 30;

    /// <summary>Slide this many times faster than the shape allows (1 for an honest bot).</summary>
    public float SpeedCheat { get; set; } = 1;

    /// <summary>Stay within this distance of the island's centre, in metres.</summary>
    public float WanderRadius { get; set; } = 18;
}

/// <summary>
/// One simulated player: joins, wanders the island with the shared player motor, jumps now and then and chats
/// occasionally. A cheating bot slides faster than its shape allows and should be snapped back. Counts what it
/// sees, so tests and the bot program can check the server's behaviour.
/// </summary>
public sealed class Bot(string name, int seed, BotSettings settings, ShapeLandContent content, CollisionWorld world)
{
    private const int StepsPerSecond = 30;
    private const int StepsPerReport = 2; // ~15 Hz while moving

    private static readonly string[] ChatLines = ["hello!", "nice island", "anyone up for a race?", "look, I can jump", "wheee", "brb"];

    private readonly Random _random = new(seed);
    private readonly ClientWebSocket _socket = new();
    private readonly ConcurrentQueue<byte[]> _frames = new();
    private readonly MessageWriter _frame = new(options: ShapeLandProtocol.Options);
    private readonly HashSet<uint> _seen = [];
    private MotorState _motor;
    private Shape _shape = null!;
    private uint _serverTick;
    private uint _correctionSequence;
    private Vector2 _target;
    private int _step;
    private bool _wasMoving;
    private double _nextJump;
    private double _nextChat;

    public string Name => name;

    public uint EntityId { get; private set; }

    public bool Joined { get; private set; }

    public JoinRejection? Rejected { get; private set; }

    /// <summary>Other players this bot has seen spawn.</summary>
    public IReadOnlyCollection<uint> SeenPlayers => _seen;

    public int StatesReceived { get; private set; }

    public int SnapBacks { get; private set; }

    public int Respawns { get; private set; }

    public int ChatsSent { get; private set; }

    public int ChatsReceived { get; private set; }

    public int Despawns { get; private set; }

    public bool Failed { get; private set; }

    public async Task RunAsync(Uri url, CancellationToken stop)
    {
        try
        {
            await _socket.ConnectAsync(url, stop);
        }
        catch (Exception e) when (e is WebSocketException or HttpRequestException or OperationCanceledException)
        {
            Failed = !stop.IsCancellationRequested;
            return;
        }

        var receiving = ReceiveLoopAsync();
        try
        {
            _shape = content.Shapes.All[_random.Next(content.Shapes.All.Count)];
            _frame.BeginFrame(0);
            _frame.Write(ShapeLandMessageIds.JoinRequest, new JoinRequest { Name = name, Shape = _shape.Number });
            await SendAsync(stop);
            await SimulateAsync(stop);
            await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        }
        catch (Exception e) when (e is WebSocketException or ObjectDisposedException)
        {
            Failed = true;
        }

        if (await Task.WhenAny(receiving, Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None)) != receiving)
        {
            _socket.Abort();
        }

        _socket.Dispose();
    }

    private async Task SimulateAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / StepsPerSecond));
        while (await timer.WaitForNextTickAsync(stop))
        {
            while (_frames.TryDequeue(out var frame))
            {
                Handle(frame);
            }

            if (!Joined)
            {
                continue;
            }

            _step++;
            Step(1f / StepsPerSecond);
            if (_frame.MessageCount > 0)
            {
                await SendAsync(stop);
            }
        }
    }

    private void Step(float seconds)
    {
        var now = _step / (double)StepsPerSecond;
        var position = new Vector2(_motor.Position.X, _motor.Position.Z);
        if (Vector2.Distance(position, _target) < 1.5f)
        {
            _target = RandomPoint();
        }

        var move = Vector2.Normalize(_target - position);
        var jump = _motor.Grounded && now >= _nextJump;
        if (jump)
        {
            _nextJump = now + 3 + _random.NextDouble() * 5;
        }

        PlayerMotor.Step(ref _motor, move, jump, seconds, _shape.MaxSpeed * settings.SpeedCheat, _shape.JumpVelocity, world, ShapeLandWorld.Rules);

        _frame.BeginFrame(_serverTick);
        var moving = _motor.Velocity.LengthSquared() > 0.01f;
        if (moving && (_step % StepsPerReport == 0 || !_wasMoving) || !moving && _wasMoving)
        {
            _frame.Write(MessageIds.PositionReport, new PositionReport
            {
                X = _motor.Position.X,
                Y = _motor.Position.Y,
                Z = _motor.Position.Z,
                VelocityX = _motor.Velocity.X,
                VelocityY = _motor.Velocity.Y,
                VelocityZ = _motor.Velocity.Z,
                Facing = _motor.Facing,
                CorrectionSequence = _correctionSequence,
            });
        }

        _wasMoving = moving;
        if (settings.ChatEverySeconds > 0 && now >= _nextChat)
        {
            _frame.Write(ShapeLandMessageIds.ChatSend, new ChatSend { Text = ChatLines[_random.Next(ChatLines.Length)] });
            ChatsSent++;
            _nextChat = now + settings.ChatEverySeconds * (0.5 + _random.NextDouble());
        }
    }

    private void Handle(byte[] data)
    {
        var reader = FrameReader.Create(data);
        _serverTick = reader.Tick;
        var options = ShapeLandProtocol.Options;
        while (reader.TryReadNext(out var messageId, out var payload))
        {
            switch (messageId)
            {
                case MessageIds.Welcome:
                    EntityId = FrameReader.Decode<Welcome>(payload, options).EntityId;
                    break;
                case ShapeLandMessageIds.PlayerSpawn:
                    var spawn = FrameReader.Decode<PlayerSpawn>(payload, options);
                    if (spawn.EntityId == EntityId)
                    {
                        Joined = true;
                        _motor = new MotorState { Position = new Vector3(spawn.X, spawn.Y, spawn.Z), Grounded = true, Facing = spawn.Facing };
                        _target = RandomPoint();
                        _nextJump = 2 + _random.NextDouble() * 4;
                        _nextChat = settings.ChatEverySeconds * _random.NextDouble();
                    }
                    else
                    {
                        _seen.Add(spawn.EntityId);
                    }

                    break;
                case ShapeLandMessageIds.JoinRejected:
                    Rejected = FrameReader.Decode<JoinRejected>(payload, options).Reason;
                    break;
                case MessageIds.EntityState:
                    StatesReceived++;
                    break;
                case MessageIds.EntityDespawn:
                    Despawns++;
                    break;
                case MessageIds.PositionCorrection:
                    var correction = FrameReader.Decode<PositionCorrection>(payload, options);
                    _correctionSequence = correction.Sequence;
                    _motor.Position = new Vector3(correction.X, correction.Y, correction.Z);
                    _motor.Velocity = Vector3.Zero;
                    _motor.Grounded = false;
                    if (correction.Reason == CorrectionReason.Respawn)
                    {
                        Respawns++;
                    }
                    else
                    {
                        SnapBacks++;
                    }

                    break;
                case ShapeLandMessageIds.ChatMessage:
                    ChatsReceived++;
                    break;
            }
        }
    }

    private Vector2 RandomPoint()
    {
        var angle = _random.NextDouble() * Math.Tau;
        var distance = settings.WanderRadius * Math.Sqrt(_random.NextDouble());
        return new Vector2((float)(Math.Cos(angle) * distance), (float)(Math.Sin(angle) * distance));
    }

    private async Task SendAsync(CancellationToken stop) =>
        await _socket.SendAsync(_frame.WrittenMemory, WebSocketMessageType.Binary, endOfMessage: true, stop);

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[64 * 1024];
        var length = 0;
        try
        {
            while (true)
            {
                var result = await _socket.ReceiveAsync(buffer.AsMemory(length), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                length += result.Count;
                if (result.EndOfMessage)
                {
                    _frames.Enqueue(buffer.AsSpan(0, length).ToArray());
                    length = 0;
                }
                else if (length == buffer.Length)
                {
                    throw new ProtocolException("Server frame too large.");
                }
            }
        }
        catch (Exception e) when (e is WebSocketException or ProtocolException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }
}
