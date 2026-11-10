using Comet.Content;

namespace Comet.ContentBuild.Tests;

public class HeightmapFormatTests
{
    private static Heightmap Sample() =>
        new()
        {
            SizeX = 3,
            SizeZ = 2,
            Samples = [0, 1, 2, 3, 4, 65535],
        };

    [Fact]
    public void RoundTripsWithA10ByteHeader()
    {
        var stream = new MemoryStream();
        HeightmapFormat.Write(stream, Sample());

        Assert.Equal(HeightmapFormat.HeaderSize + 6 * 2, stream.Length);
        Assert.Equal("CHGT"u8.ToArray(), stream.ToArray()[..4]);
        stream.Position = 0;
        var map = HeightmapFormat.Read(stream);
        Assert.Equal((3, 2), (map.SizeX, map.SizeZ));
        Assert.Equal(Sample().Samples, map.Samples);
    }

    [Fact]
    public void HeightsAreCentimetersAboveTheOffset()
    {
        var map = Sample();
        map.Offset = -10;

        Assert.True(map.TryGetHeight(1, 0, out var low));
        Assert.Equal(-9.99f, low, 3);
        Assert.True(map.TryGetHeight(1, 1, out var mid));
        Assert.Equal(-9.96f, mid, 3);
        Assert.True(map.TryGetHeight(2, 1, out var high));
        Assert.Equal(645.35f, high, 3);
        Assert.Equal((ushort)1000, map.ToSample(0));
        Assert.Equal(ushort.MaxValue, map.ToSample(1000));
    }

    [Fact]
    public void ZeroSamplesAreHoles()
    {
        var map = Sample();

        Assert.True(map.IsHole(0, 0));
        Assert.False(map.TryGetHeight(0, 0, out _));
        Assert.False(map.IsHole(1, 0));
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

    private static byte[] WithHeader(Action<byte[]> change)
    {
        var stream = new MemoryStream();
        HeightmapFormat.Write(stream, Sample());
        var bytes = stream.ToArray();
        change(bytes);
        return bytes;
    }

    [Fact]
    public void AHugeClaimedSizeIsTruncatedNotAnOverflow()
    {
        var bytes = WithHeader(b =>
        {
            BitConverter.TryWriteBytes(b.AsSpan(6), ushort.MaxValue);
            BitConverter.TryWriteBytes(b.AsSpan(8), ushort.MaxValue);
        });

        Assert.Contains("truncated", Assert.Throws<ContentException>(() => HeightmapFormat.Read(new MemoryStream(bytes))).Message);
    }

    [Fact]
    public void OldVersionsAreRejected()
    {
        var bytes = WithHeader(b => BitConverter.TryWriteBytes(b.AsSpan(4), (ushort)1));

        Assert.Contains(
            "Unsupported heightmap version 1",
            Assert.Throws<ContentException>(() => HeightmapFormat.Read(new MemoryStream(bytes))).Message
        );
    }
}
