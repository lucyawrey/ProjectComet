using System.Numerics;
using Comet.Protocol.Messages;
using Comet.Simulation;

namespace Comet.Server.Movement;

/// <summary>What a position report's check found.</summary>
public enum MovementVerdict
{
    Accepted,

    /// <summary>Sent before the client applied the latest correction; ignored.</summary>
    Stale,

    /// <summary>Moved further than the speed budget allows.</summary>
    TooFast,

    /// <summary>Higher than gravity allows: above the arc of a jump from the last ground at this point in it.</summary>
    TooHigh,

    /// <summary>The body overlaps a solid box.</summary>
    InsideBox,

    /// <summary>Below the terrain surface.</summary>
    UnderGround,

    /// <summary>Fell below the world's kill height; respawn.</summary>
    FellOut,
}

/// <summary>What <see cref="MovementValidator.Fall"/> did this tick.</summary>
public enum FallResult
{
    /// <summary>Nothing: the player is on the ground, or still reporting.</summary>
    None,

    /// <summary>The server moved a silent airborne player; send their new state.</summary>
    Moved,

    /// <summary>The server's fall landed; send the state, and the client the correction from <see cref="MovementValidator.Landing"/>.</summary>
    Landed,

    /// <summary>The server's fall went below the kill height; respawn.</summary>
    FellOut,
}

/// <summary>How lenient the checks are; tuned in the prototype.</summary>
public sealed class MovementTolerances
{
    /// <summary>Allowed speed as a multiple of the shape's max speed.</summary>
    public float SpeedFactor { get; set; } = 1.2f;

    /// <summary>How much unused movement can build up, in seconds at the allowed speed (covers bursts after a stall).</summary>
    public float MaxBurstSeconds { get; set; } = 1f;

    /// <summary>Extra distance always allowed, in metres (rounding, small corrections).</summary>
    public float DistanceSlack { get; set; } = 0.25f;

    /// <summary>Allowed jump height as a multiple of the shape's jump apex.</summary>
    public float JumpFactor { get; set; } = 1.2f;

    /// <summary>
    /// How long, in seconds, a takeoff may come after the last report on the ground: reports are paced, so the
    /// gravity arc starts this much later than that report, which only ever makes it more lenient.
    /// </summary>
    public float AirTimeSlack { get; set; } = 0.3f;

    /// <summary>
    /// Seconds without a report before the server moves an airborne player itself. Longer than a TCP resend
    /// stall at a bad connection's round trip (about 200 ms with 2% loss), which 0.5 s wasn't (prototype.md, 3c).
    /// </summary>
    public float SilenceSeconds { get; set; } = 1.0f;

    /// <summary>Extra height always allowed above a jump, in metres.</summary>
    public float HeightSlack { get; set; } = 0.3f;

    /// <summary>How far below the terrain surface a report may be, in metres.</summary>
    public float GroundSlack { get; set; } = 0.3f;
}

/// <summary>
/// Checks one player's position reports (the client is the authority on its own movement, within these rules;
/// netcode.md). Speed uses a distance budget: each server tick earns max speed × the speed factor, each report
/// spends the distance it moved, and the budget is capped, so a burst of reports after a stall passes but
/// banking movement for a dash doesn't. Height follows gravity: in the air, a report must stay under the arc of
/// a jump from the last ground the player stood on, at that point in time (the report's stamp). The server never
/// steers, but it knows gravity: a player silent in the air for <see cref="MovementTolerances.SilenceSeconds"/>
/// is moved down that arc by the server (<see cref="Fall"/>) until they land, and their client is corrected
/// there; on the ground, silence just means standing still. Used from the tick thread only.
/// </summary>
public sealed class MovementValidator
{
    private readonly CollisionWorld _world;
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

    /// <summary>The last accepted position (where a snap-back returns to).</summary>
    public Vector3 Position { get; private set; }

    public float Facing { get; private set; }

    /// <summary>The last accepted position standing on solid ground (where a respawn returns to).</summary>
    public Vector3 SafePosition { get; private set; }

    /// <summary>The latest correction sent; reports echoing an older one are stale.</summary>
    public uint CorrectionSequence { get; private set; }

    /// <summary>Rejected reports so far (not counting stale ones or falls).</summary>
    public int Violations { get; private set; }

    /// <summary>True while the server is moving the player down a fall, because their client went silent in the air.</summary>
    public bool ServerFalling => _serverFalling;

    /// <summary>Places the player, standing on the ground at <paramref name="position"/>, e.g. at spawn.</summary>
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

    /// <summary>
    /// Checks a report received at server tick <paramref name="tick"/>, accepting it if it passes.
    /// <paramref name="stamp"/> is when the client sent it (bounded by <see cref="StateStamp"/>), which times the
    /// gravity arc, so late reports after a stall aren't judged by when they arrived; it defaults to the arrival.
    /// </summary>
    public MovementVerdict Check(in PositionReport report, uint tick, float maxSpeed, float jumpVelocity, uint? stamp = null)
    {
        if (report.CorrectionSequence != CorrectionSequence)
        {
            return MovementVerdict.Stale;
        }

        var allowedSpeed = maxSpeed * _tolerances.SpeedFactor;
        var elapsed = (float)(tick - _lastTick) / _tickRate;
        _lastTick = tick;
        _budget = MathF.Min(_budget + allowedSpeed * elapsed, allowedSpeed * _tolerances.MaxBurstSeconds + _tolerances.DistanceSlack);

        // Standing still sends nothing, and the first report after it is sent as the player starts moving, so a
        // player last seen on the ground was still there when this report was sent.
        if (!_airborne)
        {
            _groundTick = Math.Max(_groundTick, stamp ?? tick);
        }

        var position = new Vector3(report.X, report.Y, report.Z);
        if (position.Y < _world.KillHeight)
        {
            return MovementVerdict.FellOut;
        }

        var distance = Vector2.Distance(new Vector2(Position.X, Position.Z), new Vector2(position.X, position.Z));
        var verdict =
            distance > _budget ? MovementVerdict.TooFast
            : position.Y > _groundY + AllowedRise(stamp ?? tick, jumpVelocity) ? MovementVerdict.TooHigh
            : _world.IsBlocked(position, _rules.BodyRadius, _rules.BodyHeight) ? MovementVerdict.InsideBox
            : _world.Terrain != null && _world.Terrain.TryGetHeight(position.X, position.Z, out var terrain) && position.Y < terrain - _tolerances.GroundSlack ? MovementVerdict.UnderGround
            : MovementVerdict.Accepted;

        if (verdict != MovementVerdict.Accepted)
        {
            Violations++;
            return verdict;
        }

        _budget -= distance;
        Position = position;
        Facing = report.Facing;
        _velocity = new Vector3(report.VelocityX, report.VelocityY, report.VelocityZ);
        _lastReportTick = tick;
        _airborne = !Land(position, stamp ?? tick);
        return MovementVerdict.Accepted;
    }

    // Notes the ground under an accepted position, if it's standing on some; returns whether it is.
    private bool Land(Vector3 position, uint tick)
    {
        if (_world.TryGetGround(position, _rules.BodyRadius, position.Y + _tolerances.HeightSlack, out var ground) && position.Y - ground <= _tolerances.HeightSlack)
        {
            _groundY = ground;
            _groundTick = Math.Max(_groundTick, tick);
            SafePosition = position;
            return true;
        }

        return false;
    }

    // Seconds since the last ground, less the takeoff slack: how far into a jump a moment at tick can be.
    private float AirTime(uint tick) =>
        MathF.Max(0, (tick > _groundTick ? tick - _groundTick : 0) / (float)_tickRate - _tolerances.AirTimeSlack);

    /// <summary>
    /// How far above the last ground a player can be at <paramref name="tick"/>: a full jump (with the tolerances)
    /// until it would peak, then falling under gravity from there.
    /// </summary>
    private float AllowedRise(uint tick, float jumpVelocity)
    {
        var top = _rules.JumpApex(jumpVelocity) * _tolerances.JumpFactor + _tolerances.HeightSlack;
        var falling = AirTime(tick) - jumpVelocity / _rules.Gravity;
        return falling <= 0 ? top : top - 0.5f * _rules.Gravity * falling * falling;
    }

    /// <summary>
    /// Called every server tick. Once an airborne player has been silent for
    /// <see cref="MovementTolerances.SilenceSeconds"/>, moves them down their fall with the shared movement code:
    /// from their last reported velocity (its upward part capped by the arc), sideways as if they still held the
    /// keys, until they land or fall out. Reports sent before the landing correction are ignored meanwhile.
    /// </summary>
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
            CorrectionSequence++; // the silent client's reports from before the landing correction are stale
            var maxUp = jumpVelocity - _rules.Gravity * AirTime(_lastReportTick);
            _fall = new MotorState
            {
                Position = Position,
                Velocity = new Vector3(_velocity.X, MathF.Min(_velocity.Y, maxUp), _velocity.Z),
                Grounded = false,
                Facing = Facing,
            };
            _fallMove = maxSpeed > 0 ? new Vector2(_velocity.X, _velocity.Z) / maxSpeed : Vector2.Zero;

            // The fall began at the last report, so catch up on the silence first: the player is where that arc
            // has taken them by now, rather than hanging at the last report and then dropping.
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

    // One server tick of a server fall; null while still in the air.
    private FallResult? StepFall(float maxSpeed, float jumpVelocity)
    {
        PlayerMotor.Step(ref _fall, _fallMove, jump: false, 1f / _tickRate, maxSpeed, jumpVelocity, _world, _rules);
        Position = _fall.Position;
        Facing = _fall.Facing;
        return Position.Y < _world.KillHeight ? FallResult.FellOut : _fall.Grounded ? FallResult.Landed : null;
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

    /// <summary>The correction after a server fall lands: to where it landed, with the sequence it began.</summary>
    public PositionCorrection Landing() => new()
    {
        Sequence = CorrectionSequence,
        Reason = CorrectionReason.SnapBack,
        X = Position.X,
        Y = Position.Y,
        Z = Position.Z,
        Facing = Facing,
    };

    /// <summary>
    /// Builds the correction for a rejected report or a fall: back to the last accepted position, or for a fall to
    /// the last safe ground. Later reports must echo its sequence.
    /// </summary>
    public PositionCorrection Correct(CorrectionReason reason, uint tick)
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
            X = target.X,
            Y = target.Y,
            Z = target.Z,
            Facing = Facing,
        };
    }
}
