using Comet.Content;
using MessagePack;
using ShapeLand.ContentBuild;
using ShapeLand.Shared.Content;

namespace ShapeLand.ContentBuild.Tests;

public class ContentTests
{
    private static readonly string ContentRoot = Path.Combine(RepoRoot(), "shapeland", "content");

    [Fact]
    public void CompiledContentFindsEachShapeByKey()
    {
        var (built, errors) = ShapeLandBuild.Build(ContentRoot, saveRegistry: false);
        Assert.Empty(errors);
        var file = new MemoryStream();
        built!.Save(file);
        file.Position = 0;

        var content = ShapeLandContent.Load(file);

        Assert.Equal(["shape.cube", "shape.diamond", "shape.pyramid"], content.Shapes.All.Select(s => s.Id));
        Assert.Equal(MeshKind.Diamond, content.Shapes["shape.diamond"].Mesh);
        Assert.Equal("Pyramid", content.Shapes["shape.pyramid"].DisplayName);
        Assert.True(content.Shapes["shape.cube"].MaxSpeed > 0);
        Assert.Same(content.Shapes["shape.cube"], content.Shapes[content.Shapes["shape.cube"].Number]);
        Assert.Equal(TestTerrain.Size, content.Terrain.SizeX);
        Assert.Throws<ContentException>(() => content.Shapes["shape.sphere"]);
    }

    [Fact]
    public void TerrainIsTheSameForTheSameSeed()
    {
        Assert.Equal(TestTerrain.Generate(1).Samples, TestTerrain.Generate(1).Samples);
        Assert.NotEqual(TestTerrain.Generate(1).Samples, TestTerrain.Generate(2).Samples);
    }

    [Fact]
    public void TerrainIsAFloatingDisc()
    {
        var terrain = TestTerrain.Generate(1);
        const int centre = TestTerrain.Size / 2;

        Assert.False(terrain.IsHole(centre, centre));
        Assert.False(terrain.IsHole(centre + 13, centre));
        Assert.True(terrain.IsHole(centre + 15, centre));
        Assert.True(terrain.IsHole(0, 0));
    }

    [Fact]
    public void CorruptContentFailsClearly()
    {
        var error = Assert.Throws<ContentException>(() => ShapeLandContent.Load(new MemoryStream([0xc1, 0x00])));
        Assert.Contains("corrupt", error.Message);
    }

    [Theory]
    [InlineData("max_speed = 0.0", "max_speed: 0 is out of range")]
    [InlineData("jump_velocity = 99.0", "jump_velocity: 99 is out of range")]
    [InlineData("display_name = \" \"", "display_name: must not be empty")]
    [InlineData("mesh = \"sphere\"", "Invalid enum name `sphere`")]
    public void ShapeRulesAreChecked(string line, string message)
    {
        var root = Path.Combine(Path.GetTempPath(), "shapeland-content-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(ContentRoot, root);
            var cube = Path.Combine(root, "shapes", "cube.toml");
            var key = line[..line.IndexOf(' ')];
            File.WriteAllLines(cube, File.ReadAllLines(cube).Select(l => l.StartsWith(key + " ") ? line : l));

            var (built, errors) = ShapeLandBuild.Build(root, saveRegistry: false);

            Assert.Null(built);
            var error = Assert.Single(errors);
            Assert.EndsWith("cube.toml", error.File);
            Assert.Contains(message, error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // IL2CPP and web builds can't generate code at run time, so every content type needs a source-generated formatter.
    [Fact]
    public void EveryContentTypeHasASourceGeneratedFormatter()
    {
        var contentTypes = typeof(Shape).Assembly.GetTypes()
            .Concat(typeof(Heightmap).Assembly.GetTypes())
            .Where(t => t.GetCustomAttributes(typeof(MessagePackObjectAttribute), false).Length > 0)
            .ToList();
        var resolvers = new[] { typeof(Shape).Assembly.GetType("ShapeLand.Shared.Content.ShapeLandResolver")!, typeof(Heightmap).Assembly.GetType("Comet.Content.ContentResolver")! }
            .Select(t => (IFormatterResolver)t.GetField("Instance")!.GetValue(null)!)
            .ToList();
        var getFormatter = typeof(IFormatterResolver).GetMethod(nameof(IFormatterResolver.GetFormatter))!;

        Assert.Equal(3, contentTypes.Count);
        Assert.All(contentTypes, t => Assert.Contains(resolvers, r => getFormatter.MakeGenericMethod(t).Invoke(r, null) is not null));
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ProjectComet.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Can't find the repository root.");
    }
}
