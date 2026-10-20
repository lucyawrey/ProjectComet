using System.Numerics;
using Comet.Content;
using Comet.ZoneFiles;
using Group = Comet.Content.Group;

namespace Comet.ContentBuild.Tests;

public class ZoneBuildTests
{
    // A zone with spawn points named by spawns (spawn.<zone>.<name>).
    private static Zone Zone(string id, params string[] spawns) => new()
    {
        Id = id,
        Chunks =
        [
            new ZoneChunk
            {
                Coord = new ChunkCoord(0, 0),
                Spawns = spawns.Select((s, i) => new SpawnPoint
                {
                    Id = $"spawn.{id[5..]}.{s}",
                    Volume = new TriggerVolume { Kind = PrimitiveKind.Cylinder, Position = new Vector3(i, 0, 1), Size = new Vector3(4, 2, 4) },
                }).ToArray(),
            },
        ],
    };

    private static Teleporter Teleporter(string id, string target, float x) =>
        new() { Id = id, Target = target, Volume = new TriggerVolume { Position = new Vector3(x, 0, 4), Size = new Vector3(2, 2, 2) } };

    private static ContentBuildResult Build(ContentFolder folder) =>
        new ContentBuilder(folder.Root).Add("widget", "widgets", TestToml.Default.Widget).AddProps(Path.Combine(folder.Root, "models")).AddGroups().AddZones().Build();

    // A zone with one placement, and a 1 m plate prop (its model spans x -1..0, z 0..1 in Unity's space).
    private static void WritePlacement(ContentFolder folder, Placement placement, bool uniformScale = false)
    {
        var model = TestModels.Model(("Plate", Vector3.Zero));
        Directory.CreateDirectory(Path.Combine(folder.Root, "models"));
        File.Move(model, Path.Combine(folder.Root, "models", "plate.glb"));
        folder.Write("props/plate.toml", $"id = \"prop.plate\"\nmodel = \"plate.glb\"\nuniform_scale = {(uniformScale ? "true" : "false")}\n");
        var zone = Zone("zone.meadow");
        zone.Chunks[0].Placements = [placement];
        ZoneFolder.Write(Path.Combine(folder.Root, "zones", "meadow"), zone);
    }

    [Fact]
    public void ZonesAndSpawnsGetRegistryNumbers()
    {
        using var folder = new ContentFolder();
        var meadow = Zone("zone.meadow", "gate", "well");
        meadow.Chunks[0].Teleporters = [Teleporter("teleporter.meadow.to_cave", "teleporter.cave.to_meadow", 8)];
        meadow.Chunks[0].Triggers = [new Trigger { Id = "trigger.meadow.plate", Volume = new TriggerVolume { Position = new Vector3(12, 0, 4), Size = Vector3.One } }];
        var cave = Zone("zone.cave");
        cave.Chunks[0].Teleporters = [Teleporter("teleporter.cave.to_meadow", "teleporter.meadow.to_cave", 8)];
        ZoneFolder.Write(Path.Combine(folder.Root, "zones", "meadow"), meadow);
        ZoneFolder.Write(Path.Combine(folder.Root, "zones", "cave"), cave);

        var result = Build(folder);

        Assert.Empty(result.Errors);
        Assert.Equal([("zone.cave", 1), ("zone.meadow", 2)], result.Zones.Select(z => (z.Id, z.Number)));
        Assert.Equal([1, 2], result.Zones[1].Chunks[0].Spawns.Select(s => s.Number));
        Assert.Equal([1, 2], result.Zones.Select(z => z.Chunks[0].Teleporters[0].Number));
        Assert.Equal(1, result.Zones[1].Chunks[0].Triggers[0].Number);
        var registry = File.ReadAllText(folder.Registry).ReplaceLineEndings("\n");
        Assert.Contains("[spawn]\n\"meadow.gate\" = 1\n\"meadow.well\" = 2\n", registry);
        Assert.Contains("[teleporter]\n\"cave.to_meadow\" = 1\n\"meadow.to_cave\" = 2\n", registry);

        // The quoted keys read back: a second build keeps the numbers.
        Assert.Empty(Build(folder).Errors);
        Assert.Equal(registry, File.ReadAllText(folder.Registry).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void ZoneErrorsNameTheFile()
    {
        using var folder = new ContentFolder();
        var zone = Zone("zone.meadow", "gate");
        zone.Chunks[0].Spawns[0].Volume.Position = new Vector3(-1, 0, 0);
        ZoneFolder.Write(Path.Combine(folder.Root, "zones", "meadow"), zone);

        var error = Assert.Single(Build(folder).Errors);

        Assert.Equal(Path.Combine(folder.Root, "zones", "meadow", "chunks/0_0.toml"), error.File);
        Assert.StartsWith("spawn.meadow.gate at [-1, 0, 0] is in chunk -1_0", error.Message);
    }

    [Fact]
    public void ZoneKeysAreUnique()
    {
        using var folder = new ContentFolder();
        ZoneFolder.Write(Path.Combine(folder.Root, "zones", "a"), Zone("zone.meadow", "gate"));
        ZoneFolder.Write(Path.Combine(folder.Root, "zones", "b"), Zone("zone.meadow"));

        Assert.StartsWith("'zone.meadow' is already used by", Assert.Single(Build(folder).Errors).Message);
    }

    [Fact]
    public void TeleportersTargetATeleporterInAnyZone()
    {
        using var folder = new ContentFolder();
        var meadow = Zone("zone.meadow");
        meadow.Chunks[0].Teleporters = [Teleporter("teleporter.meadow.to_cave", "teleporter.cave.to_meadow", 8)];
        ZoneFolder.Write(Path.Combine(folder.Root, "zones", "meadow"), meadow);

        Assert.Equal("teleporter.meadow.to_cave: there's no teleporter 'teleporter.cave.to_meadow' to target.", Assert.Single(Build(folder).Errors).Message);
    }

    [Fact]
    public void PropsAreBakedFromTheirModels()
    {
        using var folder = new ContentFolder();
        WritePlacement(folder, new Placement { Prop = "prop.plate", Position = new Vector3(10, 0, 10) });

        var result = Build(folder);

        Assert.Empty(result.Errors);
        var prop = Assert.Single(result.Entries<Prop>());
        Assert.Equal((1, 3), (prop.Number, prop.Collision.Length));
        Assert.Equal((new Vector3(-1, 1, 0), new Vector3(0, 1, 1)), (prop.BoundsMin, prop.BoundsMax));
    }

    [Fact]
    public void PlacementsNeedAKnownProp()
    {
        using var folder = new ContentFolder();
        WritePlacement(folder, new Placement { Prop = "prop.plank", Position = new Vector3(10, 0, 10) });

        Assert.EndsWith("there's no prop 'prop.plank'.", Assert.Single(Build(folder).Errors).Message);
    }

    [Fact]
    public void UniformScaleAssetsScaleEvenly()
    {
        using var folder = new ContentFolder();
        WritePlacement(folder, new Placement { Prop = "prop.plate", Position = new Vector3(10, 0, 10), Scale = new Vector3(2, 1, 2) }, uniformScale: true);

        Assert.EndsWith("prop.plate only scales evenly, the same on every axis.", Assert.Single(Build(folder).Errors).Message);
    }

    [Theory]
    [InlineData(10, 0, 1, true)] // well inside
    [InlineData(1, 0, 64, true)] // spans x -63..1: inside the chunk to the west
    [InlineData(1, 0, 66, false)] // spans x -65..1: past it
    [InlineData(1, 90, 126, true)] // turned so the plate runs toward +Z: z 1..127 (toward -Z it would reach -125)
    [InlineData(1, 90, 128, false)] // z 1..129: past the chunk to the north
    public void ObjectsReachAtMostOneChunkPastTheirOwn(float x, float yaw, float scale, bool fits)
    {
        using var folder = new ContentFolder();
        WritePlacement(folder, new Placement { Prop = "prop.plate", Position = new Vector3(x, 0, 1), Rotation = new Vector3(0, yaw, 0), Scale = new Vector3(scale, 1, 1) });

        var errors = Build(folder).Errors;

        Assert.Equal(fits, errors.Count == 0);
        Assert.All(errors, e => Assert.Contains("reaches past the chunks around its own", e.Message));
    }

    [Fact]
    public void ModelPathsStayInsideTheModelFolder()
    {
        using var folder = new ContentFolder();
        WritePlacement(folder, new Placement { Prop = "prop.plate", Position = new Vector3(10, 0, 10) });
        folder.Write("props/plate.toml", "id = \"prop.plate\"\nmodel = \"../plate.glb\"\n");

        Assert.Contains(Build(folder).Errors, e => e.Message.EndsWith("must be a path inside the model folder, without '..'.", StringComparison.Ordinal));
    }

    [Fact]
    public void GroupCopiesAreExpandedAndGroupsNumbered()
    {
        using var folder = new ContentFolder();
        var box = new Primitive { Kind = PrimitiveKind.Box };
        GroupFile.Write(Path.Combine(folder.Root, "groups", "post.toml"), new Group { Id = "group.post", Parts = [new Placement { Primitive = box, Scale = new Vector3(0.2f, 1, 0.2f) }] });
        GroupFile.Write(Path.Combine(folder.Root, "groups", "fence.toml"), new Group
        {
            Id = "group.fence",
            Parts = [new Placement { Group = "group.post" }, new Placement { Group = "group.post", Position = new Vector3(2, 0, 0) }, new Placement { Primitive = box, Position = new Vector3(1, 0.5f, 0), Scale = new Vector3(2, 0.1f, 0.1f) }],
        });
        var zone = Zone("zone.meadow");
        zone.Chunks[0].Placements = [new Placement { Group = "group.fence", Position = new Vector3(10, 0, 10), Rotation = new Vector3(0, 90, 0) }];
        ZoneFolder.Write(Path.Combine(folder.Root, "zones", "meadow"), zone);

        var result = Build(folder);

        Assert.Empty(result.Errors);
        Assert.Equal([("group.fence", 1), ("group.post", 2)], result.Groups.Select(g => (g.Id, g.Number)));
        var placed = result.Zones[0].Chunks.SelectMany(c => c.Placements).ToList();
        Assert.Equal(3, placed.Count);
        Assert.All(placed, p => Assert.Equal(PrimitiveKind.Box, p.Primitive!.Kind));
        Assert.Contains("[group]\nfence = 1\npost = 2\n", File.ReadAllText(folder.Registry).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void BlockoutPlacementsWarnOrFailARelease()
    {
        using var folder = new ContentFolder();
        var box = new Primitive { Kind = PrimitiveKind.Box };
        GroupFile.Write(Path.Combine(folder.Root, "groups", "hut.toml"), new Group { Id = "group.hut", Parts = [new Placement { Primitive = box, Blockout = true }, new Placement { Primitive = box }] });
        var zone = Zone("zone.meadow");
        zone.Chunks[0].Placements =
        [
            new Placement { Primitive = box, Position = new Vector3(5, 0, 5), Blockout = true },
            new Placement { Group = "group.hut", Position = new Vector3(10, 0, 10) },
            new Placement { Primitive = box, Position = new Vector3(20, 0, 20) },
        ];
        ZoneFolder.Write(Path.Combine(folder.Root, "zones", "meadow"), zone);
        ContentBuilder Builder() => new ContentBuilder(folder.Root).Add("widget", "widgets", TestToml.Default.Widget).AddGroups().AddZones();

        var result = Builder().Build();

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Warnings.Count);
        Assert.All(result.Warnings, w => Assert.True(w.Warning));
        Assert.Contains(result.Warnings, w => w.Message.Contains("a box at [5, 0, 5] is blockout") && w.File.EndsWith("0_0.toml", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.Message.Contains("group.hut at [10, 0, 10] has 1 blockout part"));
        Assert.Contains(": warning: ", result.Warnings[0].ToString());

        var release = Builder().Build(release: true);

        Assert.Equal(2, release.Errors.Count);
        Assert.Empty(release.Warnings);
        Assert.Contains(": error: ", release.Errors[0].ToString());
    }

    [Fact]
    public void GroupsSharingAnIdAreReportedNotThrown()
    {
        using var folder = new ContentFolder();
        var post = new Group { Id = "group.post", Parts = [new Placement { Primitive = new Primitive { Kind = PrimitiveKind.Box } }] };
        GroupFile.Write(Path.Combine(folder.Root, "groups", "post.toml"), post);
        GroupFile.Write(Path.Combine(folder.Root, "groups", "post_copy.toml"), post);

        Assert.Contains("is already used by", Assert.Single(Build(folder).Errors).Message);
    }

    [Fact]
    public void GroupErrorsNameTheGroupFile()
    {
        using var folder = new ContentFolder();
        folder.Write("groups/loop.toml", "id = \"group.loop\"\n\n[[part]]\ngroup = \"group.loop\"\nposition = [0.0, 0.0, 0.0]\n");

        // A zone's copy of a sound group isn't reported as unknown because another group is broken.
        GroupFile.Write(Path.Combine(folder.Root, "groups", "post.toml"), new Group { Id = "group.post", Parts = [new Placement { Primitive = new Primitive { Kind = PrimitiveKind.Box } }] });
        var zone = Zone("zone.meadow");
        zone.Chunks[0].Placements = [new Placement { Group = "group.post", Position = new Vector3(10, 0, 10) }];
        ZoneFolder.Write(Path.Combine(folder.Root, "zones", "meadow"), zone);

        var error = Assert.Single(Build(folder).Errors);

        Assert.EndsWith(Path.Combine("groups", "loop.toml"), error.File);
        Assert.Contains("contains itself", error.Message);
    }
}
