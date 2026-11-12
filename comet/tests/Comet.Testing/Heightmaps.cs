using Comet.Content;

namespace Comet.Testing;

public static class Heightmaps
{
    public static Heightmap Flat(int sizeX, int sizeZ, float height)
    {
        var map = new Heightmap { SizeX = sizeX, SizeZ = sizeZ };
        map.Samples = Enumerable.Repeat(map.ToSample(height), sizeX * sizeZ).ToArray();
        return map;
    }

    public static Heightmap Flat(int size, float height) => Flat(size, size, height);
}
