using Comet.Content;

namespace Comet.ContentBuild.Tests;

public class ColorMapFormatTests
{
    private static ColorMap Sample() => new() { SizeX = 2, SizeZ = 2, Colors = [0x000000, 0xff0000, 0x00ff80, 0x123456] };

    [Fact]
    public void RoundTripsWithA10ByteHeader()
    {
        var stream = new MemoryStream();
        ColorMapFormat.Write(stream, Sample());

        Assert.Equal(ColorMapFormat.HeaderSize + 4 * 3, stream.Length);
        Assert.Equal("CCOL"u8.ToArray(), stream.ToArray()[..4]);
        stream.Position = 0;
        var map = ColorMapFormat.Read(stream);
        Assert.Equal((2, 2), (map.SizeX, map.SizeZ));
        Assert.Equal(Sample().Colors, map.Colors);
        Assert.Equal(0x123456u, map[1, 1]);
    }

    [Fact]
    public void RejectsOtherFilesAndTruncatedOnes()
    {
        var stream = new MemoryStream();
        ColorMapFormat.Write(stream, Sample());
        var bytes = stream.ToArray();

        Assert.Contains("magic", Assert.Throws<ContentException>(() => ColorMapFormat.Read(new MemoryStream("CHGT"u8.ToArray()))).Message);
        Assert.Contains("truncated", Assert.Throws<ContentException>(() => ColorMapFormat.Read(new MemoryStream(bytes[..^1]))).Message);
        Assert.Contains("after", Assert.Throws<ContentException>(() => ColorMapFormat.Read(new MemoryStream([.. bytes, 0]))).Message);
    }
}
