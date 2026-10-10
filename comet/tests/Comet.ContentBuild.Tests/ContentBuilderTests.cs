namespace Comet.ContentBuild.Tests;

public class ContentBuilderTests
{
    [Fact]
    public void ReadsValidFilesInNumberOrder()
    {
        using var folder = new ContentFolder();
        folder.Write("widgets/b.toml", "id = \"widget.b\"\nsize = 2\ncolour = { red = 1, green = 2, blue = 3 }\n");
        folder.Write("widgets/sub/a.toml", "id = \"widget.a\"\nsize = 1\ntags = [\"x\"]\n\n[[parts]]\nname = \"p\"\nstats = { speed = 3 }\n");

        var result = folder.Build();

        Assert.Empty(result.Errors);
        var widgets = result.Entries<Widget>();
        Assert.Equal(["widget.a", "widget.b"], widgets.Select(w => w.Id));
        Assert.Equal([1, 2], widgets.Select(w => w.Number));
        Assert.Equal(3, widgets[1].Colour!.Blue);
        Assert.Equal(3, widgets[0].Parts[0].Stats["speed"]);
    }

    [Theory]
    [InlineData("id = \"widget.a\"\nsize = 1\nsise = 2\n", 3, "Unknown key 'sise' in widget")]
    [InlineData("id = \"widget.a\"\nsize = 1\ncolour = { red = 1, purple = 2 }\n", 3, "Unknown key 'purple' in colour")]
    [InlineData("id = \"widget.a\"\nsize = 1\ncolour.reed = 1\n", 3, "Unknown key 'reed' in colour")]
    [InlineData("id = \"widget.a\"\nsize = 1\n\n[[parts]]\nname = \"p\"\nnmae = \"q\"\n", 6, "Unknown key 'nmae' in part")]
    [InlineData("id = \"widget.a\"\n", 1, "Missing required TOML key 'size'")]
    [InlineData("id = \"widget.a\"\nsize = \"big\"\n", 2, "Expected Integer token but was String")]
    [InlineData("id = \"widget.a\"\nsize = \n", 2, "")]
    [InlineData("id = \"thing.a\"\nsize = 1\n", 1, "'thing.a' isn't a valid widget id")]
    [InlineData("id = \"widget.Bad-Name\"\nsize = 1\n", 1, "isn't a valid widget id")]
    [InlineData("id = \"widget.a\"\nsize = 1\nnumber = 5\n", 3, "Unknown key 'number' in widget")]
    [InlineData("id = \"widget.a\"\nsize = 1\nweight = nan\n", 3, "content numbers must be finite")]
    [InlineData("id = \"widget.a\"\nsize = 1\nweight = -inf\n", 3, "content numbers must be finite")]
    [InlineData("id = \"widget.a\"\nsize = 1\n\n[[parts]]\nname = \"p\"\nlength = +inf\n", 6, "content numbers must be finite")]
    public void BadFilesFailWithTheirLine(string toml, int line, string message)
    {
        using var folder = new ContentFolder();
        folder.Write("widgets/a.toml", toml);

        var result = folder.Build();

        var error = Assert.Single(result.Errors);
        Assert.EndsWith(Path.Combine("widgets", "a.toml"), error.File);
        Assert.Equal(line, error.Line);
        Assert.Contains(message, error.Message);
        Assert.Throws<InvalidOperationException>(() => result.Entries<Widget>());
    }

    [Fact]
    public void DuplicateIdsNameTheFirstFile()
    {
        using var folder = new ContentFolder();
        folder.Write("widgets/a.toml", "id = \"widget.a\"\nsize = 1\n");
        folder.Write("widgets/b.toml", "size = 1\nid = \"widget.a\"\n");

        var error = Assert.Single(folder.Build().Errors);

        Assert.EndsWith("b.toml", error.File);
        Assert.Equal(2, error.Line);
        Assert.Contains("already used by", error.Message);
        Assert.Contains("a.toml", error.Message);
    }

    [Fact]
    public void ValidationErrorsPointAtTheField()
    {
        using var folder = new ContentFolder();
        folder.Write("widgets/a.toml", "id = \"widget.a\"\nsize = -4\n");

        var result = folder.Build((widget, validation) =>
        {
            if (widget.Size < 0)
            {
                validation.Error(nameof(Widget.Size), "must not be negative.");
            }
        });

        var error = Assert.Single(result.Errors);
        Assert.Equal((2, 8), (error.Line, error.Column));
        Assert.Equal("size: must not be negative.", error.Message);
        Assert.Matches(@"a\.toml\(2,8\): error: size: must not be negative\.$", error.ToString());
    }

    [Theory]
    [InlineData("id = \"widget.a\"\nsize = 1\n\n[colour]\nred = 1\nblue = -1\n", 6, 8)]
    [InlineData("id = \"widget.a\"\nsize = 1\ncolour.blue = -1\n", 3, 15)]
    [InlineData("id = \"widget.a\"\nsize = 1\ncolour = { red = 1, blue = -1 }\n", 3, 28)]
    public void ValidationErrorsInATablePointAtTheField(string toml, int line, int column)
    {
        using var folder = new ContentFolder();
        folder.Write("widgets/a.toml", toml);

        var result = folder.Build((widget, validation) =>
        {
            if (widget.Colour?.Blue < 0)
            {
                validation.Error(nameof(Widget.Colour), nameof(Colour.Blue), "must not be negative.");
            }
        });

        var error = Assert.Single(result.Errors);
        Assert.Equal((line, column), (error.Line, error.Column));
        Assert.Equal("colour.blue: must not be negative.", error.Message);
    }

    [Fact]
    public void ReportsErrorsFromEveryFile()
    {
        using var folder = new ContentFolder();
        folder.Write("widgets/a.toml", "id = \"widget.a\"\n");
        folder.Write("widgets/b.toml", "id = \"widget.b\"\nsize = 1\nextra = 1\n");
        folder.Write("widgets/c.toml", "id = \"widget.c\"\nsize = 1\n");

        Assert.Equal(2, folder.Build().Errors.Count);
        Assert.False(File.Exists(folder.Registry));
    }

    [Fact]
    public void SchemaMatchesTheReader()
    {
        var schema = new ContentBuilder(".").Add("widget", "widgets", TestToml.Default.Widget).Schemas()["widget"];

        Assert.Equal(["id", "size"], schema["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
        Assert.Null(schema["properties"]!["number"]);
        Assert.Equal("^widget\\.[a-z][a-z0-9_]*$", schema["properties"]!["id"]!["pattern"]!.GetValue<string>());
        Assert.False(schema["properties"]!["colour"]!["additionalProperties"]!.GetValue<bool>());
    }
}
