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
        Assert.Equal(("main", 12f, false, true), (piece.Name, piece.SkirtDepth, piece.RimSkirts, piece.HoleSkirts));
        Assert.Equal((new ChunkCoord(0, 0), ChunkSide.North, "zone.hills"), (zone.Borders[0].Chunk, zone.Borders[0].Side, zone.Borders[0].Zone));
        Assert.Equal(("cave", "zone.cave", "mouth"), (zone.Entrances[0].Name, zone.Entrances[0].ToZone, zone.Entrances[0].ToEntrance));
        Assert.Equal(("hall", "cave"), (zone.TransitionRooms[0].Name, zone.TransitionRooms[0].Entrance));

        Assert.Equal([new ChunkCoord(-1, 0), new ChunkCoord(0, 0)], zone.Chunks.Select(c => c.Coord));
        var placement = Assert.Single(zone.Chunks[0].Placements);
        Assert.Equal(("prop.cube", new Vector3(-10.5f, 4, 3.25f), new Vector3(0, 45, 0), new Vector3(2, 1, 2)), (placement.Prop, placement.Position, placement.Rotation, placement.Scale));
        var tile = zone.Chunks[1].Tiles[0];
        Assert.Equal(Zones.Sample().Chunks[1].Tiles[0].Heights.Samples, tile.Heights.Samples);
        Assert.Equal(0x66aa33u, tile.Colors![3, 5]);
        Assert.Null(tile.Underside);
        var spawn = Assert.Single(zone.Chunks[1].Spawns);
        Assert.Equal(("spawn.meadow.gate", SpawnFacing.Direction), (spawn.Id, spawn.Facing));
        Assert.Equal((PrimitiveKind.Cylinder, new Vector3(5, 4, 5), 90f, new Vector3(6, 2, 6), 2f, Rearm.StepOff), (spawn.Volume.Kind, spawn.Volume.Position, spawn.Volume.Yaw, spawn.Volume.Size, spawn.Volume.StandSeconds, spawn.Volume.Rearm));
        Assert.Equal((PrimitiveKind.Cylinder, new Vector3(6, 0.4f, 6)), (spawn.Volume.Pad!.Primitive!.Kind, spawn.Volume.Pad.Scale));
        var teleporter = Assert.Single(zone.Chunks[1].Teleporters);
        Assert.Equal(("teleporter.meadow.to_cave", SpawnFacing.Random, "teleporter.cave.to_meadow", 1f, null), (teleporter.Id, teleporter.Facing, teleporter.Target, teleporter.Volume.StandSeconds, teleporter.Volume.Pad));
        Assert.Equal(0xFFCC00u, teleporter.ChargeColor);
        var trigger = Assert.Single(zone.Chunks[1].Triggers);
        Assert.Equal(("trigger.meadow.plate", PrimitiveKind.Sphere, Rearm.Repeat, 0x33AAFFu), (trigger.Id, trigger.Volume.Kind, trigger.Volume.Rearm, trigger.ChargeColor));
        Assert.Equal(("gate", 5f), (trigger.Effects[0].Tag, trigger.Effects[0].RevertSeconds));
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
            prop = "prop.cube"
            tag = "gate"
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
    public void WritesReadableTriggers()
    {
        using var folder = new TempFolder();
        ZoneFolder.Write(folder.Root, Zones.Sample());

        var text = File.ReadAllText(folder.PathOf("chunks/0_0.toml")).ReplaceLineEndings("\n");
        Assert.Contains(
            """
            [[spawn]]
            id = "spawn.meadow.gate"
            volume = "cylinder"
            position = [5.0, 4.0, 5.0]
            yaw = 90.0
            size = [6.0, 2.0, 6.0]
            stand_seconds = 2.0
            facing = "direction"

            [spawn.pad]
            primitive = "cylinder"
            position = [5.0, 3.8, 5.0]
            scale = [6.0, 0.4, 6.0]

            [[teleporter]]
            id = "teleporter.meadow.to_cave"
            volume = "box"
            position = [12.0, 4.0, 5.0]
            size = [2.0, 2.0, 2.0]
            stand_seconds = 1.0
            facing = "random"
            target = "teleporter.cave.to_meadow"
            charge_color = 0xFFCC00

            [[trigger]]
            id = "trigger.meadow.plate"
            volume = "sphere"
            position = [14.0, 4.0, 9.0]
            size = [2.0, 2.0, 2.0]
            stand_seconds = 1.0
            rearm = "repeat"
            charge_color = 0x33AAFF

            [[trigger.effect]]
            toggle_tag = "gate"
            revert_seconds = 5.0
            """,
            text);
    }

    [Fact]
    public void PlacementsCanStartOff()
    {
        using var folder = new TempFolder();
        var zone = Zones.Sample();
        zone.Chunks[0].Placements[0].Off = true;

        var read = RoundTrip(zone, folder, out var errors);

        Assert.Empty(errors);
        Assert.True(read.Chunks[0].Placements[0].Off);
        Assert.Contains("off = true", File.ReadAllText(folder.PathOf("chunks/-1_0.toml")));
    }

    [Fact]
    public void PieceColorsAndUndersideColorsRoundTrip()
    {
        using var folder = new TempFolder();
        var zone = Zones.Sample();
        zone.Pieces[0].UnderColor = 0x7a5c3e;
        zone.Pieces[0].GroundColor = 0x739e4d;
        var tile = zone.Chunks[1].Tiles[0];
        var size = tile.Heights.SizeX * tile.Heights.SizeZ;
        tile.Underside = new Heightmap { SizeX = tile.Heights.SizeX, SizeZ = tile.Heights.SizeZ, Scale = 0.25f, Offset = -100, Samples = Enumerable.Repeat((ushort)360, size).ToArray() };
        tile.UndersideColors = new ColorMap { SizeX = tile.Heights.SizeX, SizeZ = tile.Heights.SizeZ, Colors = Enumerable.Repeat(0x336633u, size).ToArray() };

        var read = RoundTrip(zone, folder, out var errors);

        Assert.Empty(errors);
        Assert.Empty(ZoneValidation.Check(read));
        Assert.Equal(0x7a5c3eu, read.Pieces[0].UnderColor);
        Assert.Equal(0x739e4du, read.Pieces[0].GroundColor);
        Assert.Contains("under_color = 0x7A5C3E", File.ReadAllText(folder.PathOf("zone.toml")));
        Assert.Contains("ground_color = 0x739E4D", File.ReadAllText(folder.PathOf("zone.toml")));
        Assert.Equal(0x336633u, read.Chunks[1].Tiles[0].UndersideColors![3, 5]);
        Assert.True(File.Exists(folder.PathOf("chunks/0_0/main.under.color")));
    }

    [Fact]
    public void PlacementColorsRoundTripAsHex()
    {
        using var folder = new TempFolder();
        var zone = Zones.Sample();
        zone.Chunks[0].Placements[0].Color = 0xc04020;

        var read = RoundTrip(zone, folder, out var errors);

        Assert.Empty(errors);
        Assert.Equal(0xc04020u, read.Chunks[0].Placements[0].Color);
        Assert.Contains("color = 0xC04020", File.ReadAllText(folder.PathOf("chunks/-1_0.toml")));
    }

    [Fact]
    public void UndersideColorsNeedAnUnderside()
    {
        var zone = Zones.Sample();
        var tile = zone.Chunks[1].Tiles[0];
        tile.UndersideColors = new ColorMap { SizeX = tile.Heights.SizeX, SizeZ = tile.Heights.SizeZ, Colors = new uint[tile.Heights.SizeX * tile.Heights.SizeZ] };

        Assert.Contains(ZoneValidation.Check(zone), e => e.Message.Contains("underside colors but no underside"));
    }

    [Fact]
    public void PrimitivesRoundTrip()
    {
        using var folder = new TempFolder();
        var zone = Zones.Sample();
        var placement = zone.Chunks[0].Placements[0];
        placement.Prop = "";
        placement.Primitive = new Primitive { Kind = PrimitiveKind.Arch, Sides = 8, Thickness = 0.4f };
        zone.Chunks[0].Placements = [placement, new Placement { Position = placement.Position, Primitive = new Primitive { Kind = PrimitiveKind.Doorway, OpeningWidth = 1.2f, OpeningHeight = 2.2f } }];
        placement.Blockout = true;

        var read = RoundTrip(zone, folder, out var errors).Chunks[0].Placements[0];

        Assert.Empty(errors);
        Assert.Equal("", read.Prop);
        Assert.True(read.Blockout);
        Assert.True(placement.Primitive.SameAs(read.Primitive!));
        var text = File.ReadAllText(folder.PathOf("chunks/-1_0.toml"));
        Assert.Contains("primitive = \"arch\"", text);
        Assert.Contains("thickness = 0.4", text);
        Assert.Contains("opening = [1.2, 2.2]", text);
        Assert.Contains("blockout = true", text);
    }

    [Fact]
    public void APlacementHasAPropOrAPrimitive()
    {
        using var folder = new TempFolder();
        folder.Write("zone.toml", "id = \"zone.test\"\n");
        folder.Write("chunks/0_0.toml", "[[placement]]\nprop = \"prop.rock\"\nprimitive = \"box\"\nposition = [1.0, 0.0, 1.0]\n\n[[placement]]\nprop = \"prop.rock\"\nbevel = 0.1\nposition = [2.0, 0.0, 1.0]\n");

        var errors = new List<ZoneFileError>();
        ZoneFolder.Read(folder.Root, errors);

        Assert.Contains(errors, e => e.Message.Contains("places one of: a prop, a primitive or a group"));
        Assert.Contains(errors, e => e.Message.Contains("'bevel' is for primitives"));
    }

    [Fact]
    public void PlacementsKeepTheirTags()
    {
        using var folder = new TempFolder();
        var zone = Zones.Sample();
        zone.Chunks[0].Placements[0].Tag = "east.gate";

        var placement = RoundTrip(zone, folder, out var errors).Chunks[0].Placements[0];

        Assert.Empty(errors);
        Assert.Equal("east.gate", placement.Tag);
        Assert.Contains("tag = \"east.gate\"", File.ReadAllText(folder.PathOf("chunks/-1_0.toml")));
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
        zone.Chunks[1].Spawns[0].Volume.Size = new Vector3(4, 2, 4);
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
            prop = "prop.cube"
            positon = [1, 2, 3]

            [[spawn]]
            id = "spawn.meadow.gate"
            volume = "cylinder"
            position = [1, 2]
            size = [2, 2, 2]
            radius = 3.0
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
                "chunks/0_0.toml: spawn[0].radius: unknown key.",
                "chunks/0_0.toml: spawn[0].position: should be 3 numbers.",
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
