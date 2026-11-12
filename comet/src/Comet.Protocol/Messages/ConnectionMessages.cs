using MessagePack;

namespace Comet.Protocol.Messages
{
    [MessagePackObject]
    public struct Welcome
    {
        [Key(0)]
        public uint EntityId;

        /// <summary>Server simulation ticks per second.</summary>
        [Key(1)]
        public int TickRate;

        /// <summary>The zone this server plays, by its content number; 0 for a game without zones.</summary>
        [Key(2)]
        public int Zone;
    }

    [MessagePackObject]
    public struct Ping
    {
        /// <summary>The client's own clock, echoed back so it can measure the round trip.</summary>
        [Key(0)]
        public long ClientTime;
    }

    [MessagePackObject]
    public struct Pong
    {
        [Key(0)]
        public long ClientTime;
    }
}
