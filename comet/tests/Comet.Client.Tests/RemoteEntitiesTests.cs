using System.Numerics;

namespace Comet.Client.Tests;

public class RemoteEntitiesTests
{
    private readonly RemoteEntities _entities = new(tickRate: 30);

    private Vector3 PositionAt(double renderTick)
    {
        Assert.True(_entities.TrySample(1, renderTick, out var pose));
        return pose.Position;
    }

    [Fact]
    public void BlendsBetweenStates()
    {
        _entities.Spawn(1, 10, Vector3.Zero, 0);
        _entities.AddState(1, 12, new Vector3(2, 0, 0), 0);

        Assert.Equal(new Vector3(1, 0, 0), PositionAt(11));
        Assert.Equal(new Vector3(2, 0, 0), PositionAt(12));
    }

    [Fact]
    public void JumpsInsteadOfGlidingWhenAMoveIsTooFastToWalk()
    {
        var entities = new RemoteEntities(tickRate: 30, teleportSpeed: 10);
        entities.Spawn(1, 10, Vector3.Zero, 0);
        entities.AddState(1, 12, new Vector3(1, 0, 0), 0);
        entities.AddState(1, 14, new Vector3(20, 0, 0), 0);

        Assert.True(entities.TrySample(1, 11, out var walking));
        Assert.Equal(new Vector3(0.5f, 0, 0), walking.Position);
        Assert.True(entities.TrySample(1, 13.9, out var before));
        Assert.Equal(new Vector3(1, 0, 0), before.Position);
        Assert.True(entities.TrySample(1, 14, out var after));
        Assert.Equal(new Vector3(20, 0, 0), after.Position);
    }

    [Fact]
    public void FallingFastIsBlendedButRisingFastJumps()
    {
        var entities = new RemoteEntities(tickRate: 30, teleportSpeed: 10);
        entities.Spawn(1, 10, Vector3.Zero, 0);
        entities.AddState(1, 12, new Vector3(0, -5, 0), 0);
        entities.AddState(1, 14, new Vector3(0, 30, 0), 0);

        Assert.True(entities.TrySample(1, 11, out var falling));
        Assert.Equal(new Vector3(0, -2.5f, 0), falling.Position);
        Assert.True(entities.TrySample(1, 13, out var rising));
        Assert.Equal(new Vector3(0, -5, 0), rising.Position);
    }

    [Fact]
    public void AnEntityStandingStillHoldsAtItsNewestState()
    {
        _entities.Spawn(1, 10, Vector3.Zero, 0);
        _entities.AddState(1, 12, new Vector3(2, 0, 0), 0);

        Assert.Equal(new Vector3(2, 0, 0), PositionAt(20));
    }

    [Fact]
    public void ARunDryEntityIsDeadReckonedAlongItsVelocityThenHolds()
    {
        _entities.Spawn(1, 10, Vector3.Zero, 0);
        _entities.AddState(1, 12, new Vector3(0.2f, 0, 0), 0, new Vector3(3, 0, 0));

        Assert.Equal(0.5f, PositionAt(15).X, 4);
        Assert.Equal(1.1f, PositionAt(25).X, 4);
        Assert.Equal(1.1f, PositionAt(30).X, 4);
        Assert.Equal(1, _entities.Overruns);
    }

    [Fact]
    public void ALateStateIsEasedInRatherThanJumpedTo()
    {
        _entities.Spawn(1, 10, Vector3.Zero, 0);
        _entities.AddState(1, 12, new Vector3(0.2f, 0, 0), 0, new Vector3(3, 0, 0));
        var reckoned = PositionAt(16).X;
        _entities.AddState(1, 14, new Vector3(0.3f, 0, 0.3f), 0, new Vector3(1, 0, 3));
        _entities.AddState(1, 18, new Vector3(0.5f, 0, 0.9f), 0, new Vector3(1, 0, 3));
        _entities.AddState(1, 24, new Vector3(0.7f, 0, 1.5f), 0, new Vector3(1, 0, 3));
        var first = _entities.TrySample(1, 16.1, out var pose) ? pose.Position : default;
        Assert.InRange(Vector3.Distance(first, new Vector3(reckoned, 0, 0)), 0, 0.05f);
        Assert.Equal(0, Vector3.Distance(new Vector3(0.6f, 0, 1.2f), PositionAt(21)), 4);
        Assert.Equal(1, _entities.BlendBacks);
        Assert.Equal(1, _entities.VisibleBlendBacks);
    }

    [Fact]
    public void DeadReckoningFallsInTheAirButNotThroughTheGround()
    {
        var entities = new RemoteEntities(tickRate: 30, gravity: 40) { GroundHeight = _ => 0 };
        entities.Spawn(1, 10, new Vector3(0, 1, 0), 0);
        entities.AddState(1, 12, new Vector3(0, 1, 0), 0, new Vector3(0, 2, 0));

        Assert.True(entities.TrySample(1, 15, out var rising));
        Assert.Equal(1 + 2 * 0.1f - 0.5f * 40 * 0.01f, rising.Position.Y, 4);
        Assert.True(entities.TrySample(1, 21, out var landed));
        Assert.Equal(0, landed.Position.Y, 4);
    }

    [Fact]
    public void DeadReckoningOnTheGroundFollowsASlope()
    {
        var entities = new RemoteEntities(tickRate: 30, gravity: 25) { GroundHeight = feet => MathF.Min(feet.Y, 0.577f * feet.X) };
        entities.Spawn(1, 10, Vector3.Zero, 0);
        entities.AddState(1, 12, new Vector3(0.2f, 0.1155f, 0), 0, new Vector3(3, 0, 0));

        Assert.True(entities.TrySample(1, 18, out var pose));
        Assert.Equal(0.577f * 0.8f, pose.Position.Y, 3);
    }

    [Fact]
    public void DeadReckoningOnTheGroundFallsOffALedge()
    {
        var entities = new RemoteEntities(tickRate: 30, gravity: 25) { GroundHeight = feet => feet.X < 0.3f ? MathF.Min(feet.Y, 3) : 0 };
        entities.Spawn(1, 10, new Vector3(0, 3, 0), 0);
        entities.AddState(1, 12, new Vector3(0.2f, 3, 0), 0, new Vector3(3, 0, 0));

        Assert.True(entities.TrySample(1, 18, out var pose));
        Assert.Equal(3 - 0.5f * 25 * 0.04f, pose.Position.Y, 3);
    }

    [Fact]
    public void DeadReckoningOnTheGroundStopsAtABlockTooHighToStepOnto()
    {
        var entities = new RemoteEntities(tickRate: 30, gravity: 25)
        {
            GroundHeight = feet =>
                feet.X >= 1
                    ? MathF.Min(feet.Y, 1) is var top && top >= 1
                        ? 1
                        : 0
                    : 0,
        };
        entities.Spawn(1, 10, Vector3.Zero, 0);
        entities.AddState(1, 12, new Vector3(0.2f, 0, 0), 0, new Vector3(6, 0, 0));

        for (var tick = 13; tick <= 30; tick++)
        {
            Assert.True(entities.TrySample(1, tick, out var pose));
            Assert.Equal(0, pose.Position.Y);
            Assert.True(pose.Position.X < 1, $"inside the block at tick {tick}: {pose.Position}");
        }
    }

    [Fact]
    public void DeadReckoningOffABlockFallsSmoothlyWithoutSnappingDown()
    {
        var entities = new RemoteEntities(tickRate: 30, gravity: 25)
        {
            GroundHeight = feet =>
                feet.X < 0.3f
                    ? MathF.Min(feet.Y, 1) is var top && top >= 1
                        ? 1
                        : 0
                    : 0,
        };
        entities.Spawn(1, 10, new Vector3(0, 1, 0), 0);
        entities.AddState(1, 12, new Vector3(0.2f, 1, 0), 0, new Vector3(6, 0, 0));

        var previous = 1f;
        for (var tick = 13; tick <= 24; tick++)
        {
            Assert.True(entities.TrySample(1, tick, out var pose));
            var seconds = (tick - 12) / 30f;
            var expected = MathF.Max(0, 1 - 0.5f * 25 * seconds * seconds);
            Assert.True(MathF.Abs(pose.Position.Y - expected) < 0.15f, $"at tick {tick}: y = {pose.Position.Y}, the fall says {expected}");
            Assert.True(pose.Position.Y <= previous);
            previous = pose.Position.Y;
        }
    }

    [Fact]
    public void AStallWhileMovingIsntMistakenForStandingStill()
    {
        _entities.Spawn(1, 10, Vector3.Zero, 0);
        _entities.AddState(1, 12, new Vector3(0.2f, 0, 0), 0, new Vector3(3, 0, 0));
        Assert.Equal(12, _entities.AddState(1, 27, new Vector3(1.7f, 0, 0), 0, new Vector3(3, 0, 0)));
        Assert.Equal(0.95f, PositionAt(19.5).X, 4);
    }

    [Fact]
    public void HoldsAtTheSpawnUntilTheRenderTickReachesIt()
    {
        _entities.Spawn(1, 10, new Vector3(5, 0, 0), 0);
        _entities.AddState(1, 12, new Vector3(7, 0, 0), 0);

        Assert.Equal(new Vector3(5, 0, 0), PositionAt(8));
    }

    [Fact]
    public void AnExcludedEntityIsntCounted()
    {
        var entities = new RemoteEntities(tickRate: 30, teleportSpeed: 10);
        entities.Spawn(1, 10, Vector3.Zero, 0);
        entities.Spawn(2, 10, Vector3.Zero, 0);
        entities.ExcludeFromCounts(2);

        entities.AddState(1, 12, new Vector3(5, 0, 0), 0, new Vector3(3, 0, 0));
        entities.AddState(2, 12, new Vector3(5, 0, 0), 0, new Vector3(3, 0, 0));

        Assert.Equal(1, entities.MoveStates);
        Assert.Equal(1, entities.Jumps);
    }

    [Fact]
    public void JumpsToTheLatestStateAfterAStall()
    {
        _entities.Spawn(1, 10, Vector3.Zero, 0);
        Assert.Equal(Vector3.Zero, PositionAt(10));

        for (uint tick = 12; tick <= 20; tick += 2)
        {
            _entities.AddState(1, tick, new Vector3(tick, 0, 0), 0);
        }

        Assert.Equal(new Vector3(20, 0, 0), PositionAt(20));
    }

    [Fact]
    public void CountsMovesThatRanOutOfStates()
    {
        _entities.Spawn(1, 10, Vector3.Zero, 0);
        _entities.AddState(1, 12, new Vector3(2, 0, 0), 0);
        PositionAt(11);
        _entities.AddState(1, 14, new Vector3(4, 0, 0), 0);
        PositionAt(15);
        _entities.AddState(1, 16, new Vector3(6, 0, 0), 0);
        PositionAt(30);
        _entities.AddState(1, 100, new Vector3(8, 0, 0), 0);

        Assert.Equal(3, _entities.MoveStates);
        Assert.Equal(1, _entities.Holds);
        Assert.Equal(1, _entities.HeldTicks, 6);
    }

    [Fact]
    public void RestampsAStateLeftStandingBeforeAnIdleGap()
    {
        _entities.Spawn(1, 10, Vector3.Zero, 0);
        _entities.AddState(1, 100, new Vector3(2, 0, 0), 0);

        Assert.Equal(Vector3.Zero, PositionAt(98));
        Assert.Equal(new Vector3(1, 0, 0), PositionAt(99));
    }

    [Fact]
    public void ReportsThePreviousStampOnlyWhileMoving()
    {
        Assert.Null(_entities.AddState(1, 10, Vector3.One, 0));

        _entities.Spawn(1, 10, Vector3.Zero, 0);
        Assert.Equal(10, _entities.AddState(1, 12, new Vector3(2, 0, 0), 0));
        Assert.Null(_entities.AddState(1, 12, new Vector3(3, 0, 0), 0));
        Assert.Null(_entities.AddState(1, 100, new Vector3(4, 0, 0), 0));
    }

    [Fact]
    public void IgnoresUnknownEntitiesAndOldStates()
    {
        _entities.AddState(2, 10, Vector3.One, 0);
        Assert.False(_entities.Contains(2));

        _entities.Spawn(1, 10, Vector3.Zero, 0);
        _entities.AddState(1, 12, new Vector3(2, 0, 0), 0);
        _entities.AddState(1, 11, new Vector3(50, 0, 0), 0);
        Assert.Equal(new Vector3(1, 0, 0), PositionAt(11));
    }

    [Fact]
    public void DespawnStopsTracking()
    {
        _entities.Spawn(1, 10, Vector3.Zero, 0);
        Assert.True(_entities.Despawn(1));
        Assert.False(_entities.TrySample(1, 10, out _));
    }

    [Theory]
    [InlineData(0.1f, 6.2f, 0.5f, 6.2831855f)]
    [InlineData(0f, 1f, 0.5f, 0.5f)]
    [InlineData(3f, -3f, 0.5f, 3.1415927f)]
    public void FacingTurnsTheShortWay(float from, float to, float t, float expected)
    {
        var angle = RemoteEntities.LerpAngle(from, to, t);
        var difference = MathF.IEEERemainder(angle - expected, 2 * MathF.PI);
        Assert.InRange(difference, -0.01f, 0.01f);
    }
}
