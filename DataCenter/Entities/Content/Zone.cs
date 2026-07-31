namespace AzaleaGames.ProjectComet.DataCenter.Entities.Content;

public class ContentZone : IUpdated, ICreated
{
    public required string Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? DisplayName { get; set; }
    public required ZoneLocationType ZoneInstancingType { get; set; }
    public required ZoneData ContentZoneData { get; set; }
}

public class ZoneData
{

}

public enum ZoneInstancingType
{
    World = 0, // An open zone seemlessly connected to other world zones.
    Dungeon = 2, // A closed zone that is instanced seperately generated for each player or party that enters it.
    PlayerHousing = 3, // An zone that is instanced for each player, but can be set to be open to many players.
}
