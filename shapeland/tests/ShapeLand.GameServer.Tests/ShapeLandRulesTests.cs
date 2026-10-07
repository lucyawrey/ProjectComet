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
