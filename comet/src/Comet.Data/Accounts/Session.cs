namespace Comet.Data.Accounts;

/// <summary>
/// One signed-in device. The client keeps the token (32 random bytes); only its hash is stored. Deleting the row
/// revokes it.
/// </summary>
public class Session : ITimestamped
{
    public required long Id { get; set; }
    public required long AccountId { get; set; }

    /// <summary>SHA-256 of the session token.</summary>
    public required byte[] TokenHash { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Moves forward when the session is used (sliding expiry), at most once an hour.</summary>
    public required DateTime ExpiresAt { get; set; }
}
