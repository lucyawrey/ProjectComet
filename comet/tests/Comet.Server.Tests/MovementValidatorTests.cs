using System.Numerics;
using Comet.Content;
using Comet.Protocol.Messages;
using Comet.Server.Movement;
using Comet.Simulation;

namespace Comet.Server.Tests;

public class MovementValidatorTests
{
    private const int TickRate = 30;
    private const float MaxSpeed = 6;
    private const float JumpVelocity = 7; // apex ~0.98 m with gravity 25

    private readonly MovementValidator _validator;

    public MovementValidatorTests()
    {
        // Flat ground at 0 over 32 m, with a 2 m cube at (20, 20) and a hole cell around (28, 28).
        var map = new Heightmap { SizeX = 17, SizeZ = 17, Scale = 1, Offset = -1, Samples = Enumerable.Repeat((ushort)1, 289).ToArray() };
        map.Samples[14 * 17 + 14] = Heightmap.Hole;
        var world = new CollisionWorld(
            new TerrainGround(map, 2, Vector3.Zero),
            [Box.Standing(20, 20, 0, new Vector3(2, 2, 2))],
            killHeight: -20);
        _validator = new MovementValidator(world, new MovementRules(), new MovementTolerances(), TickRate);
        _validator.Reset(new Vector3(4, 0, 4), 0, tick: 100);
    }

    private MovementVerdict Report(float x, float y, float z, uint tick, uint sequence = 0) =>
        _validator.Check(new PositionReport { X = x, Y = y, Z = z, CorrectionSequence = sequence }, tick, MaxSpeed, JumpVelocity);

    [Fact]
    public void AcceptsWalkingAtFullSpeed()
    {
        // 15 reports a second (every 2 ticks) at 6 m/s: 0.4 m each.
        for (var i = 1; i <= 30; i++)
        {
            Assert.Equal(MovementVerdict.Accepted, Report(4 + 0.4f * i, 0, 4, (uint)(100 + 2 * i)));
        }

        Assert.Equal(new Vector3(16, 0, 4), _validator.Position);
        Assert.Equal(0, _validator.Violations);
    }

    [Fact]
    public void RejectsMovingTooFast()
    {
        // 50% over max speed: the budget runs out within a second.
        var verdicts = Enumerable.Range(1, 15).Select(i => Report(4 + 0.6f * i, 0, 4, (uint)(100 + 2 * i))).ToList();

        Assert.Contains(MovementVerdict.TooFast, verdicts);
        Assert.Equal(MovementVerdict.Accepted, verdicts[0]);
    }

    [Fact]
    public void AcceptsABurstAfterAStall()
    {
        // Nothing arrives for 0.5 s, then the delayed reports all arrive in the same tick.
        for (var i = 1; i <= 7; i++)
        {
            Assert.Equal(MovementVerdict.Accepted, Report(4 + 0.4f * i, 0, 4, 115));
        }
    }

    [Fact]
    public void DoesNotBankMovementForADash()
    {
        // Standing still for 10 s earns at most one second of movement.
        Assert.Equal(MovementVerdict.TooFast, Report(4 + 10, 0, 4, 400));
        Assert.Equal(MovementVerdict.Accepted, Report(4 + 7, 0, 4, 401));
    }

    [Fact]
    public void AllowsAJumpButNotMore()
    {
        Assert.Equal(MovementVerdict.Accepted, Report(4, 1.1f, 4, 110));
        Assert.Equal(MovementVerdict.TooHigh, Report(4, 2f, 4, 112));
    }

    [Fact]
    public void JumpsCountFromTheGroundStoodOn()
    {
        // Walk up to the cube and stand on top of it, then jump from there.
        _validator.Reset(new Vector3(20, 2, 20), 0, 100);

        Assert.Equal(MovementVerdict.Accepted, Report(20, 3.1f, 20, 110));
    }

    [Fact]
    public void RejectsBeingInsideABoxOrUnderground()
    {
        _validator.Reset(new Vector3(17.5f, 0, 20), 0, 100);

        Assert.Equal(MovementVerdict.InsideBox, Report(19, 0, 20, 130));
        Assert.Equal(MovementVerdict.UnderGround, Report(17.5f, -1, 20, 131));
    }

    [Fact]
    public void FallingOutRespawnsOnTheLastSafeGround()
    {
        Assert.Equal(MovementVerdict.Accepted, Report(5, 0, 4, 110));
        Assert.Equal(MovementVerdict.FellOut, Report(5, -25, 4, 140));

        var correction = _validator.Correct(CorrectionReason.Respawn, 140);

        Assert.Equal(CorrectionReason.Respawn, correction.Reason);
        Assert.Equal((5f, 0f, 4f), (correction.X, correction.Y, correction.Z));
        Assert.Equal(0, _validator.Violations);
    }

    [Fact]
    public void ReportsSentBeforeACorrectionAreIgnored()
    {
        Assert.Equal(MovementVerdict.TooFast, Report(14, 0, 4, 102));
        var correction = _validator.Correct(CorrectionReason.SnapBack, 102);
        Assert.Equal(1u, correction.Sequence);
        Assert.Equal((4f, 4f), (correction.X, correction.Z));

        // Already in flight when the correction went out.
        Assert.Equal(MovementVerdict.Stale, Report(14.4f, 0, 4, 104));

        // The client applied it and carries on from there.
        Assert.Equal(MovementVerdict.Accepted, Report(4.4f, 0, 4, 106, sequence: 1));
        Assert.Equal(1, _validator.Violations);
    }
}
