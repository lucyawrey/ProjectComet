using Comet.Content;

namespace ShapeLand.ContentBuild;

/// <summary>
/// A seeded test terrain: a floating disc of gentle hills in one 64 m chunk, dropping off into open sky at its edge.
/// The same for the same seed on every machine.
/// </summary>
public static class TestTerrain
{
    public const int Size = 33; // 64 m at 2 m spacing
    public const float Scale = 1f / 256; // about 4 mm steps
    public const float Offset = -64f; // heights from -64 m to 192 m
    public const float Radius = 28f; // the disc's edge, in metres from the chunk's centre
    public const float Spacing = 2f;

    public static Heightmap Generate(int seed)
    {
        var samples = new ushort[Size * Size];
        for (var z = 0; z < Size; z++)
        {
            for (var x = 0; x < Size; x++)
            {
                var fromCentre = MathF.Sqrt(MathF.Pow((x - (Size - 1) / 2f) * Spacing, 2) + MathF.Pow((z - (Size - 1) / 2f) * Spacing, 2));
                if (fromCentre > Radius)
                {
                    samples[z * Size + x] = Heightmap.Hole;
                    continue;
                }

                var height = 4f + 3f * Noise(seed, x / 12f, z / 12f) + 1f * Noise(seed + 1, x / 5f, z / 5f);
                samples[z * Size + x] = (ushort)Math.Clamp(MathF.Round((height - Offset) / Scale), 1, ushort.MaxValue);
            }
        }

        return new Heightmap { SizeX = Size, SizeZ = Size, Scale = Scale, Offset = Offset, Samples = samples };
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
