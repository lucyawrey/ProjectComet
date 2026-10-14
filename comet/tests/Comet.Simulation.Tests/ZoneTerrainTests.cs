using Comet.Content;

namespace Comet.Simulation.Tests;

public class ZoneTerrainTests
{
    private static Heightmap Map(int sizeX, int sizeZ, Func<int, int, float?> height)
    {
        var map = new Heightmap { SizeX = sizeX, SizeZ = sizeZ, Scale = 0.01f, Offset = -50, Samples = new ushort[sizeX * sizeZ] };
        for (var z = 0; z < sizeZ; z++)
        {
            for (var x = 0; x < sizeX; x++)
            {
                map.Samples[z * sizeX + x] = height(x, z) is { } h ? (ushort)MathF.Round((h + 50) / 0.01f) : Heightmap.Hole;
            }
        }

        return map;
    }

    [Fact]
    public void HeightsFollowTheMeshesTriangles()
    {
        // One cell with corners 0, 1 (x + 1), 2 (z + 1) and 4 (both): the diagonal runs from (x+1, z) to (x, z+1).
        var zone = new Zone
        {
            Pieces = [new TerrainPiece { Name = "main" }],
            Chunks = [new ZoneChunk { Tiles = [new TerrainTile { Piece = "main", Heights = Map(2, 2, (x, z) => x + 2 * z + (x == 1 && z == 1 ? 1 : 0)) }] }],
        };
        var piece = new ZoneTerrain(zone)["main"];

        Assert.True(piece.TryGetHeightAt(0.25f, 0.25f, out var low));
        Assert.Equal(0.25f + 0.5f, low, 3);
        Assert.True(piece.TryGetHeightAt(0.75f, 0.75f, out var high));
        Assert.Equal(4 - 0.25f * 2 - 0.25f * 3, high, 3);
        Assert.False(piece.TryGetHeightAt(1.5f, 0.5f, out _));
    }

    [Fact]
    public void APieceIsOneGridAcrossChunks()
    {
        // Two tiles meeting at x = 64: chunk 0's last column is chunk 1's first.
        var zone = new Zone
        {
            Pieces = [new TerrainPiece { Name = "main", BottomHeight = -3 }],
            Chunks =
            [
                new ZoneChunk { Coord = new ChunkCoord(0, 0), Tiles = [new TerrainTile { Piece = "main", OriginX = 62, Heights = Map(3, 2, (x, _) => x) }] },
                new ZoneChunk { Coord = new ChunkCoord(1, 0), Tiles = [new TerrainTile { Piece = "main", Heights = Map(3, 2, (x, _) => 2 + x) }] },
            ],
        };
        var piece = new ZoneTerrain(zone)["main"];

        Assert.Equal((62, 0, 66, 1), piece.Extent);
        Assert.True(piece.TryGetHeightAt(63.5f, 0.2f, out var west));
        Assert.Equal(1.5f, west, 3);
        Assert.True(piece.TryGetHeightAt(64.5f, 0.2f, out var east));
        Assert.Equal(2.5f, east, 3);
        Assert.Equal(-3, piece.BottomAt(64.5f, 0.2f));
    }

    [Fact]
    public void BottomsFollowTheUndersideWhereThereIsOne()
    {
        var zone = new Zone
        {
            Pieces = [new TerrainPiece { Name = "island", BottomHeight = -3 }],
            Chunks = [new ZoneChunk { Tiles = [new TerrainTile { Piece = "island", Heights = Map(2, 2, (_, _) => 5), Underside = Map(2, 2, (_, _) => 1) }] }],
        };

        Assert.Equal(1, new ZoneTerrain(zone)["island"].BottomAt(0.5f, 0.5f), 3);
    }
}
