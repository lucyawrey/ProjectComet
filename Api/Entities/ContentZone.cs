namespace AzaleaGames.ProjectComet.Api.Entities;

public class ContentZone : IUpdated, ICreated
{
    public required string Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? DisplayName { get; set; }
    public required ZoneLocationType ZoneLocationType { get; set; }
    public required ZoneLocationType ZoneInstancingType { get; set; }
    public required ZoneData ContentZoneData { get; set; }
}

public class ZoneData
{

}

public enum ZoneLocationType
{
    Connected = 0,
    Separated = 1,
}

public enum ZoneInstancingType
{
    World = 0,
    Dungeon = 1,
    UserGenerated = 2,
}
