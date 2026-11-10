using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace Comet.Data.Accounts;

/// <summary>
/// Identity's own properties (<c>Id</c>, <c>UserName</c>) can't be marked <c>required</c> here: set them when making
/// an account.
/// </summary>
public class Account : IdentityUser<long>, ITimestamped
{
    public AccountKind Kind { get; set; } = AccountKind.Player;
    public AccountStatus Status { get; set; } = AccountStatus.Active;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public DateTime? ClaimedAt { get; set; }

    public required DateTime LastActiveAt { get; set; }

    public byte[]? RecoveryCodeHash { get; set; }

    public DateTime? RecoveryCodeCreatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    /// <summary>Deletion isn't a <see cref="Status"/>, so a ban survives it.</summary>
    [NotMapped]
    public bool IsDeleted => DeletedAt != null;

    [NotMapped]
    public bool IsGuest => ClaimedAt == null;

    [NotMapped]
    public bool IsBanned => Status == AccountStatus.Banned;

    [NotMapped]
    public bool HasRecoveryCode => RecoveryCodeHash != null;
}
