using Comet.Data.Characters;

namespace ShapeLand.Data;

public class ShapeLandCharacter : Character
{
    public required int Shape { get; set; }

    public required int Color { get; set; }

    public required int EyeColor { get; set; }
}
