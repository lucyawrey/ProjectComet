namespace AzaleaGames.ProjectComet.DataCenter.Entities;

public class Server : IUpdated, ICreated
{
    public required string Id { get; set; } // Primary key
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public required ServerType ServerType { get; set; }
    public required IPAddress Address { get; set; }

    public enum ServerType
    {
        DataCenterServer = 0,
        GameServer = 1,
    }
}
