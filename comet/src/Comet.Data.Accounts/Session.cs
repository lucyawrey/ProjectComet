namespace Comet.Data.Accounts;

public class Session : ITimestamped
{
    public required long Id { get; set; }
    public required long AccountId { get; set; }

    public required byte[] TokenHash { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public required DateTime ExpiresAt { get; set; }

    public bool IsExpired(DateTime now) => now >= ExpiresAt;
}
