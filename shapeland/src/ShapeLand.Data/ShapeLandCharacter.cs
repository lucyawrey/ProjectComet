using Comet.Data.Characters;

namespace ShapeLand.Data;

/// <summary>A ShapeLand character: Comet's columns, plus the look.</summary>
public class ShapeLandCharacter : Character
{
    /// <summary>The shape's content number.</summary>
    public required int Shape { get; set; }

    /// <summary>The body color's content number.</summary>
    public required int Color { get; set; }

    /// <summary>The eye color's content number, from the same colors.</summary>
    public required int EyeColor { get; set; }
}
