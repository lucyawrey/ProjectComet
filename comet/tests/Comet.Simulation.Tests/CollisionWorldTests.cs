using System.Numerics;

namespace Comet.Simulation.Tests;

public class CollisionWorldTests
{
    private readonly CollisionWorld _world = TestWorlds.Create();

    [Fact]
    public void MeshTopsAreGroundWhenTheFootprintOverlaps()
    {
        Assert.True(_world.TryGetGround(new Vector3(4, 3, 4), 0.4f, 3, out var top));
        Assert.Equal(3f, top);

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
        var ramp = new CollisionMesh([
            new Vector3(20, 0, 19),
            new Vector3(20, 0, 21),
            new Vector3(22, 2, 19),
            new Vector3(22, 2, 19),
            new Vector3(20, 0, 21),
            new Vector3(22, 2, 21),
        ]);
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
        Assert.False(_world.IsBlocked(new Vector3(4, 3, 4), 0.4f, 1));
    }

    [Fact]
    public void TerrainWallsBlockButStandingOnTopDoesNot()
    {
        var world = new CollisionWorld(new ZoneTerrain(TestWorlds.Zone(("plateau", 4, -5, 6, 6, 4))), [], -20);

        Assert.True(world.IsBlocked(new Vector3(5.7f, 1, 7), 0.4f, 1));
        Assert.False(world.IsBlocked(new Vector3(5.5f, 1, 7), 0.4f, 1));
        Assert.False(world.IsBlocked(new Vector3(7, 4, 7), 0.4f, 1));
    }

    [Fact]
    public void PiecesAboveBlockOnlyAtHeadHeight()
    {
        var world = new CollisionWorld(new ZoneTerrain(TestWorlds.Zone(("island", 4, 3, 6, 6, 4))), [], -20);

        Assert.False(world.IsBlocked(new Vector3(7, 1, 7), 0.4f, 1));
        Assert.True(world.IsBlocked(new Vector3(7, 2.5f, 7), 0.4f, 1));
    }

    [Fact]
    public void InsideTerrainIsBetweenAPiecesGroundAndBottom()
    {
        var world = new CollisionWorld(new ZoneTerrain(TestWorlds.Zone(("island", 4, 3, 6, 6, 4))), [], -20);

        Assert.True(world.IsInsideTerrain(new Vector3(1, 0.5f, 1), slack: 0.3f));
        Assert.False(world.IsInsideTerrain(new Vector3(1, 0.8f, 1), slack: 0.3f));
        Assert.False(world.IsInsideTerrain(new Vector3(7, 2, 7), slack: 0.3f));
        Assert.True(world.IsInsideTerrain(new Vector3(7, 3.5f, 7), slack: 0.3f));
    }

    [Fact]
    public void QueriesFindTrianglesSpanningManyCells()
    {
        var world = TestWorlds.Create(TestWorlds.Block(40, 40, 0, new Vector3(40, 1, 2)));

        Assert.True(world.TryGetGround(new Vector3(25, 1, 40), 0.4f, 1, out var top));
        Assert.Equal(1f, top);
        Assert.True(world.IsBlocked(new Vector3(55, 0.5f, 40), 0.4f, 1));
    }

    [Fact]
    public void LinesAreBlockedByMeshesAndTerrainInTheirWay()
    {
        Assert.False(_world.IsLineBlocked(new Vector3(1, 2, 1), new Vector3(1, 2, 12)));
        Assert.True(_world.IsLineBlocked(new Vector3(4, 2, 0), new Vector3(4, 2, 8)));
        Assert.True(_world.IsLineBlocked(new Vector3(0.5f, 2, 0.5f), new Vector3(10, 2, 10)));
        Assert.True(_world.IsLineBlocked(new Vector3(8, 2, 8), new Vector3(8, 0, 8)));
        Assert.False(_world.IsLineBlocked(new Vector3(4, 2, 0), new Vector3(4, 2, 2.9f)));
    }

    [Fact]
    public void LinesFromInsideAMeshSeeOut()
    {
        Assert.False(_world.IsLineBlocked(new Vector3(4, 2, 4), new Vector3(4, 2, 8)));
    }

    [Fact]
    public void LinesPassMeshesThatAreOffOrPassedThrough()
    {
        var wall = TestWorlds.Block(8, 8, 1, new Vector3(4, 3, 0.5f));
        wall.Tag = "wall";
        var world = TestWorlds.Create(wall);
        Vector3 from = new(8, 2, 5),
            to = new(8, 2, 11);
        Assert.True(world.IsLineBlocked(from, to));
        Assert.False(world.IsLineBlocked(from, to, new HashSet<int>(world.MeshesTagged("wall"))));

        world.SetFlipped("wall", true);
        Assert.False(world.IsLineBlocked(from, to));
    }
}
