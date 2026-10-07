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

    /// <summary>Rose higher than a jump from the last ground can reach.</summary>
    TooHigh,

    /// <summary>The body overlaps a solid box.</summary>
    InsideBox,

    /// <summary>Below the terrain surface.</summary>
    UnderGround,

    /// <summary>Fell below the world's kill height; respawn.</summary>
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

    /// <summary>Extra height always allowed above a jump, in metres.</summary>
    public float HeightSlack { get; set; } = 0.3f;

    /// <summary>How far below the terrain surface a report may be, in metres.</summary>
    public float GroundSlack { get; set; } = 0.3f;
}

/// <summary>
/// Checks one player's position reports (the client is the authority on its own movement, within these rules;
/// netcode.md). Speed uses a distance budget: each server tick earns max speed × the speed factor, each report
/// spends the distance it moved, and the budget is capped, so a burst of reports after a stall passes but
/// banking movement for a dash doesn't. Height is limited to a jump from the last ground the player stood on.
/// Used from the tick thread only.
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

    /// <summary>Places the player, standing on the ground at <paramref name="position"/>, e.g. at spawn.</summary>
    public void Reset(Vector3 position, float facing, uint tick)
    {
        Position = position;
        SafePosition = position;
        Facing = facing;
        _groundY = position.Y;
        _lastTick = tick;
        _budget = _tolerances.DistanceSlack;
    }

    /// <summary>Checks a report received at server tick <paramref name="tick"/>, accepting it if it passes.</summary>
    public MovementVerdict Check(in PositionReport report, uint tick, float maxSpeed, float jumpVelocity)
    {
        if (report.CorrectionSequence != CorrectionSequence)
        {
            return MovementVerdict.Stale;
        }

        var allowedSpeed = maxSpeed * _tolerances.SpeedFactor;
        var elapsed = (float)(tick - _lastTick) / _tickRate;
        _lastTick = tick;
        _budget = MathF.Min(_budget + allowedSpeed * elapsed, allowedSpeed * _tolerances.MaxBurstSeconds + _tolerances.DistanceSlack);

        var position = new Vector3(report.X, report.Y, report.Z);
        if (position.Y < _world.KillHeight)
        {
            return MovementVerdict.FellOut;
        }

        var distance = Vector2.Distance(new Vector2(Position.X, Position.Z), new Vector2(position.X, position.Z));
        var verdict =
            distance > _budget ? MovementVerdict.TooFast
            : position.Y > _groundY + _rules.JumpApex(jumpVelocity) * _tolerances.JumpFactor + _tolerances.HeightSlack ? MovementVerdict.TooHigh
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
        if (_world.TryGetGround(position, _rules.BodyRadius, position.Y + _tolerances.HeightSlack, out var ground) && position.Y - ground <= _tolerances.HeightSlack)
        {
            _groundY = ground;
            SafePosition = position;
        }

        return MovementVerdict.Accepted;
    }

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
