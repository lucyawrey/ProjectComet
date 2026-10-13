using Comet.Content;

namespace Comet.ContentBuild.Tests;

public class StringFileTests
{
    [Fact]
    public void ReadsTextByDottedKey()
    {
        using var folder = new ContentFolder();
        folder.Write("strings/en.toml", "title = \"Hello\"\n\n[join]\nname_taken = \"{name} is taken.\"\n\n[join.card]\nok = \"OK\"\n");

        var result = Build(folder);

        Assert.Empty(result.Errors);
        Assert.Equal("Hello", result.Strings!.Get("title"));
        Assert.Equal("Sam is taken.", result.Strings.Format("join.name_taken", ("name", "Sam")));
        Assert.Equal("OK", result.Strings.Get("join.card.ok"));
        Assert.Equal("[join.missing]", result.Strings.Get("join.missing"));
    }

    [Theory]
    [InlineData("a = 1\n", 1, "must be text in quotes")]
    [InlineData("[join]\nBad = \"x\"\n", 2, "'Bad' isn't a valid key")]
    [InlineData("[Join]\nok = \"x\"\n", 1, "'Join' isn't a valid key")]
    [InlineData("a = \"Hi {Name}\"\n", 1, "'{Name}'")]
    [InlineData("a = \"Hi {name\"\n", 1, "'{'")]
    [InlineData("a = \"Hi }\"\n", 1, "'}'")]
    [InlineData("a = { b = \"x\" }\n", 1, "must be text in quotes")]
    [InlineData("a = \"x\"\na = \"y\"\n", 2, "")]
    public void BadStringsFailWithTheirLine(string toml, int line, string message)
    {
        using var folder = new ContentFolder();
        folder.Write("strings/en.toml", toml);

        var error = Assert.Single(Build(folder).Errors);

        Assert.EndsWith(Path.Combine("strings", "en.toml"), error.File);
        Assert.Equal(line, error.Line);
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void AMissingStringFileFails()
    {
        using var folder = new ContentFolder();

        var error = Assert.Single(Build(folder).Errors);

        Assert.Contains("No string file", error.Message);
    }

    [Fact]
    public void FormatLeavesUnknownPlaceholders()
    {
        var table = new StringTable { Entries = { ["a"] = "{x} and {y}" } };

        Assert.Equal("1 and {y}", table.Format("a", ("x", 1)));
    }

    private static ContentBuildResult Build(ContentFolder folder) =>
        new ContentBuilder(folder.Root).Add("widget", "widgets", TestToml.Default.Widget).AddStrings().Build();
}
