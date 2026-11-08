using MessagePack;

namespace ShapeLand.Shared.Messages
{
    /// <summary>Sent on joining, and whenever the player claims another spawn point by standing in it.</summary>
    [MessagePackObject]
    public struct SpawnSet
    {
        [Key(0)]
        public int Spawn;

        /// <summary>Whether the player just claimed it (rather than starting there).</summary>
        [Key(1)]
        public bool Claimed;
    }

    [MessagePackObject]
    public struct ReturnToSpawn { }
}
