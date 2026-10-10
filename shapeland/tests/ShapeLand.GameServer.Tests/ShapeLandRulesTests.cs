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
