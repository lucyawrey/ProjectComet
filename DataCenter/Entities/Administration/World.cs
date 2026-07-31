namespace AzaleaGames.ProjectComet.DataCenter.Entities.Administration;

public class World : IUpdated, ICreated
{
    public required string Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsOverflowWorld { get; set; } = false;
    public required string DisplayName;
}
