using System.ComponentModel.DataAnnotations.Schema;

namespace Comet.Data.Characters;

public class Character : ITimestamped
{
    public required long Id { get; set; }

    /// <summary>The owning account's ID: a plain number, since accounts are in another database.</summary>
    public required long AccountId { get; set; }

    /// <summary>Unique per region among live characters, ignoring case (the collation); restoring a deleted one may need a new name.</summary>
    public required string Name { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    [NotMapped]
    public bool IsDeleted => DeletedAt != null;

    internal Character Copy() => (Character)MemberwiseClone();
}
