using Microsoft.AspNetCore.Identity;

namespace Comet.Data.Accounts;

/// <summary>
/// A player's or staff member's account: ASP.NET Core Identity's user, keyed by a Snowflake ID, with Comet's own
/// columns. Identity's email, phone and two-factor columns aren't stored (<see cref="CometAccountContext"/>).
/// Identity's own properties (<c>Id</c>, <c>UserName</c>) are on its base class, so they can't be marked
/// <c>required</c>: set them when making an account.
/// </summary>
public class Account : IdentityUser<long>, ITimestamped
{
    public AccountKind Kind { get; set; } = AccountKind.Player;
    public AccountStatus Status { get; set; } = AccountStatus.Active;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>When the first password was set. Null for a guest account.</summary>
    public DateTime? ClaimedAt { get; set; }

    /// <summary>The last time the account's session was used, updated at most once an hour.</summary>
    public DateTime LastActiveAt { get; set; }

    /// <summary>SHA-256 of the account's one recovery code, or null if it has none.</summary>
    public byte[]? RecoveryCodeHash { get; set; }

    public DateTime? RecoveryCodeCreatedAt { get; set; }

    /// <summary>
    /// Set when the account is deleted; it's purged after the game's hold. Until then its username stays taken, and
    /// signing in restores it.
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>Whether the account is deleted and waiting for the purge. Deletion isn't a <see cref="Status"/>, so a ban survives it.</summary>
    public bool IsDeleted => DeletedAt != null;
}
