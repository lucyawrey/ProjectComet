using Comet.Content;

namespace Comet.ContentBuild.Tests;

public class ContentTableTests
{
    private static readonly ContentTable<Widget> Table = new("widget", [new Widget { Id = "widget.a", Number = 4 }]);

    [Fact]
    public void FindsByKeyAndNumber()
    {
        Assert.Same(Table["widget.a"], Table[4]);
        Assert.True(Table.TryGet("widget.a", out _));
    }

    [Fact]
    public void UnknownIdsFailLoudly()
    {
        Assert.Equal("Unknown widget id 'widget.b'.", Assert.Throws<ContentException>(() => Table["widget.b"]).Message);
        Assert.Throws<ContentException>(() => Table[5]);
        Assert.False(Table.TryGet("widget.b", out _));
    }
}
