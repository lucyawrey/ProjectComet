namespace AzaleaGames.ProjectComet.Api.Entities;

public class World : IUpdated, ICreated
{
    public required string Id;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsOverflowWorld { get; set; } = false;
    public required string DisplayName;
}
