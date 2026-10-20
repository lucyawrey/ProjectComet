using System.Text.Json.Serialization;
using Comet.Content;
using Comet.ContentBuild;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.World;
using Tomlyn.Serialization;

namespace ShapeLand.ContentBuild;

/// <summary>ShapeLand's content types and rules, run through Comet's content build.</summary>
public static class ShapeLandBuild
{
    /// <summary>The test zone's folder, made by the <c>terrain</c> command until ShapeLand's level is built with the editor.</summary>

    /// <summary>Where prop models are, relative to the content folder: the Unity project's <c>Assets</c>, which imports them too.</summary>
    public const string ModelRoot = "../ShapeLand.Unity/Assets";

    /// <summary>
    /// A content build for ShapeLand's content folder at <paramref name="contentRoot"/>, with prop models under
    /// <paramref name="modelRoot"/> (by default <see cref="ModelRoot"/> from the content folder).
    /// </summary>
    public static ContentBuilder CreateBuilder(string contentRoot, string? modelRoot = null) => new ContentBuilder(contentRoot)
        .Add("shape", "shapes", ShapeLandToml.Default.Shape, ValidateShape)
        .Add("settings", "settings", ShapeLandToml.Default.GameSettings, ValidateSettings)
        .AddStrings()
        .AddProps(modelRoot ?? Path.Combine(contentRoot, ModelRoot))
        .AddGroups()
        .AddZones();

    /// <summary>
    /// Checks and compiles the content folder; null content means there were errors. Warnings (blockout placements)
    /// go into <paramref name="warnings"/>, or with <paramref name="release"/> are errors.
    /// </summary>
    public static (ShapeLandContent? Content, IReadOnlyList<ContentError> Errors) Build(string contentRoot, bool saveRegistry = true, string? modelRoot = null,
        bool release = false, ICollection<ContentError>? warnings = null)
    {
        var result = CreateBuilder(contentRoot, modelRoot).Build(saveRegistry, release);
        foreach (var warning in result.Warnings)
        {
            warnings?.Add(warning);
        }

        var errors = result.Errors.ToList();
        if (errors.Count > 0)
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

        // One set of game-wide values, and the pads it names must be primitive kinds.
        var settings = result.Entries<GameSettings>();
        if (settings.Count != 1)
        {
            errors.Add(new ContentError(Path.Combine(contentRoot, "settings"), 1, 1, $"{settings.Count} game settings files: the game needs exactly one (settings/game.toml)."));
            return (null, errors);
        }

        foreach (var (field, pad) in new[] { ("spawns.pad", settings[0].Spawns.Pad), ("teleporters.pad", settings[0].Teleporters.Pad) })
        {
            if (PrimitiveKinds.Named(pad) == null)
            {
                errors.Add(new ContentError(Path.Combine(contentRoot, "settings"), 1, 1, $"{field}: '{pad}' isn't a primitive kind."));
                return (null, errors);
            }
        }

        // Each shape's look is a group with a body for the player's color (errors against the shape's own file, by its key).
        var groups = result.Groups.ToDictionary(g => g.Id);
        foreach (var shape in result.Entries<Shape>())
        {
            var file = Path.Combine(contentRoot, "shapes", shape.Id.Substring("shape.".Length) + ".toml");
            if (!File.Exists(file))
            {
                file = Path.Combine(contentRoot, "shapes");
            }

            if (!groups.ContainsKey(shape.Look))
            {
                errors.Add(new ContentError(file, 1, 1, $"{shape.Id}: look '{shape.Look}' isn't a group (groups/*.toml)."));
            }
            else
            {
                var parts = Groups.Expand(new Placement { Group = shape.Look }, key => groups.TryGetValue(key, out var g) ? g : null);
                if (parts.FirstOrDefault(p => p.Primitive == null) is { } prop)
                {
                    errors.Add(new ContentError(file, 1, 1, $"{shape.Id}: its look {shape.Look} has {prop.Prop}, but looks are built only from primitives."));
                }
                else if (!parts.Any(p => p.Role == "body"))
                {
                    errors.Add(new ContentError(file, 1, 1, $"{shape.Id}: its look {shape.Look} has no part with role = \"body\" to take the player's color."));
                }
            }
        }

        // Each shape starts at a spawn point, in any zone.
        var spawns = result.Zones.SelectMany(z => z.Chunks).SelectMany(c => c.Spawns).Select(s => s.Id).ToHashSet();
        foreach (var shape in result.Entries<Shape>().Where(s => !spawns.Contains(s.Spawn)))
        {
            errors.Add(new ContentError(Path.Combine(contentRoot, "shapes"), 1, 1, $"{shape.Id}: spawn '{shape.Spawn}' isn't a spawn point; change the shape's 'spawn =' to one there is: {(spawns.Count > 0 ? string.Join(", ", spawns.OrderBy(k => k, StringComparer.Ordinal)) : "(none yet)")}."));
        }

        if (errors.Count > 0)
        {
            return (null, errors);
        }


        return (new ShapeLandContent(result.Entries<Shape>(), settings[0], result.Strings!, result.Entries<Prop>(), result.Zones, result.Groups), errors);
    }

    private static void ValidateSettings(GameSettings settings, ContentValidation validation)
    {
        void InRange(string table, string field, float value, float max)
        {
            if (value <= 0 || value > max)
            {
                validation.Error(table, field, $"{value} is out of range (above 0, at most {max}).");
            }
        }

        void IsColor(string table, string field, uint color)
        {
            if (color > 0xFFFFFF)
            {
                validation.Error(table, field, $"0x{color:X} isn't a color: use 0xRRGGBB.");
            }
        }

        const string movement = nameof(GameSettings.Movement), world = nameof(GameSettings.World), spawns = nameof(GameSettings.Spawns), teleporters = nameof(GameSettings.Teleporters), triggers = nameof(GameSettings.Triggers), colors = nameof(GameSettings.Colors), primitives = nameof(GameSettings.Primitives);
        var m = settings.Movement;
        InRange(movement, nameof(m.Gravity), m.Gravity, 200);
        InRange(movement, nameof(m.BodyRadius), m.BodyRadius, 5);
        InRange(movement, nameof(m.BodyHeight), m.BodyHeight, 10);
        InRange(movement, nameof(m.StepHeight), m.StepHeight, m.BodyHeight);
        InRange(movement, nameof(m.GroundSnap), m.GroundSnap, 5);
        InRange(movement, nameof(m.GroundAcceleration), m.GroundAcceleration, 1000);
        InRange(movement, nameof(m.AirAcceleration), m.AirAcceleration, 1000);
        InRange(movement, nameof(m.TurnSpeed), m.TurnSpeed, 10000);
        InRange(spawns, nameof(SpawnSettings.StandSeconds), settings.Spawns.StandSeconds, 60);
        InRange(spawns, nameof(SpawnSettings.PadHeight), settings.Spawns.PadHeight, 5);
        InRange(teleporters, nameof(TeleporterSettings.StandSeconds), settings.Teleporters.StandSeconds, 60);
        InRange(teleporters, nameof(TeleporterSettings.PadHeight), settings.Teleporters.PadHeight, 5);
        if (settings.Triggers.StandSeconds < 0 || settings.Triggers.StandSeconds > 60)
        {
            validation.Error(triggers, nameof(TriggerSettings.StandSeconds), $"{settings.Triggers.StandSeconds} is out of range (0 to 60).");
        }

        var budget = settings.Primitives;
        InRange(primitives, nameof(budget.FacetLength), budget.FacetLength, 100);
        if (budget.MinSides < 3)
        {
            validation.Error(primitives, nameof(budget.MinSides), $"{budget.MinSides} is too few: a round primitive needs at least 3 sides.");
        }

        if (budget.MaxSides < budget.MinSides || budget.MaxSides > 256)
        {
            validation.Error(primitives, nameof(budget.MaxSides), $"{budget.MaxSides} is out of range (at least min_sides, {budget.MinSides}, and at most 256).");
        }

        IsColor(world, nameof(WorldSettings.GroundColor), settings.World.GroundColor);
        IsColor(world, nameof(WorldSettings.UnderColor), settings.World.UnderColor);
        IsColor(world, nameof(WorldSettings.PlacedColor), settings.World.PlacedColor);
        IsColor(spawns, nameof(SpawnSettings.UnsetColor), settings.Spawns.UnsetColor);
        IsColor(teleporters, nameof(TeleporterSettings.PadColor), settings.Teleporters.PadColor);
        foreach (var color in settings.Colors.Body)
        {
            IsColor(colors, nameof(ColorSettings.Body), color);
        }

        foreach (var color in settings.Colors.Eyes)
        {
            IsColor(colors, nameof(ColorSettings.Eyes), color);
        }

        if (settings.Colors.Body.Length == 0)
        {
            validation.Error(colors, nameof(ColorSettings.Body), "players need at least one body color.");
        }

        if (settings.Colors.Eyes.Length == 0)
        {
            validation.Error(colors, nameof(ColorSettings.Eyes), "players need at least one eye color.");
        }
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

        if (shape.Hover is < 0 or > 2)
        {
            validation.Error(nameof(Shape.Hover), $"{shape.Hover} is out of range (0 to 2 m).");
        }
    }
}

[TomlSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[TomlSerializable(typeof(Shape))]
[TomlSerializable(typeof(GameSettings))]
internal partial class ShapeLandToml : TomlSerializerContext
{
}
