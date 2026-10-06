using MessagePack;

namespace Comet.Protocol.Messages
{
    /// <summary>Server event: sent once after connecting.</summary>
    [MessagePackObject]
    public struct Welcome
    {
        /// <summary>The entity this connection controls.</summary>
        [Key(0)] public uint EntityId;

        /// <summary>Server simulation ticks per second.</summary>
        [Key(1)] public int TickRate;
    }

    /// <summary>Client input: asks for a <see cref="Pong"/>, answered immediately rather than on the next tick.</summary>
    [MessagePackObject]
    public struct Ping
    {
        /// <summary>The client's own clock, echoed back so it can measure the round trip.</summary>
        [Key(0)] public long ClientTime;
    }

    /// <summary>Server event: the answer to a <see cref="Ping"/>.</summary>
    [MessagePackObject]
    public struct Pong
    {
        [Key(0)] public long ClientTime;
    }
}
