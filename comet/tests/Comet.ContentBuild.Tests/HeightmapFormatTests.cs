using Comet.Content;

namespace Comet.ContentBuild.Tests;

public class HeightmapFormatTests
{
    private static Heightmap Sample() => new() { SizeX = 3, SizeZ = 2, Scale = 0.5f, Offset = -10f, Samples = [0, 1, 2, 3, 4, 65535] };

    [Fact]
    public void RoundTripsWithAn18ByteHeader()
    {
        var stream = new MemoryStream();
        HeightmapFormat.Write(stream, Sample());

        Assert.Equal(HeightmapFormat.HeaderSize + 6 * 2, stream.Length);
        Assert.Equal("CHGT"u8.ToArray(), stream.ToArray()[..4]);
        stream.Position = 0;
        var map = HeightmapFormat.Read(stream);
        Assert.Equal((3, 2, 0.5f, -10f), (map.SizeX, map.SizeZ, map.Scale, map.Offset));
        Assert.Equal(Sample().Samples, map.Samples);
    }

    [Fact]
    public void HeightAtUsesScaleAndOffset()
    {
        var map = Sample();

        Assert.Equal(-10f, map.HeightAt(0, 0));
        Assert.Equal(-8f, map.HeightAt(1, 1));
        Assert.Equal(-10f + 65535 * 0.5f, map.HeightAt(2, 1));
    }

    [Fact]
    public void RejectsOtherFiles()
    {
        var error = Assert.Throws<ContentException>(() => HeightmapFormat.Read(new MemoryStream("id = 1\n"u8.ToArray())));
        Assert.Contains("Not a heightmap", error.Message);
    }

    [Fact]
    public void RejectsTruncatedAndOversizedFiles()
    {
        var stream = new MemoryStream();
        HeightmapFormat.Write(stream, Sample());
        var bytes = stream.ToArray();

        Assert.Contains("truncated", Assert.Throws<ContentException>(() => HeightmapFormat.Read(new MemoryStream(bytes[..^1]))).Message);
        Assert.Contains("bytes after", Assert.Throws<ContentException>(() => HeightmapFormat.Read(new MemoryStream([.. bytes, 0]))).Message);
    }
}
