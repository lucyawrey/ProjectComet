using System.Text.Json.Serialization;
using Comet.Content;
using Tomlyn.Serialization;

namespace Comet.ContentBuild.Tests;

// A test content type covering nested tables and arrays, so the build is tested without any game's types.
public sealed class Widget : IContentEntry
{
    [JsonIgnore]
    public int Number { get; set; }

    [JsonRequired]
    public string Id { get; set; } = "";

    [JsonRequired]
    public int Size { get; set; }

    public string[] Tags { get; set; } = [];

    public float Weight { get; set; }

    public Colour? Colour { get; set; }

    public Part[] Parts { get; set; } = [];
}

public sealed class Colour
{
    public int Red { get; set; }

    public int Green { get; set; }

    public int Blue { get; set; }
}

public sealed class Part
{
    public string Name { get; set; } = "";

    public float Length { get; set; }

    public Dictionary<string, int> Stats { get; set; } = new();
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[TomlSerializable(typeof(Widget))]
internal partial class TestToml : TomlSerializerContext
{
}
