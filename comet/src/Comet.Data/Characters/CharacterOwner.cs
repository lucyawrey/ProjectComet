using System.ComponentModel.DataAnnotations.Schema;

namespace Comet.Data.Characters;

public class CharacterOwner : ITimestamped
{
    public required long CharacterId { get; set; }

    public required int ServerId { get; set; }

    public required long Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Set when the player leaves; null while a server holds the character.</summary>
    public DateTime? ReleasedAt { get; set; }

    [NotMapped]
    public bool IsHeld => ReleasedAt == null;
}
