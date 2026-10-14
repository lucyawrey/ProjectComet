using System.Numerics;
using Comet.Content;

namespace Comet.Simulation.Tests;

internal static class TestWorlds
{
    public static readonly MovementRules Rules = new();

    /// <summary>
    /// One terrain piece of 17×17 samples (16 m) with sample (0, 0) at the origin: flat at 1 m, except a hole at
    /// sample (12, 12) and a rise to 2 m from x = 14 to x = 16. Plus a 2 m cube standing at (4, 4) and a 0.3 m step at (4, 10).
    /// </summary>
    public static CollisionWorld Create(params CollisionMesh[] more) =>
        new(new ZoneTerrain(Zone()), [Block(4, 4, 1, new Vector3(2, 2, 2)), Block(4, 10, 1, new Vector3(2, 0.3f, 2)), .. more], killHeight: -20);

    /// <summary>The terrain, plus <paramref name="more"/> square flat pieces, each with its height, bottom, first sample and size.</summary>
    public static Zone Zone(params (string Piece, float Height, float Bottom, int FromX, int FromZ, int Size)[] more)
    {
        var map = Flat(17, 1);
        for (var z = 0; z < 17; z++)
        {
            map.Samples[z * 17 + 15] = Sample(1.5f);
            map.Samples[z * 17 + 16] = Sample(2);
        }

        map.Samples[12 * 17 + 12] = Heightmap.Hole;
        var tiles = new List<TerrainTile> { new() { Piece = "main", Heights = map } };
        var pieces = new List<TerrainPiece> { new() { Name = "main", BottomHeight = -5, RimSkirts = false } };
        foreach (var (piece, height, bottom, fromX, fromZ, size) in more)
        {
            pieces.Add(new TerrainPiece { Name = piece, BottomHeight = bottom });
            tiles.Add(new TerrainTile { Piece = piece, OriginX = fromX, OriginZ = fromZ, Heights = Flat(size, height) });
        }

        return new Zone
        {
            Id = "zone.test",
            Pieces = pieces.ToArray(),
            Chunks = [new ZoneChunk { Coord = new ChunkCoord(0, 0), Tiles = tiles.ToArray() }],
        };
    }

    /// <summary>A box standing on <paramref name="baseY"/>, centered on (x, z).</summary>
    public static CollisionMesh Block(float x, float z, float baseY, Vector3 size) =>
        CollisionMesh.Box(new Vector3(x - size.X / 2, baseY, z - size.Z / 2), new Vector3(x + size.X / 2, baseY + size.Y, z + size.Z / 2));

    private static Heightmap Flat(int size, float height) => new()
    {
        SizeX = size,
        SizeZ = size,
        Scale = 0.01f,
        Offset = -50,
        Samples = Enumerable.Repeat(Sample(height), size * size).ToArray(),
    };

    private static ushort Sample(float height) => (ushort)MathF.Round((height + 50) / 0.01f);
}
