using System.Numerics;
using Comet.Protocol.Messages;
using Comet.Simulation;

namespace Comet.Server.Movement;

public enum FallResult
{
    None,

    Moved,

    Landed,

    FellOut,
}

public sealed class MovementTolerances
{
    public float SpeedFactor { get; set; } = 1.1f;

    /// <summary>How much unused movement can build up, in seconds at the allowed speed (covers bursts after a stall).</summary>
    public float MaxBurstSeconds { get; set; } = 1f;

    /// <summary>Extra distance always allowed, in meters (rounding, small corrections).</summary>
    public float DistanceSlack { get; set; } = 0.25f;

    public float JumpFactor { get; set; } = 1.0f;

    public float AirTimeSlack { get; set; } = 0.2f;

    public float SilenceSeconds { get; set; } = 1.0f;

    /// <summary>Extra height always allowed above a jump, in meters.</summary>
    public float HeightSlack { get; set; } = 0.4f;
}

public sealed class MovementValidator
{
    private CollisionWorld _world;
    private readonly MovementRules _rules;
    private readonly MovementTolerances _tolerances;
    private readonly int _tickRate;
    private float _budget;
    private uint _lastTick;
    private float _groundY;
    private uint _groundTick;
    private uint _lastReportTick;
    private bool _airborne;
    private Vector3 _velocity;
    private bool _serverFalling;
    private MotorState _fall;
    private Vector2 _fallMove;

    public MovementValidator(CollisionWorld world, MovementRules rules, MovementTolerances tolerances, int tickRate)
    {
        _world = world;
        _rules = rules;
        _tolerances = tolerances;
        _tickRate = tickRate;
    }

    public Vector3 Position { get; private set; }

    public Vector3 Velocity => _serverFalling ? _fall.Velocity : _velocity;

    /// <summary>The last reported facing, as a yaw angle in radians.</summary>
    public float Facing { get; private set; }

    public Vector3 SafePosition { get; private set; }

    public uint CorrectionSequence { get; private set; }

    public int Violations { get; private set; }

    public MovementHeadroom Headroom { get; } = new();

    public CollisionWorld World => _world;

    public PassThrough PassThrough { get; } = new();

    public bool ServerFalling => _serverFalling;

    public void Reset(Vector3 position, float facing, uint tick)
    {
        Position = position;
        SafePosition = position;
        Facing = facing;
        _groundY = position.Y;
        _groundTick = tick;
        _lastTick = tick;
        _lastReportTick = tick;
        _airborne = false;
        _serverFalling = false;
        _velocity = Vector3.Zero;
        _budget = _tolerances.DistanceSlack;
    }

    /// <summary><paramref name="stamp"/> is when the client sent the report, so a late one after a stall isn't judged by its arrival.</summary>
    public MovementVerdict Check(in PositionReport report, uint tick, float maxSpeed, float jumpVelocity, uint? stamp = null)
    {
        if (report.CorrectionSequence != CorrectionSequence || _serverFalling)
        {
            return MovementVerdict.Stale;
        }

        if (!IsFinite(report))
        {
            Violations++;
            return MovementVerdict.NotFinite;
        }

        var allowedSpeed = maxSpeed * _tolerances.SpeedFactor;
        var elapsed = (float)(tick - _lastTick) / _tickRate;
        _lastTick = tick;
        _budget = MathF.Min(_budget + allowedSpeed * elapsed, allowedSpeed * _tolerances.MaxBurstSeconds + _tolerances.DistanceSlack);

        if (!_airborne)
        {
            _groundTick = Math.Max(_groundTick, stamp ?? tick);
        }

        var position = new Vector3(report.X, report.Y, report.Z);
        if (position.Y < _world.KillHeight)
        {
            return MovementVerdict.FellOut;
        }

        var air = stamp ?? tick;
        Headroom.AddHeight(position.Y - _groundY, (air > _groundTick ? air - _groundTick : 0) / (float)_tickRate, jumpVelocity, _rules.Gravity);

        var distance = Vector2.Distance(new Vector2(Position.X, Position.Z), new Vector2(position.X, position.Z));
        var verdict =
            distance > _budget ? MovementVerdict.TooFast
            : position.Y > _groundY + AllowedRise(stamp ?? tick, jumpVelocity) ? MovementVerdict.TooHigh
            : _world.IsBlocked(position, _rules.BodyRadius, _rules.BodyHeight, PassThrough) ? MovementVerdict.InsideSolid
            : MovementVerdict.Accepted;

        if (verdict != MovementVerdict.Accepted)
        {
            Violations++;
            return verdict;
        }

        Headroom.AddMove(distance, elapsed, maxSpeed);

        _budget -= distance;
        Position = position;
        Facing = WrapAngle(report.Facing);
        _velocity = AllowedVelocity(report, allowedSpeed, jumpVelocity, stamp ?? tick);
        _lastReportTick = tick;
        _airborne = !Land(position, stamp ?? tick);
        return MovementVerdict.Accepted;
    }

    private static bool IsFinite(in PositionReport report) =>
        float.IsFinite(report.X)
        && float.IsFinite(report.Y)
        && float.IsFinite(report.Z)
        && float.IsFinite(report.VelocityX)
        && float.IsFinite(report.VelocityY)
        && float.IsFinite(report.VelocityZ)
        && float.IsFinite(report.Facing);

    private static float WrapAngle(float radians) => (float)Math.IEEERemainder(radians, 2 * Math.PI);

    private Vector3 AllowedVelocity(in PositionReport report, float allowedSpeed, float jumpVelocity, uint tick)
    {
        var sideways = new Vector2(report.VelocityX, report.VelocityZ);
        if (sideways.Length() > allowedSpeed)
        {
            sideways = Vector2.Normalize(sideways) * allowedSpeed;
        }

        var airSeconds = (tick > _groundTick ? tick - _groundTick : 0) / (float)_tickRate + _tolerances.AirTimeSlack;
        var up = jumpVelocity - _rules.Gravity * AirTime(tick);
        var down = -(jumpVelocity + _rules.Gravity * airSeconds);
        return new Vector3(sideways.X, Math.Clamp(report.VelocityY, down, up), sideways.Y);
    }

    private bool Land(Vector3 position, uint tick)
    {
        var landed =
            _world.TryGetGround(position, _rules.BodyRadius, position.Y + _tolerances.HeightSlack, out var ground, 0, PassThrough)
            && position.Y - ground <= _tolerances.HeightSlack;
        if (!landed && _world.IsWedged(position, _rules.BodyRadius, _rules.BodyHeight, PassThrough))
        {
            landed = true;
            ground = position.Y;
        }

        if (landed)
        {
            _groundY = ground;
            _groundTick = Math.Max(_groundTick, tick);
            SafePosition = position;
            return true;
        }

        return false;
    }

    private float AirTime(uint tick) => MathF.Max(0, (tick > _groundTick ? tick - _groundTick : 0) / (float)_tickRate - _tolerances.AirTimeSlack);

    private float AllowedRise(uint tick, float jumpVelocity)
    {
        var top = _rules.JumpApex(jumpVelocity) * _tolerances.JumpFactor + _rules.MantleHeight + _tolerances.HeightSlack;
        var falling = AirTime(tick) - jumpVelocity / _rules.Gravity;
        return falling <= 0 ? top : top - 0.5f * _rules.Gravity * falling * falling;
    }

    public FallResult Fall(uint tick, float maxSpeed, float jumpVelocity)
    {
        if (!_airborne)
        {
            return FallResult.None;
        }

        if (!_serverFalling)
        {
            if (tick - _lastReportTick < _tolerances.SilenceSeconds * _tickRate)
            {
                return FallResult.None;
            }

            _serverFalling = true;
            CorrectionSequence++;
            var maxUp = jumpVelocity - _rules.Gravity * AirTime(_lastReportTick);
            _fall = new MotorState
            {
                Position = Position,
                Velocity = new Vector3(_velocity.X, MathF.Min(_velocity.Y, maxUp), _velocity.Z),
                Grounded = false,
                Facing = Facing,
            };
            _fallMove = maxSpeed > 0 ? new Vector2(_velocity.X, _velocity.Z) / maxSpeed : Vector2.Zero;

            for (var t = _lastReportTick + 1; t < tick; t++)
            {
                if (StepFall(maxSpeed, jumpVelocity) is { } early)
                {
                    return Finish(early, tick);
                }
            }
        }

        return StepFall(maxSpeed, jumpVelocity) is { } result ? Finish(result, tick) : FallResult.Moved;
    }

    private FallResult? StepFall(float maxSpeed, float jumpVelocity)
    {
        PlayerMotor.Step(ref _fall, _fallMove, jump: false, 1f / _tickRate, maxSpeed, jumpVelocity, _world, _rules, PassThrough);
        Position = _fall.Position;
        Facing = _fall.Facing;
        return Position.Y < _world.KillHeight ? FallResult.FellOut
            : _fall.Grounded ? FallResult.Landed
            : null;
    }

    private FallResult Finish(FallResult result, uint tick)
    {
        if (result == FallResult.FellOut)
        {
            _serverFalling = false;
            return FallResult.FellOut;
        }

        _serverFalling = false;
        _airborne = false;
        _velocity = Vector3.Zero;
        Land(Position, tick);
        _lastTick = tick;
        _lastReportTick = tick;
        _budget = _tolerances.DistanceSlack;
        return FallResult.Landed;
    }

    public PositionCorrection Landing() =>
        new()
        {
            Sequence = CorrectionSequence,
            Reason = CorrectionReason.SnapBack,
            X = Position.X,
            Y = Position.Y,
            Z = Position.Z,
            Facing = Facing,
        };

    public PositionCorrection Teleport(
        Vector3 position,
        float facing,
        uint tick,
        CorrectionReason reason = CorrectionReason.Respawn,
        CollisionWorld? world = null
    )
    {
        if (world != null && world != _world)
        {
            _world = world;
            PassThrough.Clear();
        }

        CorrectionSequence++;
        Reset(position, facing, tick);
        return new PositionCorrection
        {
            Sequence = CorrectionSequence,
            Reason = reason,
            X = position.X,
            Y = position.Y,
            Z = position.Z,
            Facing = facing,
        };
    }

    public PositionCorrection Correct(CorrectionReason reason, uint tick, MovementVerdict verdict = MovementVerdict.Accepted)
    {
        CorrectionSequence++;
        var target = reason == CorrectionReason.Respawn ? SafePosition : Position;
        if (reason == CorrectionReason.Respawn)
        {
            Reset(target, Facing, tick);
        }
        else
        {
            _lastTick = tick;
        }

        return new PositionCorrection
        {
            Sequence = CorrectionSequence,
            Reason = reason,
            Verdict = verdict,
            X = target.X,
            Y = target.Y,
            Z = target.Z,
            Facing = Facing,
        };
    }
}
