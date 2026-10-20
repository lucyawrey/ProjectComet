using System.Numerics;
using Comet.Content;
using Prop = Comet.Content.Prop;

namespace Comet.ContentBuild.Tests;

public class ModelBakerTests
{
    private static Prop Bake(string path)
    {
        try
        {
            var prop = new Prop();
            ModelBaker.Bake(prop, path);
            return prop;
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ConvertsToUnitysSpace()
    {
        var prop = Bake(TestModels.Model(("Plate", new Vector3(2, 0, 0))));

        // X is negated (glTF's +X is Unity's -X) and the winding reversed, so the triangle still faces up.
        Assert.Equal([new Vector3(-2, 1, 0), new Vector3(-3, 1, 0), new Vector3(-2, 1, 1)], prop.Collision);
        var (a, b, c) = (prop.Collision[0], prop.Collision[1], prop.Collision[2]);
        Assert.True(Vector3.Cross(b - a, c - a).Y > 0);
        Assert.Equal((new Vector3(-3, 1, 0), new Vector3(-2, 1, 1)), (prop.BoundsMin, prop.BoundsMax));
    }

    [Fact]
    public void CollisionComesFromColObjectsWhenThereAreAny()
    {
        var prop = Bake(TestModels.Model(("Body", Vector3.Zero), ("Body-col", new Vector3(0, 5, 0))));

        Assert.Equal(3, prop.Collision.Length);
        Assert.All(prop.Collision, p => Assert.Equal(6, p.Y));
        Assert.Equal((1f, 6f), (prop.BoundsMin.Y, prop.BoundsMax.Y));
    }

    [Fact]
    public void WithoutColObjectsEveryMeshCollides()
    {
        Assert.Equal(6, Bake(TestModels.Model(("Top", Vector3.Zero), ("Bottom", new Vector3(0, -1, 0)))).Collision.Length);
    }

    [Fact]
    public void BadModelsFailClearly()
    {
        var path = Path.Combine(Path.GetTempPath(), $"comet-model-{Guid.NewGuid():N}.glb");
        File.WriteAllText(path, "not a model");

        Assert.StartsWith("Can't read the model", Assert.Throws<ContentException>(() => Bake(path)).Message);
        Assert.StartsWith("Can't read the model", Assert.Throws<ContentException>(() => ModelBaker.Bake(new Prop(), "missing.glb")).Message);
    }
}
