using System.Numerics;
using Comet.Content;
using Comet.ZoneFiles;

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
        new ContentBuilder(folder.Root).Add("widget", "widgets", TestToml.Default.Widget).AddZones().Build();

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
}
