using System.Numerics;
using Comet.Content;

namespace Comet.ZoneFiles.Tests;

public class ZoneValidationTests
{
    private static IEnumerable<string> Errors(Zone zone) => ZoneValidation.Check(zone).Select(e => e.ToString());

    [Fact]
    public void AcceptsTheSampleZone() => Assert.Empty(Errors(Zones.Sample()));

    [Fact]
    public void ChecksKeysAndNames()
    {
        var zone = Zones.Sample();
        zone.Id = "meadow";
        zone.Pieces = [zone.Pieces[0], new TerrainPiece { Name = "main" }, new TerrainPiece { Name = "Sky Island" }];
        zone.Chunks[0].Placements[0].Asset = "cube";

        Assert.Equal(
            [
                "zone.toml: id: 'meadow' isn't a valid zone key: use 'zone.' then lowercase letters, digits and underscores.",
                "zone.toml: piece: 'main' is used twice.",
                "zone.toml: piece: 'Sky Island' isn't a valid name: use lowercase letters, digits and underscores, starting with a letter.",
                "chunks/-1_0.toml: placement of cube at [-10.5, 4, 3.25]: 'cube' isn't an asset key.",
            ],
            Errors(zone));
    }

    [Fact]
    public void SkirtDepthCantBeNegative()
    {
        var zone = Zones.Sample();
        zone.Pieces[0].SkirtDepth = -1;

        Assert.Equal(["zone.toml: piece main: skirt_depth can't be negative."], Errors(zone));
    }

    [Fact]
    public void TilesNeedAKnownPieceAndMustFitTheirChunk()
    {
        var zone = Zones.Sample();
        zone.Chunks[0].Tiles = [zone.Chunks[0].Tiles[0], new TerrainTile { Piece = "cliff", Heights = Zones.Flat(2, 2, 0) }];
        zone.Chunks[1].Tiles[0].OriginX = 50;

        Assert.Equal(
            [
                "chunks/-1_0.toml: tile: there's no piece 'cliff' in zone.toml.",
                "chunks/0_0.toml: tile main: 17×65 samples from [50, 0] reach past the chunk's 65×65.",
            ],
            Errors(zone));
    }

    [Fact]
    public void TerrainMustMatchWhereAPiecesTilesMeet()
    {
        var zone = Zones.Sample();
        zone.Chunks[1].Tiles[0].Heights = Zones.Flat(17, 65, 4.5f);

        Assert.Equal(
            ["chunks/-1_0.toml: tile main: the terrain doesn't match chunk 0_0's where they meet (world sample [0, 0])."],
            Errors(zone));
    }

    [Fact]
    public void HolesMustMatchToo()
    {
        var zone = Zones.Sample();
        zone.Chunks[1].Tiles[0].Heights.Samples[65 / 2 * 17] = Heightmap.Hole;

        Assert.Equal(
            ["chunks/-1_0.toml: tile main: the terrain doesn't match chunk 0_0's where they meet (world sample [0, 32])."],
            Errors(zone));
    }

    [Fact]
    public void ChunksMeetingAtACornerShareOneSample()
    {
        var zone = Zones.Sample();
        zone.Chunks =
        [
            new ZoneChunk { Coord = new ChunkCoord(0, 0), Tiles = [new TerrainTile { Piece = "main", OriginX = 60, OriginZ = 60, Heights = Zones.Flat(5, 5, 2) }] },
            new ZoneChunk { Coord = new ChunkCoord(1, 1), Tiles = [new TerrainTile { Piece = "main", Heights = Zones.Flat(5, 5, 2) }] },
        ];
        zone.Borders = [];
        Assert.Empty(Errors(zone));

        zone.Chunks[1].Tiles[0].Heights.Samples[0] = Heightmap.Hole;
        Assert.Equal(
            ["chunks/0_0.toml: tile main: the terrain doesn't match chunk 1_1's where they meet (world sample [64, 64])."],
            Errors(zone));
    }

    [Fact]
    public void ObjectsBelongToTheChunkHoldingTheirPivot()
    {
        var zone = Zones.Sample();
        zone.Chunks[0].Placements[0].Position = new Vector3(0, 4, 3);
        zone.Chunks[1].Volumes[0].Position = new Vector3(64, 0, 1);

        Assert.Equal(
            [
                "chunks/-1_0.toml: placement of asset.cube at [0, 4, 3] is in chunk 0_0: objects belong to the chunk holding their pivot.",
                "chunks/0_0.toml: wall volume at [64, 0, 1] is in chunk 1_0: objects belong to the chunk holding their pivot.",
            ],
            Errors(zone));
    }

    [Fact]
    public void PrimitiveValuesFitTheirKind()
    {
        var zone = Zones.Sample();
        var placements = zone.Chunks[0].Placements;
        placements[0].Primitive = new Primitive { Kind = PrimitiveKind.Box, Sides = 8 };
        zone.Chunks[0].Placements = placements.Append(new Placement { Position = placements[0].Position, Primitive = new Primitive { Kind = PrimitiveKind.Cylinder, Sides = 2 } })
            .Append(new Placement { Position = placements[0].Position, Primitive = new Primitive { Kind = PrimitiveKind.Stairs, Steps = -1 } })
            .Append(new Placement { Position = placements[0].Position, Primitive = new Primitive { Kind = PrimitiveKind.Doorway, OpeningWidth = 1, OpeningHeight = 2 } })
            .Append(new Placement { Position = placements[0].Position, Primitive = new Primitive { Kind = PrimitiveKind.Wall, OpeningWidth = 1, OpeningHeight = 2 } })
            .Append(new Placement { Position = placements[0].Position, Primitive = new Primitive { Kind = PrimitiveKind.Box, Thickness = 0.5f } })
            .ToArray();

        var errors = Errors(zone).ToList();

        Assert.Contains(errors, e => e.Contains("a box") && e.Contains("only cylinders, cones, spheres and arches have sides"));
        Assert.Contains(errors, e => e.Contains("a cylinder") && e.Contains("sides must be from 3"));
        Assert.Contains(errors, e => e.Contains("a stairs") && e.Contains("only stairs have steps"));
        Assert.Contains(errors, e => e.Contains("a wall") && e.Contains("only doorways have an opening"));
        Assert.Contains(errors, e => e.Contains("a box") && e.Contains("only arches have a thickness"));
        Assert.DoesNotContain(errors, e => e.Contains("a doorway"));
    }

    [Fact]
    public void PrimitivesReachAsFarAsTheirSize()
    {
        var zone = Zones.Sample();
        var placement = zone.Chunks[0].Placements[0];
        placement.Primitive = new Primitive { Kind = PrimitiveKind.Box };
        placement.Scale = new Vector3(2, 2, 2);
        Assert.Empty(ZoneValidation.CheckPlacements(zone, _ => null));

        placement.Scale = new Vector3(ZoneGrid.ChunkSize * 4, 2, 2);
        Assert.Contains(ZoneValidation.CheckPlacements(zone, _ => null), e => e.Message.Contains("reaches past the chunks around its own"));
    }

    [Fact]
    public void PlacementNamesAreUniqueAndValid()
    {
        var zone = Zones.Sample();
        zone.Chunks[0].Placements =
        [
            new Placement { Asset = "asset.cube", Name = "gate", Position = new Vector3(-10, 0, 1) },
            new Placement { Asset = "asset.cube", Name = "gate", Position = new Vector3(-12, 0, 1) },
            new Placement { Asset = "asset.cube", Name = "Gate!", Position = new Vector3(-14, 0, 1) },
        ];

        Assert.Equal(
            [
                "chunks/-1_0.toml: placement of asset.cube: the name 'gate' is already used in chunks/-1_0.toml.",
                "chunks/-1_0.toml: placement of asset.cube: 'Gate!' isn't a valid name: use lowercase letters, digits, underscores and dots, starting with a letter.",
            ],
            Errors(zone));
    }

    [Fact]
    public void AZoneWithSpawnsHasOneDefault()
    {
        var zone = Zones.Sample();
        zone.Chunks[0].Spawns = [new SpawnPoint { Id = "spawn.meadow_gate", Position = new Vector3(-5, 4, 5) }];
        zone.Chunks[1].Spawns[0].IsDefault = false;

        Assert.Equal(
            [
                "chunks/0_0.toml: spawn: 'spawn.meadow_gate' is already used in chunks/-1_0.toml.",
                "zone.toml: No default spawn: mark one of the zone's spawns with default = true.",
            ],
            Errors(zone));
    }

    [Fact]
    public void ChecksBordersEntrancesAndRooms()
    {
        var zone = Zones.Sample();
        zone.Borders[0].Chunk = new ChunkCoord(5, 5);
        zone.Borders[0].Zone = "zone.meadow";
        zone.TransitionRooms[0].Entrance = "door";
        zone.Entrances[0].Size = new Vector3(4, 0, 2);

        Assert.Equal(
            [
                "zone.toml: border: chunk 5_5 isn't one of the zone's chunks.",
                "zone.toml: border: 'zone.meadow' isn't another zone's key.",
                "zone.toml: entrance cave: size must be above 0 on every axis.",
                "zone.toml: transition_room hall: there's no entrance 'door'.",
            ],
            Errors(zone));
    }
}
