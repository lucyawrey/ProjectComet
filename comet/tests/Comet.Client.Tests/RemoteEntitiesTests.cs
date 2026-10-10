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
        // 10 m/s allows 2/30 × 10 + 0.5 ≈ 1.17 m in two ticks: 1 m blends, 20 m (a respawn) jumps at its tick.
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
        // Moving at 3 m/s (0.1 m a tick); the next state is late.
        _entities.Spawn(1, 10, Vector3.Zero, 0);
        _entities.AddState(1, 12, new Vector3(0.2f, 0, 0), 0, new Vector3(3, 0, 0));

        Assert.Equal(0.5f, PositionAt(15).X, 4);
        // Capped at 0.3 s (9 ticks) past the state, then it holds, and the overrun counts once.
        Assert.Equal(1.1f, PositionAt(25).X, 4);
        Assert.Equal(1.1f, PositionAt(30).X, 4);
        Assert.Equal(1, _entities.Overruns);
    }

    [Fact]
    public void ALateStateIsEasedInRatherThanJumpedTo()
    {
        _entities.Spawn(1, 10, Vector3.Zero, 0);
        _entities.AddState(1, 12, new Vector3(0.2f, 0, 0), 0, new Vector3(3, 0, 0));
        var reckoned = PositionAt(16).X; // 0.6, dead-reckoned
        // The late states show the entity turned off its straight line: it's eased over, not jumped.
        _entities.AddState(1, 14, new Vector3(0.3f, 0, 0.3f), 0, new Vector3(1, 0, 3));
        _entities.AddState(1, 18, new Vector3(0.5f, 0, 0.9f), 0, new Vector3(1, 0, 3));
        _entities.AddState(1, 24, new Vector3(0.7f, 0, 1.5f), 0, new Vector3(1, 0, 3));
        var first = _entities.TrySample(1, 16.1, out var pose) ? pose.Position : default;
        Assert.InRange(Vector3.Distance(first, new Vector3(reckoned, 0, 0)), 0, 0.05f);
        // After the blend (0.15 s, 4.5 ticks) it's on the states' track, halfway from 18 to 24.
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
        // Walking uphill along x on a 30 degree slope (rising 0.577 m a metre) at 3 m/s, reported level.
        var entities = new RemoteEntities(tickRate: 30, gravity: 25) { GroundHeight = feet => MathF.Min(feet.Y, 0.577f * feet.X) };
        entities.Spawn(1, 10, Vector3.Zero, 0);
        entities.AddState(1, 12, new Vector3(0.2f, 0.1155f, 0), 0, new Vector3(3, 0, 0));

        Assert.True(entities.TrySample(1, 18, out var pose)); // 0.2 s on: 0.8 m along
        Assert.Equal(0.577f * 0.8f, pose.Position.Y, 3);
    }

    [Fact]
    public void DeadReckoningOnTheGroundFallsOffALedge()
    {
        // Walking off a 3 m drop at x = 0.3.
        var entities = new RemoteEntities(tickRate: 30, gravity: 25) { GroundHeight = feet => feet.X < 0.3f ? MathF.Min(feet.Y, 3) : 0 };
        entities.Spawn(1, 10, new Vector3(0, 3, 0), 0);
        entities.AddState(1, 12, new Vector3(0.2f, 3, 0), 0, new Vector3(3, 0, 0));

        Assert.True(entities.TrySample(1, 18, out var pose)); // 0.2 s on: half of g t squared below the ledge
        Assert.Equal(3 - 0.5f * 25 * 0.04f, pose.Position.Y, 3);
    }

    [Fact]
    public void AStallWhileMovingIsntMistakenForStandingStill()
    {
        // 0.5 s without states (longer than the idle gap) after a moving state: no restamped copy of the old
        // position before the late one, which would snap the entity back.
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

        // A burst of late states; the render tick is already past all but the last.
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
        // Arrived while the render tick was still behind the state before it.
        _entities.AddState(1, 14, new Vector3(4, 0, 0), 0);
        PositionAt(15);
        // The render tick had passed 14 by one tick: a hold.
        _entities.AddState(1, 16, new Vector3(6, 0, 0), 0);
        // After an idle gap the entity stood still, so waiting for the next state isn't a hold.
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

        // Without the restamp this would be about 0.02 of the way, gliding for three seconds.
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
        // Past the idle gap the entity stood still, so the gap says nothing about the delay needed.
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
    [InlineData(0.1f, 6.2f, 0.5f, 6.2831855f)] // across zero the short way: halfway between is ~0 (2π)
    [InlineData(0f, 1f, 0.5f, 0.5f)]
    [InlineData(3f, -3f, 0.5f, 3.1415927f)]
    public void FacingTurnsTheShortWay(float from, float to, float t, float expected)
    {
        var angle = RemoteEntities.LerpAngle(from, to, t);
        var difference = MathF.IEEERemainder(angle - expected, 2 * MathF.PI);
        Assert.InRange(difference, -0.01f, 0.01f);
    }
}
