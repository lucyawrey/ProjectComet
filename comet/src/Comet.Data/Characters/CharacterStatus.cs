namespace Comet.Data.Characters;

/// <summary>Where a character is, saved periodically and on leaving. No row until the first save.</summary>
public class CharacterStatus : ITimestamped
{
    public required long CharacterId { get; set; }

    /// <summary>The zone's content number.</summary>
    public required int Zone { get; set; }

    public required float X { get; set; }
    public required float Y { get; set; }
    public required float Z { get; set; }
    public required float Facing { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
