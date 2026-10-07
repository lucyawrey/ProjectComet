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
    public void HoldsAtTheNewestStateWithoutExtrapolating()
    {
        _entities.Spawn(1, 10, Vector3.Zero, 0);
        _entities.AddState(1, 12, new Vector3(2, 0, 0), 0);

        Assert.Equal(new Vector3(2, 0, 0), PositionAt(20));
    }

    [Fact]
    public void HoldsAtTheSpawnUntilTheRenderTickReachesIt()
    {
        _entities.Spawn(1, 10, new Vector3(5, 0, 0), 0);
        _entities.AddState(1, 12, new Vector3(7, 0, 0), 0);

        Assert.Equal(new Vector3(5, 0, 0), PositionAt(8));
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
    public void RestampsAStateLeftStandingBeforeAnIdleGap()
    {
        _entities.Spawn(1, 10, Vector3.Zero, 0);
        _entities.AddState(1, 100, new Vector3(2, 0, 0), 0);

        // Without the restamp this would be about 0.02 of the way, gliding for three seconds.
        Assert.Equal(Vector3.Zero, PositionAt(98));
        Assert.Equal(new Vector3(1, 0, 0), PositionAt(99));
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
