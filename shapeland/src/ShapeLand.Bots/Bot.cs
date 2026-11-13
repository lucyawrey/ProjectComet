using System.Diagnostics;
using System.Numerics;
using Comet.Client;
using Comet.Content;
using Comet.Protocol.Framing;
using Comet.Protocol.Messages;
using Comet.Simulation;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;

namespace ShapeLand.Bots;

public sealed class BotSettings
{
    public double ChatEverySeconds { get; set; } = 30;

    public float SpeedCheat { get; set; } = 1;

    public float JumpCheat { get; set; } = 1;

    public double WarmupSeconds { get; set; } = -1;
}

public readonly record struct NetSample(double Seconds, double PingMs, double BestPingMs, double DelayMs, double TargetMs);

public sealed class Bot(string name, int seed, BotSettings settings, ShapeLandContent content, ZoneWorlds worlds, IBotAccount account)
{
    private const int StepsPerSecond = 30;

    private static readonly string[] ChatLines = ["hello!", "nice island", "anyone up for a race?", "look, I can jump", "wheee", "brb"];

    private readonly Random _random = new(seed);
    private readonly Random _routeRandom = new(seed + 1);
    private readonly HashSet<uint> _seen = [];
    private readonly HashSet<uint> _cheatersInView = [];
    private readonly OwnMovement _movement = new(StepsPerSecond);
    private readonly FixedStep _fixed = new(StepsPerSecond);
    private readonly List<(uint Tick, Vector3 Position)> _ownReports = [];
    private ClientSession _session = null!;

    private CollisionWorld _world = worlds.For(content.Zones.All[0]).WithOwnFlips();
    private MotorState _motor;
    private Shape _shape = null!;
    private Task<Wanderer>? _wandering;
    private Wanderer? _wanderer;
    private int _step;
    private double _nextJump;
    private double _nextChat;
    private double _joinedAt;
    private double _lastNow;
    private bool _recording;
    private double _nextSample;
    private (long MoveStates, long Holds, double HeldTicks, long Jumps, long SpeedHitches, long Overruns, long VisibleBlendBacks) _baseline;
    private double _lastRecord;

    public string Name { get; private set; } = name;

    private bool Calm => settings is { SpeedCheat: <= 1, JumpCheat: <= 1 };

    public string? LoginFailure { get; private set; }

    public DropReason? DroppedFor { get; private set; }

    public uint EntityId => _session.EntityId;

    public bool Joined { get; private set; }

    public JoinRejection? Rejected { get; private set; }

    public IReadOnlyCollection<uint> SeenPlayers => _seen;

    public int StatesReceived => _session.StatesReceived;

    public int SnapBacks { get; private set; }

    public int Respawns { get; private set; }

    public int ChatsSent { get; private set; }

    public int ChatsReceived { get; private set; }

    public bool Failed { get; private set; }

    public List<NetSample> NetSamples { get; } = [];

    public DateTimeOffset StartedAt { get; private set; }

    public long MoveStates => Joined && _recording ? _session.Entities.MoveStates - _baseline.MoveStates : 0;

    public long Holds => Joined && _recording ? _session.Entities.Holds - _baseline.Holds : 0;

    public long Jumps => Joined && _recording ? _session.Entities.Jumps - _baseline.Jumps : 0;

    public long OwnMoves { get; private set; }

    public long OwnHitches { get; private set; }

    public long Overruns => Joined && _recording ? _session.Entities.Overruns - _baseline.Overruns : 0;

    public long VisibleBlendBacks => Joined && _recording ? _session.Entities.VisibleBlendBacks - _baseline.VisibleBlendBacks : 0;

    public long VisibleHitches => Overruns + VisibleBlendBacks + Jumps;

    public double WatchedSeconds { get; private set; }

    public long SpeedHitches => Joined && _recording ? _session.Entities.SpeedHitches - _baseline.SpeedHitches : 0;

    public double HeldSeconds => Joined && _recording ? (_session.Entities.HeldTicks - _baseline.HeldTicks) / _session.Entities.TickRate : 0;

    public async Task RunAsync(CancellationToken stop)
    {
        BotJoin? ticket;
        string? failure;
        try
        {
            (ticket, failure) = await account.JoinAsync(stop);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return;
        }

        if (ticket == null)
        {
            LoginFailure = failure;
            Failed = true;
            Console.Error.WriteLine($"{Name} couldn't sign in: {failure}");
            return;
        }

        Name = ticket.Name;
        var transport = new WebSocketTransport(ticket.Server);
        _session = new ClientSession(
            transport,
            ShapeLandProtocol.Options,
            teleportSpeed: ShapeLandWorld.TeleportSpeed(content),
            gravity: content.Movement.Gravity
        );
        _session.GameMessage += OnGameMessage;
        _session.Corrected += OnCorrected;
        _session.EntityDespawned += id => _cheatersInView.Remove(id);

        var clock = Stopwatch.StartNew();
        StartedAt = DateTimeOffset.UtcNow;
        var asked = false;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(0.5 / StepsPerSecond));
        try
        {
            while (transport.State != TransportState.Closed && Rejected == null && await timer.WaitForNextTickAsync(stop))
            {
                var now = clock.Elapsed.TotalSeconds;
                _lastNow = now;
                if (!asked && transport.State == TransportState.Open)
                {
                    asked = true;
                    if (ticket.Ticket is { } joinTicket)
                    {
                        _session.Write(
                            ShapeLandMessageIds.JoinRequest,
                            new JoinRequest { Ticket = joinTicket, ContentFingerprint = content.Fingerprint }
                        );
                    }
                    else
                    {
                        var look = ticket.Look ?? new NewCharacter();
                        _session.Write(
                            ShapeLandMessageIds.DevJoinRequest,
                            new DevJoinRequest
                            {
                                Name = ticket.Name,
                                Shape = look.Shape,
                                Color = look.Color,
                                EyeColor = look.EyeColor,
                                ContentFingerprint = content.Fingerprint,
                                Bot = true,
                            }
                        );
                    }
                }

                _session.Update(now);
                if (Joined)
                {
                    var steps = _fixed.Advance(_session.ServerSeconds(now));
                    for (var i = 0; i < steps; i++)
                    {
                        Step(now, _fixed.LastStep - (steps - 1 - i));
                    }

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
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }

        transport.Close();
        await transport.Completion;
        transport.Dispose();
        Failed = _session.CloseReason != CloseReason.None;
    }

    private void Step(double now, long stepNumber)
    {
        _step++;
        var elapsed = _step / (double)StepsPerSecond;
        if (_wandering is { IsCompleted: true } built)
        {
            if (built.IsFaulted)
            {
                Console.Error.WriteLine($"{Name} can't wander: {built.Exception!.GetBaseException().Message}");
            }
            else
            {
                _wanderer = built.Result;
            }

            _wandering = null;
        }

        var move = _wanderer?.Steer(now, _motor.Position, _shape.MaxSpeed * settings.SpeedCheat / StepsPerSecond) ?? Vector2.Zero;
        var jump = _motor.Grounded && elapsed >= _nextJump && _wanderer?.CanJump(_motor.Position, move) == true;
        if (jump)
        {
            _nextJump = elapsed + (Calm ? 10 + _random.NextDouble() * 15 : 3 + _random.NextDouble() * 5);
        }

        if (
            _movement.Step(
                _session,
                stepNumber,
                ref _motor,
                move,
                jump,
                _shape.MaxSpeed * settings.SpeedCheat,
                _shape.JumpVelocity * settings.JumpCheat,
                _world,
                content.Movement,
                out var tick
            )
        )
        {
            CountOwnHitch(tick, _motor.Position);
        }

        if (settings.ChatEverySeconds > 0 && elapsed >= _nextChat)
        {
            _session.Write(ShapeLandMessageIds.ChatSend, new ChatSend { Text = ChatLines[_random.Next(ChatLines.Length)] });
            ChatsSent++;
            _nextChat = elapsed + settings.ChatEverySeconds * (0.5 + _random.NextDouble());
        }
    }

    private async Task<Wanderer> WanderIn(Zone zone) => new(await worlds.GridFor(zone), worlds.TriggersFor(zone), _routeRandom, Calm);

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
                    _shape = content.Shapes[spawn.Shape];
                    if (Joined)
                    {
                        break;
                    }

                    Joined = true;
                    _joinedAt = _lastNow;
                    _motor = new MotorState
                    {
                        Position = position,
                        Grounded = true,
                        Facing = spawn.Facing,
                    };
                    _fixed.Reset();
                    _nextJump = 2 + _random.NextDouble() * 4;
                    _nextChat = settings.ChatEverySeconds * _random.NextDouble();
                }
                else
                {
                    _seen.Add(spawn.EntityId);
                    _session.Entities.Spawn(spawn.EntityId, tick, position, spawn.Facing);
                    if (IsCheaterName(spawn.Name))
                    {
                        _session.Entities.ExcludeFromCounts(spawn.EntityId);
                        _cheatersInView.Add(spawn.EntityId);
                    }
                }

                break;
            case ShapeLandMessageIds.ZoneEntered:
                var entered = FrameReader.Decode<ZoneEntered>(payload, options);
                var zone = content.Zones[entered.Zone];
                _world = worlds.For(zone).WithOwnFlips();
                _wanderer = null;
                _wandering = WanderIn(zone);
                _movement.PassThrough.Clear();
                _session.Entities.GroundHeight = _world.GroundHeight(content.Movement);
                foreach (var tag in entered.Flipped ?? [])
                {
                    _world.SetFlipped(tag, true);
                }

                break;
            case ShapeLandMessageIds.PlacementsFlipped:
                var flipped = FrameReader.Decode<PlacementsFlipped>(payload, options);
                _world.SetFlipped(flipped.Tag, flipped.Flipped);
                _movement.PassThrough.Flipped(_world, flipped.Tag, _motor.Position, content.Movement.BodyRadius, content.Movement.BodyHeight);
                break;
            case ShapeLandMessageIds.Dropped:
                DroppedFor = FrameReader.Decode<Dropped>(payload, options).Reason;
                break;
            case ShapeLandMessageIds.JoinRejected:
                Rejected = FrameReader.Decode<JoinRejected>(payload, options).Reason;
                Console.Error.WriteLine($"{Name} was refused: {Rejected}");
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
        _movement.Reporter.Reset();
        if (correction.Reason == CorrectionReason.Respawn)
        {
            Respawns++;
        }
        else if (correction.Reason != CorrectionReason.Teleport)
        {
            SnapBacks++;
        }
    }

    public static bool IsCheaterName(string name) =>
        name.StartsWith("Cheater", StringComparison.Ordinal) || name.Contains("-cheater", StringComparison.Ordinal);

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
            _baseline = (
                entities.MoveStates,
                entities.Holds,
                entities.HeldTicks,
                entities.Jumps,
                entities.SpeedHitches,
                entities.Overruns,
                entities.VisibleBlendBacks
            );
            _lastRecord = now;
            _nextSample = now;
        }

        WatchedSeconds += (entities.Count - _cheatersInView.Count) * (now - _lastRecord);
        _lastRecord = now;
        if (now < _nextSample)
        {
            return;
        }

        _nextSample += 0.25;
        var delay = _session.InterpolationDelay;
        NetSamples.Add(
            new NetSample(
                now - _joinedAt,
                _session.Clock.LastRoundTrip * 1000,
                _session.Clock.RoundTrip * 1000,
                delay.Seconds * 1000,
                delay.Target / delay.TickRate * 1000
            )
        );
    }
}
