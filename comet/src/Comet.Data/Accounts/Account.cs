using Microsoft.AspNetCore.Identity;

namespace Comet.Data.Accounts;

/// <summary>
/// A player's or staff member's account: ASP.NET Core Identity's user, keyed by a Snowflake ID, with Comet's own
/// columns. Identity's email, phone and two-factor columns aren't stored (<see cref="CometAccountContext"/>).
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
}
