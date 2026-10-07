namespace Comet.ContentBuild.Tests;

public class IdRegistryTests
{
    [Fact]
    public void NewKeysGetTheNextNumbersAndKeepThem()
    {
        using var folder = new ContentFolder();
        folder.Write("ids.toml", "[widget]\nold = 1\ngone = 7\n");
        folder.Write("widgets/old.toml", "id = \"widget.old\"\nsize = 1\n");
        folder.Write("widgets/new_b.toml", "id = \"widget.new_b\"\nsize = 1\n");
        folder.Write("widgets/new_a.toml", "id = \"widget.new_a\"\nsize = 1\n");

        var first = folder.Build();
        var saved = File.ReadAllText(folder.Registry);
        var second = folder.Build();

        Assert.Equal(
            [("widget.old", 1), ("widget.new_a", 8), ("widget.new_b", 9)],
            first.Entries<Widget>().Select(w => (w.Id, w.Number)));
        Assert.EndsWith("[widget]\nold = 1\ngone = 7\nnew_a = 8\nnew_b = 9\n", saved);
        Assert.Equal(saved, File.ReadAllText(folder.Registry));
        Assert.Equal([1, 8, 9], second.Entries<Widget>().Select(w => w.Number));
    }

    [Theory]
    [InlineData("[widget]\na = 1\nb = 1\n", 3, "Number 1 is used by both")]
    [InlineData("[widget]\na = 1\na = 2\n", 3, "")]
    [InlineData("[widget]\na = 0\n", 2, "out of range")]
    [InlineData("[widget]\na = \"one\"\n", 2, "Expected 'key = number'")]
    [InlineData("a = 1\n", 1, "belong in a [type] table")]
    public void BadRegistriesFail(string registry, int line, string message)
    {
        using var folder = new ContentFolder();
        folder.Write("ids.toml", registry);
        folder.Write("widgets/a.toml", "id = \"widget.a\"\nsize = 1\n");

        var error = Assert.Single(folder.Build().Errors);

        Assert.EndsWith("ids.toml", error.File);
        Assert.Equal(line, error.Line);
        Assert.Contains(message, error.Message);
        Assert.Equal(registry, File.ReadAllText(folder.Registry));
    }
}
