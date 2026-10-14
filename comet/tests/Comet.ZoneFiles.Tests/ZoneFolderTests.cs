using System.Numerics;
using Comet.Content;

namespace Comet.ZoneFiles.Tests;

public class ZoneFolderTests
{
    private static Zone RoundTrip(Zone zone, TempFolder folder, out List<ZoneFileError> errors)
    {
        ZoneFolder.Write(folder.Root, zone);
        errors = [];
        return ZoneFolder.Read(folder.Root, errors);
    }

    [Fact]
    public void RoundTripsAZone()
    {
        using var folder = new TempFolder();
        var zone = RoundTrip(Zones.Sample(), folder, out var errors);

        Assert.Empty(errors);
        Assert.Empty(ZoneValidation.Check(zone));
        Assert.Equal("zone.meadow", zone.Id);
        var piece = Assert.Single(zone.Pieces);
        Assert.Equal(("main", -8f, false, true), (piece.Name, piece.BottomHeight, piece.RimSkirts, piece.HoleSkirts));
        Assert.Equal((new ChunkCoord(0, 0), ChunkSide.North, "zone.hills"), (zone.Borders[0].Chunk, zone.Borders[0].Side, zone.Borders[0].Zone));
        Assert.Equal(("cave", "zone.cave", "mouth"), (zone.Entrances[0].Name, zone.Entrances[0].ToZone, zone.Entrances[0].ToEntrance));
        Assert.Equal(("hall", "cave"), (zone.TransitionRooms[0].Name, zone.TransitionRooms[0].Entrance));

        Assert.Equal([new ChunkCoord(-1, 0), new ChunkCoord(0, 0)], zone.Chunks.Select(c => c.Coord));
        var placement = Assert.Single(zone.Chunks[0].Placements);
        Assert.Equal(("asset.cube", new Vector3(-10.5f, 4, 3.25f), new Vector3(0, 45, 0), new Vector3(2, 1, 2)), (placement.Asset, placement.Position, placement.Rotation, placement.Scale));
        var tile = zone.Chunks[1].Tiles[0];
        Assert.Equal(Zones.Sample().Chunks[1].Tiles[0].Heights.Samples, tile.Heights.Samples);
        Assert.Equal(0x66aa33u, tile.Colors![3, 5]);
        Assert.Null(tile.Underside);
        var spawn = Assert.Single(zone.Chunks[1].Spawns);
        Assert.Equal(("spawn.meadow_gate", 3f, SpawnFacing.Direction, 90f, true), (spawn.Id, spawn.Radius, spawn.Facing, spawn.Yaw, spawn.IsDefault));
        Assert.Equal((VolumeKind.Wall, new Vector3(1, 4, 6)), (zone.Chunks[1].Volumes[0].Kind, zone.Chunks[1].Volumes[0].Size));
    }

    [Fact]
    public void WritesReadableChunkFiles()
    {
        using var folder = new TempFolder();
        ZoneFolder.Write(folder.Root, Zones.Sample());

        Assert.Equal(
            """
            [[tile]]
            piece = "main"
            origin = [32, 0]

            [[placement]]
            asset = "asset.cube"
            position = [-10.5, 4.0, 3.25]
            rotation = [0.0, 45.0, 0.0]
            scale = [2.0, 1.0, 2.0]

            """,
            File.ReadAllText(folder.PathOf("chunks/-1_0.toml")).ReplaceLineEndings("\n"));
        Assert.True(File.Exists(folder.PathOf("chunks/-1_0/main.height")));
        Assert.True(File.Exists(folder.PathOf("chunks/0_0/main.color")));
        Assert.False(File.Exists(folder.PathOf("chunks/0_0/main.under.height")));
    }

    [Fact]
    public void RoundsToMillimetersAndHundredthsOfADegree()
    {
        using var folder = new TempFolder();
        var zone = Zones.Sample();
        zone.Chunks[0].Placements[0].Position = new Vector3(-10.50004f, 4.0001f, 3.2501f);
        zone.Chunks[0].Placements[0].Rotation = new Vector3(-0.001f, 44.999f, 0.004f);

        var placement = RoundTrip(zone, folder, out _).Chunks[0].Placements[0];

        Assert.Equal(new Vector3(-10.5f, 4, 3.25f), placement.Position);
        Assert.Equal(new Vector3(0, 45, 0), placement.Rotation);
        Assert.DoesNotContain("-0.0", File.ReadAllText(folder.PathOf("chunks/-1_0.toml")));
    }

    [Fact]
    public void LeavesUnchangedFilesAlone()
    {
        using var folder = new TempFolder();
        ZoneFolder.Write(folder.Root, Zones.Sample());
        var untouched = DateTime.UtcNow.AddDays(-1);
        foreach (var file in Directory.EnumerateFiles(folder.Root, "*", SearchOption.AllDirectories))
        {
            File.SetLastWriteTimeUtc(file, untouched);
        }

        var zone = Zones.Sample();
        zone.Chunks[1].Spawns[0].Radius = 4;
        ZoneFolder.Write(folder.Root, zone);

        Assert.NotEqual(untouched, File.GetLastWriteTimeUtc(folder.PathOf("chunks/0_0.toml")));
        Assert.Equal(untouched, File.GetLastWriteTimeUtc(folder.PathOf("chunks/-1_0.toml")));
        Assert.Equal(untouched, File.GetLastWriteTimeUtc(folder.PathOf("chunks/0_0/main.height")));
        Assert.Equal(untouched, File.GetLastWriteTimeUtc(folder.PathOf("zone.toml")));
    }

    [Fact]
    public void RemovesChunksAndTilesTheZoneNoLongerHas()
    {
        using var folder = new TempFolder();
        ZoneFolder.Write(folder.Root, Zones.Sample());
        folder.Write("chunks/notes.txt", "kept");

        var zone = Zones.Sample();
        zone.Chunks = [zone.Chunks[1]];
        zone.Chunks[0].Tiles[0].Colors = null;
        ZoneFolder.Write(folder.Root, zone);

        Assert.False(File.Exists(folder.PathOf("chunks/-1_0.toml")));
        Assert.False(Directory.Exists(folder.PathOf("chunks/-1_0")));
        Assert.False(File.Exists(folder.PathOf("chunks/0_0/main.color")));
        Assert.True(File.Exists(folder.PathOf("chunks/0_0/main.height")));
        Assert.True(File.Exists(folder.PathOf("chunks/notes.txt")));
    }

    [Fact]
    public void ReportsProblemsByFileAndKey()
    {
        using var folder = new TempFolder();
        ZoneFolder.Write(folder.Root, Zones.Sample());
        folder.Write("chunks/0_0.toml",
            """
            [[tile]]
            piece = "main"
            origin = [0, 0]

            [[placement]]
            asset = "asset.cube"
            positon = [1, 2, 3]

            [[spawn]]
            id = "spawn.gate"
            position = [1, 2]
            radius = "far"
            facing = "sideways"
            """);
        folder.Write("chunks/0_0/old.height", "");
        folder.Write("chunks/zero.toml", "");

        var errors = new List<ZoneFileError>();
        ZoneFolder.Read(folder.Root, errors);

        Assert.Equal(
            [
                "chunks/zero.toml: Chunk files are named by their grid position, x_z (such as 0_-1.toml).",
                "chunks/0_0.toml: placement[0].positon: unknown key.",
                "chunks/0_0.toml: placement[0].position: is missing.",
                "chunks/0_0.toml: spawn[0].position: should be 3 numbers.",
                "chunks/0_0.toml: spawn[0].radius: should be a number.",
                "chunks/0_0.toml: spawn[0].facing: should be one of: direction, toward_point, random.",
                "chunks/0_0/old.height: No tile in chunks/0_0.toml uses this file.",
            ],
            errors.Select(e => e.ToString()).OrderBy(e => e.StartsWith("chunks/zero", StringComparison.Ordinal) ? 0 : 1));
    }

    [Fact]
    public void ReportsAMissingTileFileAndAMissingZoneFile()
    {
        using var folder = new TempFolder();
        ZoneFolder.Write(folder.Root, Zones.Sample());
        File.Delete(folder.PathOf("chunks/-1_0/main.height"));
        File.Delete(folder.PathOf("zone.toml"));

        var errors = new List<ZoneFileError>();
        ZoneFolder.Read(folder.Root, errors);

        Assert.Equal(
            ["zone.toml: No zone file: each zone folder needs one."],
            errors.Select(e => e.ToString()));

        ZoneFolder.Write(folder.Root, new Zone { Id = "zone.x" });
        folder.Write("chunks/0_0.toml", "[[tile]]\npiece = \"main\"\norigin = [0, 0]\n");
        errors.Clear();
        ZoneFolder.Read(folder.Root, errors);

        Assert.Equal(["chunks/0_0/main.height: Missing: the chunk lists a tile for this piece."], errors.Select(e => e.ToString()));
    }
}
