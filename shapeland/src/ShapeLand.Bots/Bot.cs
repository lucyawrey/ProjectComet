using System.Diagnostics;
using System.Numerics;
using Comet.Client;
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

    /// <summary>Ask for this body colour (0xRRGGBB) instead of a random one from the starting set.</summary>
    public uint? Colour { get; set; }
}

/// <summary>
/// One simulated player: joins, wanders the island with the shared player motor, jumps now and then and chats
/// occasionally. A cheating bot slides faster than its shape allows and should be snapped back. Runs on the
/// same client session as the Unity client (tick estimate, interpolation buffer, report schedule), so load
/// tests exercise that code too. Counts what it sees, so tests and the bot program can check the server.
/// </summary>
public sealed class Bot(string name, int seed, BotSettings settings, ShapeLandContent content, CollisionWorld world)
{
    private const int StepsPerSecond = 30;

    private static readonly string[] ChatLines = ["hello!", "nice island", "anyone up for a race?", "look, I can jump", "wheee", "brb"];

    private readonly Random _random = new(seed);
    private readonly HashSet<uint> _seen = [];
    private readonly PositionReporter _reporter = new();
    private ClientSession _session = null!;
    private MotorState _motor;
    private Shape _shape = null!;
    private Vector2 _target;
    private int _step;
    private double _nextJump;
    private double _nextChat;

    public string Name => name;

    public uint EntityId => _session.EntityId;

    public bool Joined { get; private set; }

    public JoinRejection? Rejected { get; private set; }

    /// <summary>Other players this bot has seen spawn.</summary>
    public IReadOnlyCollection<uint> SeenPlayers => _seen;

    public int StatesReceived => _session.StatesReceived;

    public int SnapBacks { get; private set; }

    public int Respawns { get; private set; }

    public int ChatsSent { get; private set; }

    public int ChatsReceived { get; private set; }

    public int Despawns { get; private set; }

    /// <summary>The connection failed or was lost, rather than being closed by the bot.</summary>
    public bool Failed { get; private set; }

    public async Task RunAsync(Uri url, CancellationToken stop)
    {
        var transport = new WebSocketTransport(url);
        _session = new ClientSession(transport, ShapeLandProtocol.Options, teleportSpeed: ShapeLandWorld.TeleportSpeed(content));
        _session.GameMessage += OnGameMessage;
        _session.Corrected += OnCorrected;
        _session.EntityDespawned += _ => Despawns++;

        var clock = Stopwatch.StartNew();
        var asked = false;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / StepsPerSecond));
        try
        {
            while (transport.State != TransportState.Closed && await timer.WaitForNextTickAsync(stop))
            {
                var now = clock.Elapsed.TotalSeconds;
                if (!asked && transport.State == TransportState.Open)
                {
                    asked = true;
                    _shape = content.Shapes.All[_random.Next(content.Shapes.All.Count)];
                    _session.Write(ShapeLandMessageIds.JoinRequest, new JoinRequest
                    {
                        Name = name,
                        Shape = _shape.Number,
                        Colour = settings.Colour ?? ShapeLandRules.BodyColours[_random.Next(ShapeLandRules.BodyColours.Length)],
                        EyeColour = ShapeLandRules.EyeColours[_random.Next(ShapeLandRules.EyeColours.Length)],
                    });
                }

                _session.Update(now);
                if (Joined)
                {
                    Step(now, 1f / StepsPerSecond);
                }

                _session.Flush(now);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }

        transport.Close();
        await transport.Completion;
        transport.Dispose();
        Failed = _session.Error is not null;
    }

    private void Step(double now, float seconds)
    {
        _step++;
        var elapsed = _step / (double)StepsPerSecond;
        var position = new Vector2(_motor.Position.X, _motor.Position.Z);
        if (Vector2.Distance(position, _target) < 1.5f)
        {
            _target = RandomPoint();
        }

        var move = Vector2.Normalize(_target - position);
        var jump = _motor.Grounded && elapsed >= _nextJump;
        if (jump)
        {
            _nextJump = elapsed + 3 + _random.NextDouble() * 5;
        }

        PlayerMotor.Step(ref _motor, move, jump, seconds, _shape.MaxSpeed * settings.SpeedCheat, _shape.JumpVelocity, world, ShapeLandWorld.Rules);
        if (_reporter.ShouldReport(_motor.Velocity, now))
        {
            _session.ReportPosition(_motor.Position, _motor.Velocity, _motor.Facing);
        }

        if (settings.ChatEverySeconds > 0 && elapsed >= _nextChat)
        {
            _session.Write(ShapeLandMessageIds.ChatSend, new ChatSend { Text = ChatLines[_random.Next(ChatLines.Length)] });
            ChatsSent++;
            _nextChat = elapsed + settings.ChatEverySeconds * (0.5 + _random.NextDouble());
        }

        // Draw the others, as a real client would, so the interpolation buffer runs under load.
        var renderTick = _session.RenderTick(now);
        foreach (var id in _session.Entities.Ids)
        {
            _session.Entities.TrySample(id, renderTick, out _);
        }
    }

    private void OnGameMessage(ushort messageId, ReadOnlyMemory<byte> payload, uint tick)
    {
        var options = ShapeLandProtocol.Options;
        switch (messageId)
        {
            case ShapeLandMessageIds.PlayerSpawn:
                var spawn = FrameReader.Decode<PlayerSpawn>(payload, options);
                var position = new Vector3(spawn.X, spawn.Y, spawn.Z);
                if (_session.Welcomed && spawn.EntityId == _session.EntityId)
                {
                    Joined = true;
                    _motor = new MotorState { Position = position, Grounded = true, Facing = spawn.Facing };
                    _target = RandomPoint();
                    _nextJump = 2 + _random.NextDouble() * 4;
                    _nextChat = settings.ChatEverySeconds * _random.NextDouble();
                }
                else
                {
                    _seen.Add(spawn.EntityId);
                    _session.Entities.Spawn(spawn.EntityId, tick, position, spawn.Facing);
                }

                break;
            case ShapeLandMessageIds.JoinRejected:
                Rejected = FrameReader.Decode<JoinRejected>(payload, options).Reason;
                break;
            case ShapeLandMessageIds.ChatMessage:
                ChatsReceived++;
                break;
        }
    }

    private void OnCorrected(PositionCorrection correction)
    {
        _motor.Position = new Vector3(correction.X, correction.Y, correction.Z);
        _motor.Velocity = Vector3.Zero;
        _motor.Grounded = false;
        _reporter.Reset();
        if (correction.Reason == CorrectionReason.Respawn)
        {
            Respawns++;
        }
        else
        {
            SnapBacks++;
        }
    }

    private Vector2 RandomPoint()
    {
        var angle = _random.NextDouble() * Math.Tau;
        var distance = settings.WanderRadius * Math.Sqrt(_random.NextDouble());
        return new Vector2((float)(Math.Cos(angle) * distance), (float)(Math.Sin(angle) * distance));
    }
}
