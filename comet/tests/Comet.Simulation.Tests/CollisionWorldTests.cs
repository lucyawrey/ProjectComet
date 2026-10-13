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

    [Fact]
    public void RaysStopWhereTheyFirstTouchABox()
    {
        // The box spans x 3 to 5 at height 2; a ray along +X from x 0 meets it at 3, or at 2.5 widened by 0.5.
        Assert.True(_world.Raycast(new Vector3(0, 2, 4), Vector3.UnitX, 10, 0, out var distance));
        Assert.Equal(3f, distance, 4);
        Assert.True(_world.Raycast(new Vector3(0, 2, 4), Vector3.UnitX * 7, 10, 0.5f, out distance));
        Assert.Equal(2.5f, distance, 4);

        Assert.False(_world.Raycast(new Vector3(0, 2, 4), Vector3.UnitX, 2, 0, out distance)); // too short
        Assert.Equal(2f, distance);
        Assert.False(_world.Raycast(new Vector3(0, 2, 4), -Vector3.UnitX, 10, 0, out _)); // pointing away
        Assert.False(_world.Raycast(new Vector3(0, 4, 4), Vector3.UnitX, 10, 0, out _)); // passing over
        Assert.True(_world.Raycast(new Vector3(4, 2, 4), Vector3.UnitX, 10, 0, out distance)); // starting inside
        Assert.Equal(0f, distance);
    }
}
