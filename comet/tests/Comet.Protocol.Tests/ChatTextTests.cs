namespace Comet.Protocol.Tests;

public class ChatTextTests
{
    private const int MaxLength = 200;

    [Theory]
    [InlineData("a\nb", "ab")]
    [InlineData("a\u202Eb\u200Bc", "abc")] // a right-to-left override and a zero-width space
    [InlineData("a\u2028b\uE000c", "abc")] // a line separator and a private-use character
    [InlineData("hi \uD83D\uDE00 <b>x</b>", "hi \uD83D\uDE00 <b>x</b>")] // an emoji and markup are text
    [InlineData("a\U000E0001\U000E0041b", "ab")] // tag characters, outside the Basic Multilingual Plane
    [InlineData("a\U000F0000\U0001D173b", "ab")] // plane 15 private use and a musical formatting character
    [InlineData("\U00020000", "\U00020000")] // a CJK letter outside the Basic Multilingual Plane is text
    [InlineData("e\u0301\u0302\u0303\u0304\u0305 1\uFE0F\u20E3", "e\u0301\u0302\u0303 1\uFE0F\u20E3")] // stacked accents are capped; a keycap emoji is whole
    public void DropsCharactersThatArentText(string sent, string shown)
    {
        Assert.True(ChatText.TryNormalize(sent, MaxLength, out var text));
        Assert.Equal(shown, text);
    }

    [Fact]
    public void DropsHalfASurrogatePair()
    {
        // Not in InlineData: a lone surrogate doesn't survive the test runner's serialization.
        Assert.True(ChatText.TryNormalize("a\uD800b", MaxLength, out var text));
        Assert.Equal("ab", text);
    }

    [Fact]
    public void ALineOfOnlyControlCharactersIsEmpty() => Assert.False(ChatText.TryNormalize("\n\u200B\t", MaxLength, out _));

    [Fact]
    public void ALineOfOnlyTagCharactersIsEmpty() => Assert.False(ChatText.TryNormalize("\U000E0001\U000E0041\U000E007F", MaxLength, out _));
}
