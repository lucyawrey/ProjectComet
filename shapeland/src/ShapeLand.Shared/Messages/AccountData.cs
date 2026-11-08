namespace ShapeLand.Shared.Messages
{
    /// <summary>Numbers are content numbers.</summary>
    public sealed class NewCharacter
    {
        public int Shape { get; set; }
        public int Color { get; set; }
        public int EyeColor { get; set; }
    }

    public sealed class CharacterView
    {
        public string Name { get; set; } = "";
        public int Shape { get; set; }
        public int Color { get; set; }
        public int EyeColor { get; set; }
    }
}
