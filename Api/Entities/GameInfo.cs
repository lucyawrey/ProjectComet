namespace AzaleaGames.ProjectComet.Api.Entities;

public class GameInfo : IUpdated, ICreated
{
    public long Id { get; set; } = 0; // Primary key. Enforce constraint Id = 0 (there should only ever be one GameInfo)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public required string ServerGroupName { get; set; }
    public required string GameId { get; set; }
    public required string GameVersion { get; set; }
    public required List<string> SupportedClientGameIds { get; set; }
    public required List<string> SupportedClientGameVersions { get; set; }
    public required string GameDisplayName { get; set; }
}
