namespace AzaleaGames.ProjectComet.Api.Entities;

public class Instance : IUpdated, ICreated
{
    public required string Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public required ContentZone ContentZone { get; set; }
    public required World World { get; set; }
    public required GameServer GameServer { get; set; }
}
