using System.Net;

namespace AzaleaGames.ProjectComet.Api.Entities;

public class GameServer : IUpdated, ICreated
{
    public required string Id;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public required IPAddress Address;
    public int CurrentUserCount = 0;
}
