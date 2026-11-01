namespace Comet.Data.Characters;

/// <summary>
/// A character: who it is and whose. A game adds its own columns to this table with
/// <see cref="CharacterColumns.AddCharacterColumns{T}"/>.
/// </summary>
public class Character : ITimestamped
{
    public long Id { get; set; }

    /// <summary>The owning account's ID: a plain number, since accounts are in another database.</summary>
    public long AccountId { get; set; }

    /// <summary>
    /// Capitals kept; unique per region among live characters, ignoring case (the column's collation). A deleted
    /// character's name is free for others; restoring it may need a new one.
    /// </summary>
    public string Name { get; set; } = "";

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Set when the character is deleted; the row is purged after the game's hold.</summary>
    public DateTime? DeletedAt { get; set; }
}
