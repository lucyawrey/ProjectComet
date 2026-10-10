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

    /// <summary>Jump this many times faster than the shape allows (1 for an honest bot); the height goes with its square.</summary>
    public float JumpCheat { get; set; } = 1;

    /// <summary>Stay within this distance of the island's centre, in metres.</summary>
    public float WanderRadius { get; set; } = 18;

    /// <summary>Ask for this body colour (0xRRGGBB) instead of a random one from the starting set.</summary>
    public uint? Colour { get; set; }

    /// <summary>Seconds after joining before network numbers are recorded (the delay settles first); negative for none.</summary>
    public double WarmupSeconds { get; set; } = -1;
}

/// <summary>A bot's network numbers at one moment: seconds since it joined, and milliseconds for the rest.</summary>
public readonly record struct NetSample(double Seconds, double PingMs, double BestPingMs, double DelayMs, double TargetMs);

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
    private readonly HashSet<uint> _cheatersInView = [];
    private readonly PositionReporter _reporter = new();
    private readonly FixedStep _fixed = new(StepsPerSecond);
    private readonly List<(uint Tick, Vector3 Position)> _ownReports = [];
    private ClientSession _session = null!;
    private MotorState _motor;
    private Shape _shape = null!;
    private Vector2 _target;
    private int _step;
    private double _nextJump;
    private double _nextChat;
    private double _targetSince;
    private double _joinedAt;
    private double _lastNow;
    private bool _recording;
    private double _nextSample;
    private (long MoveStates, long Holds, double HeldTicks, long Jumps, long SpeedHitches, long Overruns, long VisibleBlendBacks) _baseline;
    private double _lastRecord;

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

    /// <summary>Network numbers sampled while running, for <c>--results</c>; empty until the warmup is over.</summary>
    public List<NetSample> NetSamples { get; } = [];

    /// <summary>When the bot started, in UTC: its samples' seconds count from here, to match them to timestamped server logs.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>Of the others' states received since the warmup, those that continued a move, and the holds among them.</summary>
    public long MoveStates => Joined && _recording ? _session.Entities.MoveStates - _baseline.MoveStates : 0;

    public long Holds => Joined && _recording ? _session.Entities.Holds - _baseline.Holds : 0;

    /// <summary>Of the others' moves since the warmup, those drawn as a jump (too far to walk between stamps).</summary>
    public long Jumps => Joined && _recording ? _session.Entities.Jumps - _baseline.Jumps : 0;

    /// <summary>This bot's own reports since the warmup, and how many changed speed as a hitch would (real changes).</summary>
    public long OwnMoves { get; private set; }

    public long OwnHitches { get; private set; }

    public long Overruns => Joined && _recording ? _session.Entities.Overruns - _baseline.Overruns : 0;

    public long VisibleBlendBacks => Joined && _recording ? _session.Entities.VisibleBlendBacks - _baseline.VisibleBlendBacks : 0;

    public long VisibleHitches => Overruns + VisibleBlendBacks + Jumps;

    /// <summary>Other players watched since the warmup, in player-seconds, to put visible hitches per player per minute.</summary>
    public double WatchedSeconds { get; private set; }

    public long SpeedHitches => Joined && _recording ? _session.Entities.SpeedHitches - _baseline.SpeedHitches : 0;

    /// <summary>How long the holds since the warmup were, in seconds, summed.</summary>
    public double HeldSeconds => Joined && _recording ? (_session.Entities.HeldTicks - _baseline.HeldTicks) / _session.Entities.TickRate : 0;

    public async Task RunAsync(Uri url, CancellationToken stop)
    {
        var transport = new WebSocketTransport(url);
        _session = new ClientSession(transport, ShapeLandProtocol.Options, teleportSpeed: ShapeLandWorld.TeleportSpeed(content),
            gravity: ShapeLandWorld.Rules.Gravity, groundHeight: ShapeLandWorld.GroundHeight(world));
        _session.GameMessage += OnGameMessage;
        _session.Corrected += OnCorrected;
        _session.EntityDespawned += id =>
        {
            Despawns++;
            _cheatersInView.Remove(id);
        };

        var clock = Stopwatch.StartNew();
        StartedAt = DateTimeOffset.UtcNow;
        var asked = false;
        // Wakes twice per step; the steps themselves run on the server's clock (FixedStep), on its ticks.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(0.5 / StepsPerSecond));
        try
        {
            while (transport.State != TransportState.Closed && await timer.WaitForNextTickAsync(stop))
            {
                var now = clock.Elapsed.TotalSeconds;
                _lastNow = now;
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
                    var steps = _fixed.Advance(_session.ServerSeconds(now));
                    for (var i = 0; i < steps; i++)
                    {
                        Step(now, _fixed.LastStep - (steps - 1 - i));
                    }

                    // Draw the others, as a real client would, so the interpolation buffer runs under load.
                    var renderTick = _session.RenderTick(now);
                    foreach (var id in _session.Entities.Ids)
                    {
                        _session.Entities.TrySample(id, renderTick, out _);
                    }

                    Record(now);
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

    private void Step(double now, long stepNumber)
    {
        const float seconds = 1f / StepsPerSecond;
        _step++;
        var elapsed = _step / (double)StepsPerSecond;
        var position = new Vector2(_motor.Position.X, _motor.Position.Z);
        // A new target on arrival, or after 30 s without getting there (stuck behind a block).
        if (Vector2.Distance(position, _target) < 1.5f || now - _targetSince > 30)
        {
            _target = RandomPoint();
            _targetSince = now;
        }

        var move = Vector2.Normalize(_target - position);
        var jump = _motor.Grounded && elapsed >= _nextJump;
        if (jump)
        {
            _nextJump = elapsed + 3 + _random.NextDouble() * 5;
        }

        PlayerMotor.Step(ref _motor, move, jump, seconds, _shape.MaxSpeed * settings.SpeedCheat, _shape.JumpVelocity * settings.JumpCheat, world, ShapeLandWorld.Rules);
        // Each step falls on a server tick, so its state is reported stamped with exactly that tick.
        if (_session.TickOfStep(stepNumber, StepsPerSecond, out var tick) && _reporter.ShouldReport(_motor.Velocity, stepNumber / (double)StepsPerSecond))
        {
            _session.ReportPosition(tick, _motor.Position, _motor.Velocity, _motor.Facing);
            CountOwnHitch(tick, _motor.Position);
        }

        if (settings.ChatEverySeconds > 0 && elapsed >= _nextChat)
        {
            _session.Write(ShapeLandMessageIds.ChatSend, new ChatSend { Text = ChatLines[_random.Next(ChatLines.Length)] });
            ChatsSent++;
            _nextChat = elapsed + settings.ChatEverySeconds * (0.5 + _random.NextDouble());
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
                    _joinedAt = _lastNow;
                    _motor = new MotorState { Position = position, Grounded = true, Facing = spawn.Facing };
                    _fixed.Reset();
                    _target = RandomPoint();
                    _nextJump = 2 + _random.NextDouble() * 4;
                    _nextChat = settings.ChatEverySeconds * _random.NextDouble();
                }
                else
                {
                    _seen.Add(spawn.EntityId);
                    _session.Entities.Spawn(spawn.EntityId, tick, position, spawn.Facing);
                    if (IsCheaterName(spawn.Name))
                    {
                        // A speed cheater's impossible moves would count as the network's hitches.
                        _session.Entities.ExcludeFromCounts(spawn.EntityId);
                        _cheatersInView.Add(spawn.EntityId);
                    }
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

    /// <summary>Whether a player's name is a cheating bot's, as Program names them ("Cheater 01", "Far cheater 01").</summary>
    public static bool IsCheaterName(string name) =>
        name.StartsWith("Cheater ", StringComparison.Ordinal) || name.Contains(" cheater ", StringComparison.Ordinal);

    // The speed-hitch test RemoteEntities applies to others' states, applied to this bot's own reports: what its
    // real movement does, to compare with what others draw.
    private void CountOwnHitch(uint tick, Vector3 position)
    {
        if (_recording && _ownReports.Count == 2)
        {
            var (t0, p0) = _ownReports[0];
            var (t1, p1) = _ownReports[1];
            var was = GroundSpeed(p0, p1, t1 - t0);
            var now = GroundSpeed(p1, position, tick - t1);
            OwnMoves++;
            if (was > 1 && now > 1 && (now > was * 1.3f || now < was / 1.3f))
            {
                OwnHitches++;
            }
        }

        if (_ownReports.Count == 2)
        {
            _ownReports.RemoveAt(0);
        }

        _ownReports.Add((tick, position));
    }

    private static float GroundSpeed(Vector3 from, Vector3 to, uint ticks) =>
        ticks == 0 ? 0 : new Vector2(to.X - from.X, to.Z - from.Z).Length() / (ticks / 30f);

    // After the warmup, the network numbers the overlay shows, four times a second.
    private void Record(double now)
    {
        if (settings.WarmupSeconds < 0 || now < _joinedAt + settings.WarmupSeconds)
        {
            return;
        }

        var entities = _session.Entities;
        if (!_recording)
        {
            _recording = true;
            _baseline = (entities.MoveStates, entities.Holds, entities.HeldTicks, entities.Jumps, entities.SpeedHitches, entities.Overruns, entities.VisibleBlendBacks);
            _lastRecord = now;
            _nextSample = now;
        }

        WatchedSeconds += (entities.Count - _cheatersInView.Count) * (now - _lastRecord); // as counted: not cheaters
        _lastRecord = now;
        if (now < _nextSample)
        {
            return;
        }

        _nextSample += 0.25;
        var delay = _session.InterpolationDelay;
        NetSamples.Add(new NetSample(
            now - _joinedAt,
            _session.Clock.LastRoundTrip * 1000,
            _session.Clock.RoundTrip * 1000,
            delay.Seconds * 1000,
            delay.Target / delay.TickRate * 1000));
    }

    private Vector2 RandomPoint()
    {
        var angle = _random.NextDouble() * Math.Tau;
        var distance = settings.WanderRadius * Math.Sqrt(_random.NextDouble());
        return new Vector2((float)(Math.Cos(angle) * distance), (float)(Math.Sin(angle) * distance));
    }
}
