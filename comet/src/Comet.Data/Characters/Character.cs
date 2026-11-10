using System.ComponentModel.DataAnnotations.Schema;

namespace Comet.Data.Characters;

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

    public DateTime? DeletedAt { get; set; }

    [NotMapped]
    public bool IsDeleted => DeletedAt != null;

    internal Character Copy() => (Character)MemberwiseClone();
}
