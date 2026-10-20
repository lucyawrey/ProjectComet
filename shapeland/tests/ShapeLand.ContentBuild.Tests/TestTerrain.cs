using System.Numerics;
using Comet.Content;
using Comet.Simulation;

namespace ShapeLand.ContentBuild.Tests;

/// <summary>
/// A seeded zone for tests (ShapeLand's own zone is made by hand in the editor): a floating disc of gentle hills
/// centered on the origin across four chunks, dropping off into open sky at its edge, with a few cube blocks to jump
/// on. The same for the same seed on every machine.
/// </summary>
public static class TestTerrain
{
    public const float Scale = 1f / 256; // about 4 mm steps
    public const float Offset = -64f; // heights from -64 m to 192 m
    public const float Radius = 28f; // the disc's edge, in meters from the chunk's center

    /// <summary>The test zone's key.</summary>
    public const string ZoneId = "zone.test";

    /// <summary>The island: one piece, cut into the four chunks around the origin, with blocks and two spawn points, the default at the center.</summary>
    public static Zone GenerateZone(int seed)
    {
        // The island's samples run from -RadiusSamples to RadiusSamples on each axis (1 m apart, centered on the
        // origin). Each chunk's tile covers its part, sharing the samples on X = 0 and Z = 0 with its neighbors.
        var radiusSamples = (int)(Radius / ZoneGrid.SampleSpacing);
        var chunks = new List<ZoneChunk>();
        foreach (var (cx, cz) in new[] { (-1, -1), (0, -1), (-1, 0), (0, 0) })
        {
            int fromX = cx < 0 ? -radiusSamples : 0, toX = cx < 0 ? 0 : radiusSamples;
            int fromZ = cz < 0 ? -radiusSamples : 0, toZ = cz < 0 ? 0 : radiusSamples;
            int sizeX = toX - fromX + 1, sizeZ = toZ - fromZ + 1;
            var samples = new ushort[sizeX * sizeZ];
            for (var z = 0; z < sizeZ; z++)
            {
                for (var x = 0; x < sizeX; x++)
                {
                    samples[z * sizeX + x] = SampleAt((fromX + x) * ZoneGrid.SampleSpacing, (fromZ + z) * ZoneGrid.SampleSpacing, seed);
                }
            }

            chunks.Add(new ZoneChunk
            {
                Coord = new ChunkCoord(cx, cz),
                Tiles =
                [
                    new TerrainTile
                    {
                        Piece = "island",
                        OriginX = fromX - cx * ZoneGrid.ChunkSize,
                        OriginZ = fromZ - cz * ZoneGrid.ChunkSize,
                        Heights = new Heightmap { SizeX = sizeX, SizeZ = sizeZ, Scale = Scale, Offset = Offset, Samples = samples },
                    },
                ],
            });
        }

        var zone = new Zone
        {
            Id = ZoneId,
            Pieces = [new TerrainPiece { Name = "island", SkirtDepth = 4 }],
            Chunks = chunks.ToArray(),
        };

        // A low step, a block to jump onto, a taller one to jump up to from the first, and a long platform.
        var island = new ZoneTerrain(zone)["island"];
        foreach (var (x, z, size) in new[] { (6f, 2f, new Vector3(2, 0.3f, 2)), (10f, -4f, new Vector3(3, 1f, 3)), (13f, -7f, new Vector3(2, 2f, 2)), (-8f, 8f, new Vector3(6, 0.8f, 2)) })
        {
            // Sunk 0.5 m so no gap shows on slopes; a box stands on its pivot, its scale its size in meters.
            const float sink = 0.5f;
            island.TryGetHeightAt(x, z, out var ground);
            var chunk = zone.Chunks.Single(c => c.Coord.Equals(ChunkCoord.Containing(new Vector3(x, 0, z))));
            chunk.Placements = [.. chunk.Placements, new Placement
            {
                Primitive = new Primitive { Kind = PrimitiveKind.Box },
                Position = new Vector3(x, MathF.Round(ground - sink, 3), z),
                Scale = new Vector3(size.X, size.Y + sink, size.Z),
            }];
        }

        // Spawn points, each with its marker: a flat cylinder primitive (its pivot at its base) named with its key, sunk halfway into the ground, with the spawn point on its top.
        foreach (var (id, x, z, isDefault) in new[] { ("spawn.test_center", 0f, 0f, true), ("spawn.test_east", 18f, 8f, false) })
        {
            const float radius = 2, markerHeight = 0.4f;
            island.TryGetHeightAt(x, z, out var ground);
            ground = MathF.Round(ground, 3);
            var chunk = zone.Chunks.Single(c => c.Coord.Equals(ChunkCoord.Containing(new Vector3(x, 0, z))));
            chunk.Spawns = [.. chunk.Spawns, new SpawnPoint { Id = id, Position = new Vector3(x, ground + markerHeight / 2, z), Radius = radius, Facing = SpawnFacing.Random, IsDefault = isDefault }];
            chunk.Placements = [.. chunk.Placements, new Placement
            {
                Primitive = new Primitive { Kind = PrimitiveKind.Cylinder },
                Name = id,
                Position = new Vector3(x, ground - markerHeight / 2, z),
                Scale = new Vector3(2 * radius, markerHeight, 2 * radius),
            }];
        }

        return zone;
    }

    // The island's sample at world (x, z) in meters, with the same hills as the old map (whose noise ran over
    // samples 2 m apart, so world meters / 24 matches its x / 12).
    private static ushort SampleAt(float x, float z, int seed)
    {
        if (MathF.Sqrt(x * x + z * z) > Radius)
        {
            return Heightmap.Hole;
        }

        // The old map's sample 0 was at -32 m.
        float nx = (x + 32) / 24f, nz = (z + 32) / 24f;
        var height = 4f + 3f * Noise(seed, nx, nz) + 1f * Noise(seed + 1, (x + 32) / 10f, (z + 32) / 10f);
        return (ushort)Math.Clamp(MathF.Round((height - Offset) / Scale), 1, ushort.MaxValue);
    }

    // Smooth value noise in -1..1 from a hash of lattice points (not System.Random, whose sequence may change).
    private static float Noise(int seed, float x, float z)
    {
        int x0 = (int)MathF.Floor(x), z0 = (int)MathF.Floor(z);
        float tx = Smooth(x - x0), tz = Smooth(z - z0);
        var top = Lerp(Lattice(seed, x0, z0), Lattice(seed, x0 + 1, z0), tx);
        var bottom = Lerp(Lattice(seed, x0, z0 + 1), Lattice(seed, x0 + 1, z0 + 1), tx);
        return Lerp(top, bottom, tz);
    }

    private static float Lattice(int seed, int x, int z)
    {
        var h = (uint)seed * 0x9E3779B9u ^ (uint)x * 0x85EBCA6Bu ^ (uint)z * 0xC2B2AE35u;
        h ^= h >> 16;
        h *= 0x7FEB352Du;
        h ^= h >> 15;
        h *= 0x846CA68Bu;
        h ^= h >> 16;
        return h / (float)uint.MaxValue * 2f - 1f;
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
