using MessagePack;

namespace ShapeLand.Shared.Messages
{
    /// <summary>
    /// The client builds the zone's world and holds the player still until it's ready; the player's arrival
    /// correction, and spawns for the players already there, follow.
    /// </summary>
    [MessagePackObject]
    public struct ZoneEntered
    {
        [Key(0)]
        public int Zone;

        [Key(1)]
        public string[] Flipped;
    }

    [MessagePackObject]
    public struct PlacementsFlipped
    {
        [Key(0)]
        public string Tag;

        /// <summary>True when they're flipped from how they start, false when they're back.</summary>
        [Key(1)]
        public bool Flipped;
    }
}
