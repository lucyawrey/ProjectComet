using MessagePack;

namespace ShapeLand.Shared.Messages
{
    /// <summary>
    /// Server event: the player is now in a zone: on joining, and on going to another (by a teleporter, or back to
    /// a spawn point there). The client builds the zone's world and holds the player still until it's ready; the
    /// player's arrival correction, and spawns for the players already there, follow.
    /// </summary>
    [MessagePackObject]
    public struct ZoneEntered
    {
        /// <summary>The zone's content number.</summary>
        [Key(0)] public int Zone;

        /// <summary>The tags of the zone's placements that triggers have flipped from how they start.</summary>
        [Key(1)] public string[] Flipped;
    }

    /// <summary>Server event: a trigger flipped the placements with a tag in the player's zone, or they flipped back.</summary>
    [MessagePackObject]
    public struct PlacementsFlipped
    {
        [Key(0)] public string Tag;

        /// <summary>True when they're flipped from how they start, false when they're back.</summary>
        [Key(1)] public bool Flipped;
    }
}
