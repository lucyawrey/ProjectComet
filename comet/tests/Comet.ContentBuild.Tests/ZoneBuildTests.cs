using System.Numerics;
using Comet.Content;
using Comet.ZoneFiles;
using Group = Comet.Content.Group;

namespace Comet.ContentBuild.Tests;

public class ZoneBuildTests
{
    private static Zone Zone(string id, params string[] spawns) => new()
    {
        Id = id,
        Chunks =
        [
            new ZoneChunk
            {
                Coord = new ChunkCoord(0, 0),
                Spawns = spawns.Select((s, i) => new SpawnPoint { Id = s, Position = new Vector3(i, 0, 1), Radius = 2, IsDefault = i == 0 }).ToArray(),
            },
        ],
    };

    private static ContentBuildResult Build(ContentFolder folder) =>
        new ContentBuilder(folder.Root).Add("widget", "widgets", TestToml.Default.Widget).AddAssets(Path.Combine(folder.Root, "models")).AddGroups().AddZones().Build();

    // A zone with one placement, and a 1 m plate asset (its model spans x -1..0, z 0..1 in Unity's space).
    private static void WritePlacement(ContentFolder folder, Placement placement, bool uniformScale = false)
    {
        var model = TestModels.Model(("Plate", Vector3.Zero));
        Directory.CreateDirectory(Path.Combine(folder.Root, "models"));
        File.Move(model, Path.Combine(folder.Root, "models", "plate.glb"));
        folder.Write("assets/plate.toml", $"id = \"asset.plate\"\nmodel = \"plate.glb\"\nuniform_scale = {(uniformScale ? "true" : "false")}\n");
        var zone = Zone("zone.meadow");
        zone.Chunks[0].Placements = [placement];
        ZoneFolder.Write(Path.Combine(folder.Root, "zones", "meadow"), zone);
    }

    [Fact]
    public void ZonesAndSpawnsGetRegistryNumbers()
    {
        using var folder = new ContentFolder();
        ZoneFolder.Write(Path.Combine(folder.Root, "zones", "meadow"), Zone("zone.meadow", "spawn.gate", "spawn.well"));
        ZoneFolder.Write(Path.Combine(folder.Root, "zones", "cave"), Zone("zone.cave"));

        var result = Build(folder);

        Assert.Empty(result.Errors);
        Assert.Equal([("zone.cave", 1), ("zone.meadow", 2)], result.Zones.Select(z => (z.Id, z.Number)));
        Assert.Equal([1, 2], result.Zones[1].Chunks[0].Spawns.Select(s => s.Number));
        Assert.Contains("[spawn]\ngate = 1\nwell = 2\n", File.ReadAllText(folder.Registry).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void ZoneErrorsNameTheFile()
    {
        using var folder = new ContentFolder();
        var zone = Zone("zone.meadow", "spawn.gate");
        zone.Chunks[0].Spawns[0].Position = new Vector3(-1, 0, 0);
        ZoneFolder.Write(Path.Combine(folder.Root, "zones", "meadow"), zone);

        var error = Assert.Single(Build(folder).Errors);

        Assert.Equal(Path.Combine(folder.Root, "zones", "meadow", "chunks/0_0.toml"), error.File);
        Assert.StartsWith("spawn spawn.gate at [-1, 0, 0] is in chunk -1_0", error.Message);
    }

    [Fact]
    public void ZoneAndSpawnKeysAreUniqueAcrossZones()
    {
        using var folder = new ContentFolder();
        ZoneFolder.Write(Path.Combine(folder.Root, "zones", "a"), Zone("zone.meadow", "spawn.gate"));
        ZoneFolder.Write(Path.Combine(folder.Root, "zones", "b"), Zone("zone.meadow"));
        ZoneFolder.Write(Path.Combine(folder.Root, "zones", "c"), Zone("zone.cave", "spawn.gate"));

        Assert.Equal(
            ["'zone.meadow' is already used by", "spawn 'spawn.gate' is already used by zone.meadow."],
            Build(folder).Errors.Select(e => e.Message.StartsWith("'zone", StringComparison.Ordinal) ? e.Message[..32] : e.Message));
    }

    [Fact]
    public void AssetsAreBakedFromTheirModels()
    {
        using var folder = new ContentFolder();
        WritePlacement(folder, new Placement { Asset = "asset.plate", Position = new Vector3(10, 0, 10) });

        var result = Build(folder);

        Assert.Empty(result.Errors);
        var asset = Assert.Single(result.Entries<Asset>());
        Assert.Equal((1, 3), (asset.Number, asset.Collision.Length));
        Assert.Equal((new Vector3(-1, 1, 0), new Vector3(0, 1, 1)), (asset.BoundsMin, asset.BoundsMax));
    }

    [Fact]
    public void PlacementsNeedAKnownAsset()
    {
        using var folder = new ContentFolder();
        WritePlacement(folder, new Placement { Asset = "asset.plank", Position = new Vector3(10, 0, 10) });

        Assert.EndsWith("there's no asset 'asset.plank'.", Assert.Single(Build(folder).Errors).Message);
    }

    [Fact]
    public void UniformScaleAssetsScaleEvenly()
    {
        using var folder = new ContentFolder();
        WritePlacement(folder, new Placement { Asset = "asset.plate", Position = new Vector3(10, 0, 10), Scale = new Vector3(2, 1, 2) }, uniformScale: true);

        Assert.EndsWith("asset.plate only scales evenly, the same on every axis.", Assert.Single(Build(folder).Errors).Message);
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
        WritePlacement(folder, new Placement { Asset = "asset.plate", Position = new Vector3(x, 0, 1), Rotation = new Vector3(0, yaw, 0), Scale = new Vector3(scale, 1, 1) });

        var errors = Build(folder).Errors;

        Assert.Equal(fits, errors.Count == 0);
        Assert.All(errors, e => Assert.Contains("reaches past the chunks around its own", e.Message));
    }

    [Fact]
    public void ModelPathsStayInsideTheModelFolder()
    {
        using var folder = new ContentFolder();
        WritePlacement(folder, new Placement { Asset = "asset.plate", Position = new Vector3(10, 0, 10) });
        folder.Write("assets/plate.toml", "id = \"asset.plate\"\nmodel = \"../plate.glb\"\n");

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
    public void GroupErrorsNameTheGroupFile()
    {
        using var folder = new ContentFolder();
        folder.Write("groups/loop.toml", "id = \"group.loop\"\n\n[[part]]\ngroup = \"group.loop\"\nposition = [0.0, 0.0, 0.0]\n");

        var error = Assert.Single(Build(folder).Errors);

        Assert.EndsWith(Path.Combine("groups", "loop.toml"), error.File);
        Assert.Contains("contains itself", error.Message);
    }
}
