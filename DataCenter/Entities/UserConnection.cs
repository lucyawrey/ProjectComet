using AzaleaGames.ProjectComet.DataCenter.Entities;

public class UserConnection : IUpdated, IGeneratedId
{
    public long Id { get; set; } = 0;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public required User User;
    public required GameServer CurrentGameServer { get; set; }
    public required World CurrentWorld { get; set; }
    public required Instance CurrentInstance { get; set; }
}
