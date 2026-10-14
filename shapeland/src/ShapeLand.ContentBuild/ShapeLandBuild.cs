using System.Text.Json.Serialization;
using Comet.Content;
using Comet.ContentBuild;
using ShapeLand.Shared.Content;
using Tomlyn.Serialization;

namespace ShapeLand.ContentBuild;

/// <summary>ShapeLand's content types and rules, run through Comet's content build.</summary>
public static class ShapeLandBuild
{
    /// <summary>The old test heightmap the game loads until it builds its world from zones, made by the <c>terrain</c> command.</summary>
    public const string TerrainPath = "zones/test/terrain.height";

    /// <summary>The test zone's folder, also made by the <c>terrain</c> command until it's built with the editor.</summary>
    public const string TestZonePath = "zones/test";

    /// <summary>Where asset models are, relative to the content folder: the Unity project's <c>Assets</c>, which imports them too.</summary>
    public const string ModelRoot = "../ShapeLand.Unity/Assets";

    /// <summary>
    /// A content build for ShapeLand's content folder at <paramref name="contentRoot"/>, with asset models under
    /// <paramref name="modelRoot"/> (by default <see cref="ModelRoot"/> from the content folder).
    /// </summary>
    public static ContentBuilder CreateBuilder(string contentRoot, string? modelRoot = null) => new ContentBuilder(contentRoot)
        .Add("shape", "shapes", ShapeLandToml.Default.Shape, ValidateShape)
        .AddStrings()
        .AddAssets(modelRoot ?? Path.Combine(contentRoot, ModelRoot))
        .AddZones();

    /// <summary>Checks and compiles the content folder; null content means there were errors.</summary>
    public static (ShapeLandContent? Content, IReadOnlyList<ContentError> Errors) Build(string contentRoot, bool saveRegistry = true, string? modelRoot = null)
    {
        var result = CreateBuilder(contentRoot, modelRoot).Build(saveRegistry);
        var errors = result.Errors.ToList();
        var terrainFile = Path.Combine(contentRoot, TerrainPath);
        Heightmap? terrain = null;
        try
        {
            using var stream = File.OpenRead(terrainFile);
            terrain = HeightmapFormat.Read(stream);
        }
        catch (Exception e) when (e is ContentException or IOException)
        {
            errors.Add(new ContentError(terrainFile, 1, 1, e.Message));
        }

        if (errors.Count > 0 || terrain is null)
        {
            return (null, errors);
        }

        // Players pick a shape to join, so the game can't run without one (a missing or renamed folder builds
        // empty otherwise).
        if (result.Entries<Shape>().Count == 0)
        {
            errors.Add(new ContentError(Path.Combine(contentRoot, "shapes"), 1, 1, "No shapes: the game needs at least one (shapes/*.toml)."));
            return (null, errors);
        }

        return (new ShapeLandContent(result.Entries<Shape>(), terrain, result.Strings!, result.Entries<Asset>(), result.Zones), errors);
    }

    private static void ValidateShape(Shape shape, ContentValidation validation)
    {
        if (string.IsNullOrWhiteSpace(shape.DisplayName))
        {
            validation.Error(nameof(Shape.DisplayName), "must not be empty.");
        }

        if (shape.MaxSpeed is <= 0 or > 50)
        {
            validation.Error(nameof(Shape.MaxSpeed), $"{shape.MaxSpeed} is out of range (above 0, at most 50 m/s).");
        }

        if (shape.JumpVelocity is <= 0 or > 30)
        {
            validation.Error(nameof(Shape.JumpVelocity), $"{shape.JumpVelocity} is out of range (above 0, at most 30 m/s).");
        }

        if (shape.Scale is <= 0 or > 4)
        {
            validation.Error(nameof(Shape.Scale), $"{shape.Scale} is out of range (above 0, at most 4).");
        }
    }
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[TomlSerializable(typeof(Shape))]
internal partial class ShapeLandToml : TomlSerializerContext
{
}
