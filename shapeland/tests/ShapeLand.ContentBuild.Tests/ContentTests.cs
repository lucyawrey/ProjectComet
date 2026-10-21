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
        Assert.Equal("group.shape_diamond", content.Shapes["shape.diamond"].Look);
        Assert.Equal(PrimitiveKind.Octahedron, content.BodyKind(content.Shapes["shape.diamond"]));
        Assert.Equal(PrimitiveKind.Tetrahedron, content.BodyKind(content.Shapes["shape.pyramid"]));
        Assert.Equal(PrimitiveKind.Box, content.BodyKind(content.Shapes["shape.cube"]));
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
        Assert.All(content.Shapes.All, shape => Assert.NotEmpty(ShapeLandWorld.StartOf(content, shape).Zone.Chunks));
    }

    [Fact]
    public void TheTestsZoneHasWhatThePlayTestsCountOn()
    {
        // The play-mode tests walk straight ahead from its start spawn (facing +Z, next to no scatter) and jump there,
        // then walk on off the edge. Change zones/tests only along with them.
        var (content, errors) = ShapeLandBuild.Build(ContentRoot, saveRegistry: false);
        Assert.Empty(errors);
        var (zone, spawn) = ShapeLandWorld.FindSpawn(content!, "spawn.tests.start")!.Value;
        Assert.Equal("zone.tests", zone.Id);
        var world = ShapeLandWorld.Create(content!, zone);
        Assert.Equal((SpawnFacing.Direction, 0f), (spawn.Facing, spawn.Volume.Yaw));
        Assert.InRange(spawn.Volume.Size.X, 0, 0.25f);

        var feet = ShapeLandWorld.Center(spawn, world);
        var radius = content!.Movement.BodyRadius;
        for (var ahead = 2; ahead <= 19; ahead++) // past the spawn marker, flat ground at 0 m
        {
            Assert.True(world.TryGetGround(feet + new System.Numerics.Vector3(0, 1, ahead), radius, feet.Y + 1, out var ground), $"No ground {ahead} m ahead.");
            Assert.Equal(0f, ground, 0.01f);
        }

        Assert.False(world.TryGetGround(feet + new System.Numerics.Vector3(0, 1, 22), radius, feet.Y + 1, out _), "No drop 22 m ahead.");
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
        var blocks = zone.Chunks.SelectMany(c => c.Placements).Where(p => p.Tag == null).ToList();

        Assert.Equal(4, blocks.Count);
        Assert.All(blocks, b =>
        {
            Assert.Equal(PrimitiveKind.Box, b.Primitive!.Kind);
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
    [InlineData("look = \"group.nowhere\"", "look 'group.nowhere' isn't a group")]
    [InlineData("hover = 3.0", "hover: 3 is out of range")]
    [InlineData("scale = 0.0", "scale: 0 is out of range")]
    [InlineData("scale = 5.0", "scale: 5 is out of range")]
    [InlineData("max_speed = nan", "content numbers must be finite")]
    [InlineData("scale = nan", "content numbers must be finite")]
    public void ShapeRulesAreChecked(string line, string message, string shape = "cube") =>
        RuleIsChecked(Path.Combine("shapes", shape + ".toml"), line, message);

    [Fact]
    public void LooksAreOnlyPrimitives() =>
        RuleIsChecked(Path.Combine("shapes", "cube.toml"), "look = \"group.prop_look\"", "looks are built only from primitives",
            ("groups/prop_look.toml", "id = \"group.prop_look\"\n\n[[part]]\nprop = \"prop.strange_shape\"\nrole = \"body\"\nposition = [0.0, 0.0, 0.0]\n"));

    [Theory]
    [InlineData("facet_length = 0.0", "facet_length: 0 is out of range")]
    [InlineData("min_sides = 2", "min_sides: 2 is too few")]
    [InlineData("max_sides = 4", "max_sides: 4 is out of range")]
    public void SettingsRulesAreChecked(string line, string message) =>
        RuleIsChecked(Path.Combine("settings", "game.toml"), line, message);

    // Builds a copy of the content with one line of a file replaced (or added) and perhaps a file added, expecting one
    // error naming the changed file.
    private static void RuleIsChecked(string relativeFile, string line, string message, (string Path, string Text)? extraFile = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "shapeland-content-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(ContentRoot, root);
            var file = Path.Combine(root, relativeFile);
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
            if (extraFile is { } extra)
            {
                File.WriteAllText(Path.Combine(root, extra.Path), extra.Text);
            }

            var (built, errors) = ShapeLandBuild.Build(root, saveRegistry: false, Path.Combine(ContentRoot, ShapeLandBuild.ModelRoot));

            Assert.Null(built);
            var error = Assert.Single(errors);
            Assert.EndsWith(Path.GetFileName(relativeFile), error.File);
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
