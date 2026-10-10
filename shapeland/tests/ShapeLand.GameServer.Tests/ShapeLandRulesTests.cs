using ShapeLand.GameServer;
using ShapeLand.Shared.World;

namespace ShapeLand.GameServer.Tests;

public class ShapeLandRulesTests
{
    [Theory]
    [InlineData("  Ada  ", "Ada")]
    [InlineData("Bot 01", "Bot 01")]
    [InlineData("x_y-z", "x_y-z")]
    public void AcceptsAndTrimsNames(string name, string expected)
    {
        Assert.True(ShapeLandRules.TryNormaliseName(name, out var normalised));
        Assert.Equal(expected, normalised);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("two  spaces")]
    [InlineData("seventeen chars!!")]
    [InlineData("ThisNameIsTooLong")]
    [InlineData("emoji 🙂")]
    public void RejectsBadNames(string name) => Assert.False(ShapeLandRules.TryNormaliseName(name, out _));

    [Fact]
    public void ChatIsTrimmedAndLimited()
    {
        Assert.True(ShapeLandRules.TryNormaliseChat("  hi  ", out var text));
        Assert.Equal("hi", text);
        Assert.False(ShapeLandRules.TryNormaliseChat("   ", out _));
        Assert.True(ShapeLandRules.TryNormaliseChat(new string('a', 200), out _));
        Assert.False(ShapeLandRules.TryNormaliseChat(new string('a', 201), out _));
    }

    [Theory]
    [InlineData("a\nb", "ab")]
    [InlineData("a\u202Eb\u200Bc", "abc")] // a right-to-left override and a zero-width space
    [InlineData("a\u2028b\uE000c", "abc")] // a line separator and a private-use character
    [InlineData("hi \uD83D\uDE00 <b>x</b>", "hi \uD83D\uDE00 <b>x</b>")] // an emoji and markup are text
    [InlineData("a\U000E0001\U000E0041b", "ab")] // tag characters, outside the Basic Multilingual Plane
    [InlineData("a\U000F0000\U0001D173b", "ab")] // plane 15 private use and a musical formatting character
    [InlineData("\U00020000", "\U00020000")] // a CJK letter outside the Basic Multilingual Plane is text
    [InlineData("e\u0301\u0302\u0303\u0304\u0305 1\uFE0F\u20E3", "e\u0301\u0302\u0303 1\uFE0F\u20E3")] // stacked accents are capped; a keycap emoji is whole
    public void ChatDropsCharactersThatArentText(string sent, string shown)
    {
        Assert.True(ShapeLandRules.TryNormaliseChat(sent, out var text));
        Assert.Equal(shown, text);
    }

    [Fact]
    public void ChatDropsHalfASurrogatePair()
    {
        // Not in InlineData: a lone surrogate doesn't survive the test runner's serialisation.
        Assert.True(ShapeLandRules.TryNormaliseChat("a\uD800b", out var text));
        Assert.Equal("ab", text);
    }

    [Fact]
    public void ChatOfOnlyControlCharactersIsEmpty() => Assert.False(ShapeLandRules.TryNormaliseChat("\n\u200B\t", out _));

    [Fact]
    public void ChatOfOnlyTagCharactersIsEmpty() => Assert.False(ShapeLandRules.TryNormaliseChat("\U000E0001\U000E0041\U000E007F", out _));

    [Fact]
    public void ChatAllowsABurstThenOneEveryTwoSeconds()
    {
        var limiter = new ChatLimiter(tickRate: 30);

        Assert.True(limiter.TryTake(100));
        Assert.True(limiter.TryTake(100));
        Assert.True(limiter.TryTake(100));
        Assert.False(limiter.TryTake(101));
        Assert.True(limiter.TryTake(161)); // 2 s later
        Assert.False(limiter.TryTake(162));
    }
}
