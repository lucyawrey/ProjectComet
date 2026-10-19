using System.Numerics;
using Comet.Content;

namespace Comet.Simulation.Tests;

public class PrimitiveMesherTests
{
    private static readonly PolygonBudget Budget = new() { FacetLength = 0.5f, MinSides = 6, MaxSides = 48 };

    public static TheoryData<PrimitiveKind, float> EveryKind()
    {
        var data = new TheoryData<PrimitiveKind, float>();
        foreach (var kind in Enum.GetValues<PrimitiveKind>())
        {
            data.Add(kind, 0f);
        }

        data.Add(PrimitiveKind.Box, 0.2f); // with a bevel
        return data;
    }

    private static Primitive Make(PrimitiveKind kind, float bevel = 0) => new()
    {
        Kind = kind, Bevel = bevel, OpeningWidth = kind is PrimitiveKind.Wall or PrimitiveKind.Arch ? 1.2f : 0, OpeningHeight = 2.2f,
    };

    // Every edge is shared by exactly one other triangle running the other way: closed, and wound consistently.
    [Theory]
    [MemberData(nameof(EveryKind))]
    public void MeshesAreClosedAndFaceOutward(PrimitiveKind kind, float bevel)
    {
        var size = new Vector3(3, 3.2f, 2);
        var triangles = PrimitiveMesher.Build(Make(kind, bevel), size, Budget);
        Assert.NotEmpty(triangles);

        var edges = new Dictionary<(string, string), int>();
        for (var i = 0; i < triangles.Length; i += 3)
        {
            for (var k = 0; k < 3; k++)
            {
                var key = (Key(triangles[i + k]), Key(triangles[i + (k + 1) % 3]));
                edges[key] = edges.GetValueOrDefault(key) + 1;
            }
        }

        foreach (var ((from, to), count) in edges)
        {
            Assert.Equal(count, edges.GetValueOrDefault((to, from)));
        }

        Assert.True(Volume(triangles) > 0, "faces point inward");
    }

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void MeshesStayInTheirBounds(PrimitiveKind kind, float bevel)
    {
        var size = new Vector3(3, 3.2f, 2);
        var (min, max) = PrimitiveMesher.Bounds(Make(kind, bevel), size);
        foreach (var corner in PrimitiveMesher.Build(Make(kind, bevel), size, Budget))
        {
            Assert.True(Vector3.Min(corner, min - new Vector3(1e-4f)) == min - new Vector3(1e-4f) && Vector3.Max(corner, max + new Vector3(1e-4f)) == max + new Vector3(1e-4f), $"{corner} is outside");
        }
    }

    [Fact]
    public void BoxesAndWedgesHaveTheirVolume()
    {
        var size = new Vector3(2, 3, 4);
        Assert.Equal(24, Volume(PrimitiveMesher.Build(Make(PrimitiveKind.Box), size, Budget)), 3);
        Assert.Equal(12, Volume(PrimitiveMesher.Build(Make(PrimitiveKind.Wedge), size, Budget)), 3);
        Assert.Equal(24, Volume(PrimitiveMesher.Build(new Primitive { Kind = PrimitiveKind.Stairs, Steps = 6 }, size, Budget)) * 2 * 6 / 7, 3);
    }

    [Fact]
    public void WedgesRiseTowardPositiveX()
    {
        var corners = PrimitiveMesher.Build(Make(PrimitiveKind.Wedge), new Vector3(2, 1, 2), Budget);
        Assert.Contains(corners, c => c.X == 1 && c.Y == 1);
        Assert.DoesNotContain(corners, c => c.X < 0 && c.Y > 0);
    }

    [Fact]
    public void RoundPrimitivesKeepTheSameFacetsPerMeter()
    {
        // A cylinder of n sides has two caps of n - 2 triangles and n sides of 2.
        int Sides(float diameter) => (PrimitiveMesher.Build(Make(PrimitiveKind.Cylinder), new Vector3(diameter, 1, diameter), Budget).Length / 3 + 4) / 4;
        Assert.Equal(13, Sides(2)); // 2π m around at 0.5 m a facet
        Assert.Equal(25, Sides(4));
        Assert.Equal(6, Sides(0.1f)); // the minimum
        Assert.Equal(48, Sides(100)); // the maximum
        Assert.Equal(4, (PrimitiveMesher.Build(new Primitive { Kind = PrimitiveKind.Cylinder, Sides = 4 }, Vector3.One * 9, Budget).Length / 3 + 4) / 4);
    }

    [Fact]
    public void FourSidedConesAreSquareToTheAxes()
    {
        var corners = PrimitiveMesher.Build(new Primitive { Kind = PrimitiveKind.Cone, Sides = 4 }, Vector3.One, Budget);
        Assert.All(corners.Where(c => c.Y == 0), c => Assert.Equal(0.5f / MathF.Sqrt(2), MathF.Abs(c.X), 4));
    }

    [Theory]
    [InlineData(PrimitiveKind.Tetrahedron, 4)]
    [InlineData(PrimitiveKind.Octahedron, 8)]
    [InlineData(PrimitiveKind.Dodecahedron, 12)]
    [InlineData(PrimitiveKind.Icosahedron, 20)]
    public void SolidsAreRegularAndAMeterTall(PrimitiveKind kind, int faces)
    {
        var triangles = PrimitiveMesher.Build(Make(kind), Vector3.One, Budget);
        Assert.Equal(0, triangles.Min(c => c.Y), 4);
        Assert.Equal(1, triangles.Max(c => c.Y), 4);

        var normals = Enumerable.Range(0, triangles.Length / 3)
            .Select(i => Vector3.Normalize(Vector3.Cross(triangles[3 * i + 1] - triangles[3 * i], triangles[3 * i + 2] - triangles[3 * i])))
            .ToArray();
        Assert.Equal(faces, normals.Select(n => Key(n, 3)).Distinct().Count());
        if (kind == PrimitiveKind.Octahedron)
        {
            return; // standing on a tip, checked below
        }

        Assert.Contains(normals, n => n.Y < -0.9999f); // standing on a face

        // Every corner is the same distance from the middle.
        var corners = triangles.DistinctBy(c => Key(c)).ToArray();
        var middle = corners.Aggregate(Vector3.Zero, (sum, c) => sum + c) / corners.Length;
        var distances = corners.Select(c => Vector3.Distance(c, middle)).ToArray();
        Assert.Equal(distances.Min(), distances.Max(), 3);
    }

    [Fact]
    public void OctahedraAreRegular()
    {
        var corners = PrimitiveMesher.Build(Make(PrimitiveKind.Octahedron), Vector3.One, Budget);
        var middle = new Vector3(0, 0.5f, 0);
        Assert.All(corners, c => Assert.Equal(0.5f, Vector3.Distance(c, middle), 4));
    }

    [Fact]
    public void TetrahedraFaceForward()
    {
        var triangles = PrimitiveMesher.Build(Make(PrimitiveKind.Tetrahedron), Vector3.One, Budget);
        var normals = Enumerable.Range(0, triangles.Length / 3)
            .Select(i => Vector3.Normalize(Vector3.Cross(triangles[3 * i + 1] - triangles[3 * i], triangles[3 * i + 2] - triangles[3 * i])));
        Assert.Contains(normals, n => MathF.Abs(n.X) < 1e-4f && n.Z > 0.5f);
    }

    // Rounded, with -0 as 0.
    private static string Key(Vector3 v, int decimals = 4) => $"{MathF.Round(v.X, decimals) + 0f},{MathF.Round(v.Y, decimals) + 0f},{MathF.Round(v.Z, decimals) + 0f}";

    // The volume inside triangles wound with outward normals along Cross(b - a, c - a): positive when they face out.
    private static float Volume(Vector3[] triangles)
    {
        var volume = 0f;
        for (var i = 0; i < triangles.Length; i += 3)
        {
            volume += Vector3.Dot(triangles[i], Vector3.Cross(triangles[i + 1], triangles[i + 2])) / 6;
        }

        return volume;
    }
}
