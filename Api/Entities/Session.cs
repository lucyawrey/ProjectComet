namespace AzaleaGames.ProjectComet.Api.Entities;

public class Session
{
    public required string Id { get; set; }
    public required DateTime ExpiresAt { get; set; }
    public required User User { get; set; }
}