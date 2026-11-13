using System.Numerics;
using Comet.Content;

namespace Comet.Simulation.Tests;

internal static class TestWorlds
{
    public static readonly MovementRules Rules = new();

    public static CollisionWorld Create(params CollisionMesh[] more) =>
        new(new ZoneTerrain(Zone()), [Block(4, 4, 1, new Vector3(2, 2, 2)), Block(4, 10, 1, new Vector3(2, 0.3f, 2)), .. more], killHeight: -20);

    public static Zone Zone(params (string Piece, float Height, float Bottom, int FromX, int FromZ, int Size)[] more)
    {
        var map = Heightmaps.Flat(17, 1);
        for (var z = 0; z < 17; z++)
        {
            map.Samples[z * 17 + 15] = map.ToSample(1.5f);
            map.Samples[z * 17 + 16] = map.ToSample(2);
        }

        map.Samples[12 * 17 + 12] = Heightmap.Hole;
        var tiles = new List<TerrainTile>
        {
            new() { Piece = "main", Heights = map },
        };
        var pieces = new List<TerrainPiece>
        {
            new()
            {
                Name = "main",
                SkirtDepth = 6,
                RimSkirts = false,
            },
        };
        foreach (var (piece, height, bottom, fromX, fromZ, size) in more)
        {
            pieces.Add(new TerrainPiece { Name = piece, SkirtDepth = height - bottom });
            tiles.Add(
                new TerrainTile
                {
                    Piece = piece,
                    OriginX = fromX,
                    OriginZ = fromZ,
                    Heights = Heightmaps.Flat(size, height),
                }
            );
        }

        return new Zone
        {
            Id = "zone.test",
            Pieces = pieces.ToArray(),
            Chunks = [new ZoneChunk { Coord = new ChunkCoord(0, 0), Tiles = tiles.ToArray() }],
        };
    }

    public static CollisionMesh Block(float x, float z, float baseY, Vector3 size) =>
        CollisionMesh.Box(new Vector3(x - size.X / 2, baseY, z - size.Z / 2), new Vector3(x + size.X / 2, baseY + size.Y, z + size.Z / 2));
}
