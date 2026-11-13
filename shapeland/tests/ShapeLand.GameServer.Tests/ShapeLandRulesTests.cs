using ShapeLand.Shared.World;

namespace ShapeLand.GameServer.Tests;

public class ShapeLandRulesTests
{
    [Theory]
    [InlineData("  Ada  ", "Ada")]
    [InlineData("Bot01", "Bot01")]
    [InlineData("x_y-z", "x_y-z")]
    [InlineData("Max_Sixteen_Char", "Max_Sixteen_Char")]
    public void AcceptsAndTrimsNames(string name, string expected)
    {
        Assert.True(ShapeLandRules.TryNormalizeName(name, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ab")]
    [InlineData("has space")]
    [InlineData("seventeen chars!!")]
    [InlineData("ThisNameIsTooLong")]
    [InlineData("emoji 🙂")]
    public void RejectsBadNames(string name) => Assert.False(ShapeLandRules.TryNormalizeName(name, out _));

    [Fact]
    public void ChatIsTrimmedAndLimited()
    {
        Assert.True(ShapeLandRules.TryNormalizeChat("  hi  ", out var text));
        Assert.Equal("hi", text);
        Assert.False(ShapeLandRules.TryNormalizeChat("   ", out _));
        Assert.True(ShapeLandRules.TryNormalizeChat(new string('a', 200), out _));
        Assert.False(ShapeLandRules.TryNormalizeChat(new string('a', 201), out _));
    }
}
