using System.ComponentModel.DataAnnotations.Schema;

namespace Comet.Data.Characters;

/// <summary>
/// A game adds its own columns by deriving from this; only the game's class is mapped
/// (<see cref="CometRegionContext{TCharacter}"/>).
/// </summary>
public class Character : ITimestamped
{
    public required long Id { get; set; }

    /// <summary>The owning account's ID: a plain number, since accounts are in another database.</summary>
    public required long AccountId { get; set; }

    /// <summary>
    /// Capitals kept; unique per region among live characters, ignoring case (the column's collation). A deleted
    /// character's name is free for others; restoring it may need a new one.
    /// </summary>
    public required string Name { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Set when the character is deleted; the row is purged after the game's hold.</summary>
    public DateTime? DeletedAt { get; set; }

    [NotMapped]
    public bool IsDeleted => DeletedAt != null;

    /// <summary>A shallow copy, so a queued save doesn't share the game's object.</summary>
    internal Character Copy() => (Character)MemberwiseClone();
}
