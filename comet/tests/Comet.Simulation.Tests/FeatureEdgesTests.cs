using System.Numerics;

namespace Comet.Simulation.Tests;

public class FeatureEdgesTests
{
    [Fact]
    public void ABoxHasItsTwelveEdgesWithoutDiagonals()
    {
        var box = CollisionMesh.Box(new Vector3(2, 1, 3), Matrix4x4.Identity);
        var lines = FeatureEdges.Of(box.Triangles);
        Assert.Equal(24, lines.Length);
        for (var i = 0; i < lines.Length; i += 2)
        {
            // Each edge runs along one axis: a face diagonal would change two coordinates.
            var changed = (lines[i].X != lines[i + 1].X ? 1 : 0) + (lines[i].Y != lines[i + 1].Y ? 1 : 0) + (lines[i].Z != lines[i + 1].Z ? 1 : 0);
            Assert.Equal(1, changed);
        }
    }

    [Fact]
    public void AnOpenQuadKeepsItsOutlineOnly()
    {
        var quad = new[]
        {
            new Vector3(0, 0, 0), new Vector3(0, 0, 1), new Vector3(1, 0, 1),
            new Vector3(0, 0, 0), new Vector3(1, 0, 1), new Vector3(1, 0, 0),
        };
        Assert.Equal(8, FeatureEdges.Of(quad).Length);
    }

    [Fact]
    public void NearlyMatchingCornersStillPairUp()
    {
        var quad = new[]
        {
            new Vector3(0, 0, 0), new Vector3(0, 0, 1), new Vector3(1, 0, 1),
            new Vector3(0.0001f, 0, 0), new Vector3(1, 0, 1.0001f), new Vector3(1, 0, 0),
        };
        Assert.Equal(8, FeatureEdges.Of(quad).Length);
    }
}
