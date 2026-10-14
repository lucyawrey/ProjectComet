using MessagePack;

namespace ShapeLand.Shared.Messages
{
    /// <summary>
    /// Server event: the player's spawn point, where they go on falling out of the world or returning: sent on
    /// joining, and whenever they claim another by standing in it.
    /// </summary>
    [MessagePackObject]
    public struct SpawnSet
    {
        /// <summary>The spawn point's content number.</summary>
        [Key(0)] public int Spawn;
    }

    /// <summary>Client input: send me back to my spawn point.</summary>
    [MessagePackObject]
    public struct ReturnToSpawn
    {
    }
}
