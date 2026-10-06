using Comet.Protocol.Framing;

namespace Comet.Protocol.Tests;

public class VarintTests
{
    [Theory]
    [InlineData(0u, 1)]
    [InlineData(127u, 1)]
    [InlineData(128u, 2)]
    [InlineData(16_383u, 2)]
    [InlineData(16_384u, 3)]
    [InlineData(uint.MaxValue, 5)]
    public void RoundTrips(uint value, int expectedSize)
    {
        var buffer = new byte[Varint.MaxLength];

        var written = Varint.Write(buffer, value);

        Assert.Equal(expectedSize, written);
        Assert.Equal(expectedSize, Varint.SizeOf(value));
        Assert.True(Varint.TryRead(buffer, out var read, out var bytesRead));
        Assert.Equal(value, read);
        Assert.Equal(expectedSize, bytesRead);
    }

    [Fact]
    public void RejectsTruncatedInput()
    {
        Assert.False(Varint.TryRead([0x80, 0x80], out _, out _));
    }

    [Fact]
    public void RejectsValuesOver32Bits()
    {
        Assert.False(Varint.TryRead([0xFF, 0xFF, 0xFF, 0xFF, 0x1F], out _, out _));
    }
}
