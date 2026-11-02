using System.ComponentModel.DataAnnotations.Schema;
namespace Comet.Data.Characters;

/// <summary>
/// A character: who it is and whose. A game adds its own columns by deriving its character class from this one;
/// only the game's class is mapped (<see cref="CometRegionContext{TCharacter}"/>), so the table holds both.
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

    // Not stored: computed from the columns above.

    [NotMapped]
    public bool IsDeleted => DeletedAt != null;

    /// <summary>A shallow copy (the repository's, so a queued save doesn't share the game's object).</summary>
    internal Character Copy() => (Character)MemberwiseClone();
}
