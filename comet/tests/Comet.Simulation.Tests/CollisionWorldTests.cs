using System.Numerics;

namespace Comet.Simulation.Tests;

public class CollisionWorldTests
{
    private readonly CollisionWorld _world = TestWorlds.Create();

    [Fact]
    public void BoxTopsAreGroundWhenTheFootprintOverlaps()
    {
        Assert.True(_world.TryGetGround(new Vector3(4, 3, 4), 0.4f, 3, out var top));
        Assert.Equal(3f, top);

        // Centre just past the edge, footprint still on the box.
        Assert.True(_world.TryGetGround(new Vector3(5.3f, 3, 4), 0.4f, 3, out top));
        Assert.Equal(3f, top);
    }

    [Fact]
    public void BoxTopsAboveMaxYAreIgnored()
    {
        Assert.True(_world.TryGetGround(new Vector3(4, 1, 4), 0.4f, 1.5f, out var ground));
        Assert.Equal(1f, ground, 4);
    }

    [Fact]
    public void TerrainCountsUpToTheClimbAllowance()
    {
        Assert.False(_world.TryGetGround(new Vector3(1, 0, 1), 0.4f, 0.2f, out _));
        Assert.True(_world.TryGetGround(new Vector3(1, 0, 1), 0.4f, 0.2f, out var ground, terrainClimb: 0.8f));
        Assert.Equal(1f, ground, 4);
    }

    [Fact]
    public void BodiesInsideBoxesAreBlocked()
    {
        Assert.True(_world.IsBlocked(new Vector3(4, 1, 4), 0.4f, 1));
        Assert.True(_world.IsBlocked(new Vector3(5.3f, 1, 4), 0.4f, 1));
        Assert.False(_world.IsBlocked(new Vector3(5.5f, 1, 4), 0.4f, 1));
        Assert.False(_world.IsBlocked(new Vector3(4, 3, 4), 0.4f, 1)); // standing on top
    }
}
