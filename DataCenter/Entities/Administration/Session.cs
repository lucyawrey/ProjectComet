namespace AzaleaGames.ProjectComet.DataCenter.Entities;

public class Session
{
    public required string Id { get; set; } // Primary key
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public required DateTime ExpiresAt { get; set; }
    public required User User { get; set; }
}