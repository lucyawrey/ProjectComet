using MessagePack;

namespace ShapeLand.Shared.Messages
{
    [MessagePackObject]
    public struct SpawnSet
    {
        [Key(0)]
        public int Spawn;

        [Key(1)]
        public bool Claimed;
    }

    [MessagePackObject]
    public struct ReturnToSpawn { }
}
