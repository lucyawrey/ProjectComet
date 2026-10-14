using System.Numerics;
using Comet.Content;

namespace Comet.Simulation.Tests;

public class CollisionWorldTests
{
    private readonly CollisionWorld _world = TestWorlds.Create();

    [Fact]
    public void MeshTopsAreGroundWhenTheFootprintOverlaps()
    {
        Assert.True(_world.TryGetGround(new Vector3(4, 3, 4), 0.4f, 3, out var top));
        Assert.Equal(3f, top);

        // Center just past the edge, footprint still on the box.
        Assert.True(_world.TryGetGround(new Vector3(5.3f, 3, 4), 0.4f, 3, out top));
        Assert.Equal(3f, top);
    }

    [Fact]
    public void MeshTopsAboveMaxYAreIgnored()
    {
        Assert.True(_world.TryGetGround(new Vector3(4, 1, 4), 0.4f, 1.5f, out var ground));
        Assert.Equal(1f, ground, 4);
    }

    [Fact]
    public void SlopesHoldTheBodyAtTheirHighestPointUnderIt()
    {
        // A 45° ramp rising 2 m toward +X over x 0..2, its foot at (20, 0, 20): not walkable faces are ignored.
        var ramp = new CollisionMesh([new Vector3(20, 0, 19), new Vector3(20, 0, 21), new Vector3(22, 2, 19), new Vector3(22, 2, 19), new Vector3(20, 0, 21), new Vector3(22, 2, 21)]);
        var world = new CollisionWorld(null, [ramp], -20);

        Assert.True(world.TryGetGround(new Vector3(21, 5, 20), 0.4f, 5, out var ground));
        Assert.Equal(1.4f, ground, 2);
    }

    [Fact]
    public void TerrainCountsUpToTheClimbAllowance()
    {
        Assert.False(_world.TryGetGround(new Vector3(1, 0, 1), 0.4f, 0.2f, out _));
        Assert.True(_world.TryGetGround(new Vector3(1, 0, 1), 0.4f, 0.2f, out var ground, terrainClimb: 0.8f));
        Assert.Equal(1f, ground, 4);
    }

    [Fact]
    public void StackedPiecesGiveTheHighestGroundBelowTheFeet()
    {
        // A 4 m-high island piece over x, z 6..9.
        var world = new CollisionWorld(new ZoneTerrain(TestWorlds.Zone(("island", 4, 3, 6, 6, 4))), [], -20);

        Assert.True(world.TryGetGround(new Vector3(7, 5, 7), 0.4f, 5, out var ground));
        Assert.Equal(4f, ground, 3);
        Assert.True(world.TryGetGround(new Vector3(7, 1, 7), 0.4f, 1, out ground));
        Assert.Equal(1f, ground, 3);
    }

    [Fact]
    public void BodiesInsideMeshesAreBlocked()
    {
        Assert.True(_world.IsBlocked(new Vector3(4, 1, 4), 0.4f, 1));
        Assert.True(_world.IsBlocked(new Vector3(5.3f, 1, 4), 0.4f, 1));
        Assert.False(_world.IsBlocked(new Vector3(5.5f, 1, 4), 0.4f, 1));
        Assert.False(_world.IsBlocked(new Vector3(4, 3, 4), 0.4f, 1)); // standing on top
    }

    [Fact]
    public void TerrainWallsBlockButSlopesDoNot()
    {
        // A 3 m-high plateau piece over x, z 6..9, standing on the 1 m ground.
        var world = new CollisionWorld(new ZoneTerrain(TestWorlds.Zone(("plateau", 4, -5, 6, 6, 4))), [], -20);

        Assert.True(world.IsBlockedByTerrain(new Vector3(5.7f, 1, 7), 0.4f, 1, climb: 0.5f)); // footprint over its edge
        Assert.False(world.IsBlockedByTerrain(new Vector3(5.5f, 1, 7), 0.4f, 1, climb: 0.5f));
        Assert.False(world.IsBlockedByTerrain(new Vector3(7, 4, 7), 0.4f, 1, climb: 0)); // on top
        Assert.False(world.IsBlockedByTerrain(new Vector3(15, 1.5f, 3), 0.4f, 1, climb: 0.2f)); // on the rise
    }

    [Fact]
    public void PiecesAboveBlockOnlyAtHeadHeight()
    {
        // An island 4 m up with its bottom at 3 m: walking under it is fine, jumping into it isn't.
        var world = new CollisionWorld(new ZoneTerrain(TestWorlds.Zone(("island", 4, 3, 6, 6, 4))), [], -20);

        Assert.False(world.IsBlockedByTerrain(new Vector3(7, 1, 7), 0.4f, 1, climb: 0.5f));
        Assert.True(world.IsBlockedByTerrain(new Vector3(7, 2.5f, 7), 0.4f, 1, climb: 0));
    }

    [Fact]
    public void InsideTerrainIsBetweenAPiecesGroundAndBottom()
    {
        var world = new CollisionWorld(new ZoneTerrain(TestWorlds.Zone(("island", 4, 3, 6, 6, 4))), [], -20);

        Assert.True(world.IsInsideTerrain(new Vector3(1, 0.5f, 1), slack: 0.3f));
        Assert.False(world.IsInsideTerrain(new Vector3(1, 0.8f, 1), slack: 0.3f)); // within the slack
        Assert.False(world.IsInsideTerrain(new Vector3(7, 2, 7), slack: 0.3f)); // under the island, above the ground
        Assert.True(world.IsInsideTerrain(new Vector3(7, 3.5f, 7), slack: 0.3f)); // inside the island
    }

    [Fact]
    public void RaysStopWhereTheyFirstTouchAMesh()
    {
        // The box spans x 3 to 5 at height 2; a ray along +X from x 0 meets it at 3, or 2.5 for a 0.5 m sphere.
        Assert.True(_world.Raycast(new Vector3(0, 2, 4), Vector3.UnitX, 10, 0, out var distance));
        Assert.Equal(3f, distance, 4);
        Assert.True(_world.Raycast(new Vector3(0, 2, 4), Vector3.UnitX * 7, 10, 0.5f, out distance));
        Assert.Equal(2.5f, distance, 4);

        Assert.False(_world.Raycast(new Vector3(0, 2, 4), Vector3.UnitX, 2, 0, out distance)); // too short
        Assert.Equal(2f, distance);
        Assert.False(_world.Raycast(new Vector3(0, 2, 4), -Vector3.UnitX, 10, 0, out _)); // pointing away
        Assert.False(_world.Raycast(new Vector3(0, 4, 4), Vector3.UnitX, 10, 0, out _)); // passing over
        Assert.True(_world.Raycast(new Vector3(4, 2, 4), Vector3.UnitX, 10, 0, out distance)); // starting inside: the far face
        Assert.Equal(1f, distance, 4);
    }

    [Fact]
    public void QueriesFindTrianglesSpanningManyCells()
    {
        // A 40 m slab, listed in many cells, is found once and gives the right answers anywhere along it.
        var world = TestWorlds.Create(TestWorlds.Block(40, 40, 0, new Vector3(40, 1, 2)));

        Assert.True(world.TryGetGround(new Vector3(25, 1, 40), 0.4f, 1, out var top));
        Assert.Equal(1f, top);
        Assert.True(world.IsBlocked(new Vector3(55, 0.5f, 40), 0.4f, 1));
    }
}
