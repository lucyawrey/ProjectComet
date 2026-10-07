using System.Numerics;

namespace Comet.Simulation.Tests;

public class TerrainGroundTests
{
    private readonly TerrainGround _terrain = TestWorlds.Create().Terrain!;

    [Fact]
    public void FlatGroundHasItsSampleHeight()
    {
        Assert.True(_terrain.TryGetHeight(3.3f, 1.7f, out var height));
        Assert.Equal(1f, height, 4);
    }

    [Fact]
    public void SlopesInterpolateAlongTheMeshTriangles()
    {
        // Between x = 14 (1 m) and x = 16 (2 m): halfway up at x = 15.
        Assert.True(_terrain.TryGetHeight(15, 1, out var height));
        Assert.Equal(1.5f, height, 4);
    }

    [Fact]
    public void TrianglesTouchingAHoleAreNotGround()
    {
        // Sample (6, 6) sits at (12, 12); the triangles around it are gone, those further away remain.
        Assert.False(_terrain.TryGetHeight(12, 12, out _));
        Assert.False(_terrain.TryGetHeight(11.5f, 11.5f, out _));
        Assert.True(_terrain.TryGetHeight(10.2f, 10.2f, out _));
    }

    [Fact]
    public void OutsideTheMapIsNotGround()
    {
        Assert.False(_terrain.TryGetHeight(-0.1f, 4, out _));
        Assert.False(_terrain.TryGetHeight(4, 16.1f, out _));
        Assert.True(_terrain.TryGetHeight(16, 16, out _));
    }

    [Fact]
    public void CentredPlacesTheMiddleOfTheMapAtTheCentre()
    {
        var centred = TerrainGround.Centred(_terrain.Map, 2, new Vector3(100, 5, 100));

        Assert.Equal(new Vector3(92, 5, 92), centred.Origin);
        Assert.True(centred.TryGetHeight(100, 100, out var height));
        Assert.Equal(6f, height, 4);
    }
}
