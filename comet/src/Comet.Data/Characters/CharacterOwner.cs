using System.ComponentModel.DataAnnotations.Schema;

namespace Comet.Data.Characters;

/// <summary>
/// Which game server holds a character. Each join claims it and adds one to <see cref="Version"/>; status saves
/// apply only while the version is still the saving server's, so a stale save changes nothing.
/// </summary>
public class CharacterOwner : ITimestamped
{
    public required long CharacterId { get; set; }

    /// <summary>The game server's node number (the one its Snowflake IDs use).</summary>
    public required int ServerId { get; set; }

    public required long Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Set when the player leaves; null while a server holds the character.</summary>
    public DateTime? ReleasedAt { get; set; }

    // Not stored: computed from the columns above.

    /// <summary>A server holds the character now: claimed and not released since.</summary>
    [NotMapped]
    public bool IsHeld => ReleasedAt == null;
}
