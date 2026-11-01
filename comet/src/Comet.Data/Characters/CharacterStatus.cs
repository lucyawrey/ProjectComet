namespace Comet.Data.Characters;

/// <summary>Where a character is, saved periodically and on leaving. No row until the first save.</summary>
public class CharacterStatus : ITimestamped
{
    public long CharacterId { get; set; }

    /// <summary>The zone's content number.</summary>
    public int Zone { get; set; }

    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Facing { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
