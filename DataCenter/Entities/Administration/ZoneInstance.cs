namespace AzaleaGames.ProjectComet.DataCenter.Entities;

public class ZoneInstance : IUpdated, ICreated
{
    public required string Id { get; set; } // Primary key
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public required Zone Zone { get; set; }
    public required World World { get; set; }
    public required Server GameServer { get; set; }
}
