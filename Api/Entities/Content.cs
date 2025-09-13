using AzaleaGames.ProjectComet.Api.Entities;

public class Content : IUpdated, IGeneratedId
{
    public long Id { get; set; } = 0;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
