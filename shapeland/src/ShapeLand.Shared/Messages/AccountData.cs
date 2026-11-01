namespace ShapeLand.Shared.Messages
{
    /// <summary>
    /// ShapeLand's part of the Login server's JSON (Comet's <c>LoginApi</c>): the look a new guest starts with, and
    /// what the client gets back about its character. Numbers are content numbers.
    /// </summary>
    public sealed class NewCharacter
    {
        public int Shape { get; set; }
        public int Color { get; set; }
        public int EyeColor { get; set; }
    }

    /// <summary>The player's character, as the Login server describes it.</summary>
    public sealed class CharacterView
    {
        public string Name { get; set; } = "";
        public int Shape { get; set; }
        public int Color { get; set; }
        public int EyeColor { get; set; }
    }
}
