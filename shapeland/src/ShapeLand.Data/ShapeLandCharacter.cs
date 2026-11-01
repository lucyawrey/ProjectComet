using Comet.Data.Characters;

namespace ShapeLand.Data;

/// <summary>ShapeLand's columns on Comet's <c>character</c> table: the look.</summary>
public class ShapeLandCharacter : ICharacterColumns
{
    public long Id { get; set; }

    /// <summary>The shape's content number.</summary>
    public int Shape { get; set; }

    /// <summary>The body color's content number.</summary>
    public int Color { get; set; }

    /// <summary>The eye color's content number, from the same colors.</summary>
    public int EyeColor { get; set; }
}
