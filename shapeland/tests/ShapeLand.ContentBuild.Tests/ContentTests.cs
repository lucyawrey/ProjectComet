using Comet.Content;
using Comet.Simulation;
using MessagePack;
using ShapeLand.ContentBuild;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.World;

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
        Assert.Throws<ContentException>(() => content.Shapes["shape.sphere"]);
    }

    [Fact]
    public void TheContentBuildsWithItsZone()
    {
        // The zone is made by hand in the editor, so only that it builds is checked: the build itself reports
        // missing spawns and the like.
        var (built, errors) = ShapeLandBuild.Build(ContentRoot, saveRegistry: false);
        Assert.Empty(errors);
        var file = new MemoryStream();
        built!.Save(file);
        file.Position = 0;

        var content = ShapeLandContent.Load(file);
        Assert.NotEmpty(content.Zones[content.Settings.World.DefaultZone].Chunks);
    }

    [Fact]
    public void TheTestsZoneHasWhatThePlayTestsCountOn()
    {
        // The play-mode tests walk straight ahead from its default spawn (facing +Z, no scatter) and jump there, then
        // walk on off the edge. Change zones/tests only along with them.
        var (content, errors) = ShapeLandBuild.Build(ContentRoot, saveRegistry: false);
        Assert.Empty(errors);
        var zone = content!.Zones["zone.tests"];
        var world = ShapeLandWorld.Create(content, zone);
        var spawn = new Spawns(zone).Default!;
        Assert.Equal((SpawnFacing.Direction, 0f, 0f), (spawn.Facing, spawn.Yaw, spawn.Radius));

        var feet = ShapeLandWorld.SpawnPoint(content, zone, world);
        var radius = content.Movement.BodyRadius;
        for (var ahead = 2; ahead <= 19; ahead++) // past the spawn marker, flat ground at 0 m
        {
            Assert.True(world.TryGetGround(feet + new System.Numerics.Vector3(0, 1, ahead), radius, feet.Y + 1, out var ground), $"No ground {ahead} m ahead.");
            Assert.Equal(0f, ground, 0.01f);
        }

        Assert.False(world.TryGetGround(feet + new System.Numerics.Vector3(0, 1, 22), radius, feet.Y + 1, out _), "No drop 22 m ahead.");
    }

    [Fact]
    public void PrimitivesCollideAsModeled()
    {
        var (built, errors) = ShapeLandBuild.Build(ContentRoot, saveRegistry: false);
        Assert.Empty(errors);

        Assert.Equal(["asset.cone", "asset.cube", "asset.cylinder", "asset.ramp", "asset.sphere"], built!.Assets.All.Select(a => a.Id).Order());
        var cube = built.Assets["asset.cube"];
        Assert.Equal(12 * 3, cube.Collision.Length);
        Assert.Equal((new System.Numerics.Vector3(-1, 0, -1), new System.Numerics.Vector3(1, 2, 1)), (cube.BoundsMin, cube.BoundsMax));
        Assert.All(built.Assets.All, a => Assert.Equal(0, a.BoundsMin.Y, 0.001f));
    }

    [Fact]
    public void FingerprintsMatchTheFileAndChangeWithTheContent()
    {
        var (built, _) = ShapeLandBuild.Build(ContentRoot, saveRegistry: false);
        var file = new MemoryStream();
        built!.Save(file);
        file.Position = 0;
        var loaded = ShapeLandContent.Load(file);
        Assert.NotEqual(0UL, built.Fingerprint);
        Assert.Equal(built.Fingerprint, loaded.Fingerprint);

        loaded.Shapes["shape.cube"].MaxSpeed += 0.5f;
        loaded.Save(new MemoryStream());
        Assert.NotEqual(built.Fingerprint, loaded.Fingerprint);
    }

    [Fact]
    public void TerrainIsTheSameForTheSameSeed()
    {
        Assert.Equal(TestTerrain.GenerateZone(1).Chunks[3].Tiles[0].Heights.Samples, TestTerrain.GenerateZone(1).Chunks[3].Tiles[0].Heights.Samples);
        Assert.NotEqual(TestTerrain.GenerateZone(1).Chunks[3].Tiles[0].Heights.Samples, TestTerrain.GenerateZone(2).Chunks[3].Tiles[0].Heights.Samples);
    }

    [Fact]
    public void TerrainIsAFloatingDisc()
    {
        var island = new ZoneTerrain(TestTerrain.GenerateZone(1))["island"];

        Assert.True(island.TryGetHeight(0, 0, out _));
        Assert.True(island.TryGetHeight(26, 0, out _));
        Assert.False(island.TryGetHeight(30, 0, out _));
        Assert.False(island.TryGetHeight(-27, -27, out _));
        Assert.Equal((-28, -28, 28, 28), island.Extent);
    }

    [Fact]
    public void BlocksStandOnTheIsland()
    {
        var zone = TestTerrain.GenerateZone(1);
        var island = new ZoneTerrain(zone)["island"];
        var blocks = zone.Chunks.SelectMany(c => c.Placements).Where(p => p.Name == null).ToList();

        Assert.Equal(4, blocks.Count);
        Assert.All(blocks, b =>
        {
            Assert.Equal("asset.cube", b.Asset);
            Assert.True(island.TryGetHeightAt(b.Position.X, b.Position.Z, out var ground));
            Assert.Equal(ground - 0.5f, b.Position.Y, 2);
        });
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
    [InlineData("scale = 0.0", "scale: 0 is out of range")]
    [InlineData("scale = 5.0", "scale: 5 is out of range")]
    [InlineData("max_speed = nan", "content numbers must be finite")]
    [InlineData("scale = nan", "content numbers must be finite")]
    public void ShapeRulesAreChecked(string line, string message, string shape = "cube")
    {
        var root = Path.Combine(Path.GetTempPath(), "shapeland-content-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(ContentRoot, root);
            var file = Path.Combine(root, "shapes", shape + ".toml");
            var key = line[..line.IndexOf(' ')];
            var lines = File.ReadAllLines(file).ToList();
            var at = lines.FindIndex(l => l.StartsWith(key + " "));
            if (at >= 0)
            {
                lines[at] = line;
            }
            else
            {
                lines.Add(line);
            }

            File.WriteAllLines(file, lines);

            var (built, errors) = ShapeLandBuild.Build(root, saveRegistry: false, Path.Combine(ContentRoot, ShapeLandBuild.ModelRoot));

            Assert.Null(built);
            var error = Assert.Single(errors);
            Assert.EndsWith(shape + ".toml", error.File);
            Assert.Contains(message, error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ContentWithNoShapesFails()
    {
        var root = Path.Combine(Path.GetTempPath(), "shapeland-content-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(ContentRoot, root);
            Directory.Move(Path.Combine(root, "shapes"), Path.Combine(root, "shapez"));

            var (built, errors) = ShapeLandBuild.Build(root, saveRegistry: false, Path.Combine(ContentRoot, ShapeLandBuild.ModelRoot));

            Assert.Null(built);
            Assert.Contains("No shapes", Assert.Single(errors).Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // IL2CPP and web builds can't generate code at run time, so every content type and message needs a source-generated formatter.
    [Fact]
    public void EveryMessagePackTypeHasASourceGeneratedFormatter()
    {
        var contentTypes = typeof(Shape).Assembly.GetTypes()
            .Concat(typeof(Heightmap).Assembly.GetTypes())
            .Where(t => t.GetCustomAttributes(typeof(MessagePackObjectAttribute), false).Length > 0)
            .ToList();
        var resolvers = new[] { typeof(Shape).Assembly.GetType("ShapeLand.Shared.Content.ShapeLandResolver")!, typeof(Heightmap).Assembly.GetType("Comet.Content.ContentResolver")! }
            .Select(t => (IFormatterResolver)t.GetField("Instance")!.GetValue(null)!)
            .ToList();
        var getFormatter = typeof(IFormatterResolver).GetMethod(nameof(IFormatterResolver.GetFormatter))!;

        Assert.Contains(typeof(Shape), contentTypes);
        Assert.Contains(typeof(Shared.Messages.PlayerSpawn), contentTypes);
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
