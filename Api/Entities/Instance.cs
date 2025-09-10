namespace AzaleaGames.ProjectComet.Api.Entities;

public class Instance : IUpdated, ICreated
{
    public required string Id;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int CurrentUserCount = 0;
    public required string ZoneId;
    public required World World;
    public required GameServer GameServer;
}
