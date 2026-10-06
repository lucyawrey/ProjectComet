using MessagePack;

namespace Comet.Protocol.Messages
{
    /// <summary>
    /// Client input: the client's own position, which the server validates.
    /// Sent about 15 times a second while moving, and not at all while standing still.
    /// The frame header carries the client's estimate of the server tick.
    /// </summary>
    [MessagePackObject]
    public struct PositionReport
    {
        [Key(0)] public float X;
        [Key(1)] public float Y;
        [Key(2)] public float Z;
        [Key(3)] public float VelocityX;
        [Key(4)] public float VelocityY;
        [Key(5)] public float VelocityZ;

        /// <summary>Facing as a yaw angle in radians.</summary>
        [Key(6)] public float Facing;
    }

    /// <summary>
    /// Server state: where an entity is. Latest-only: a newer update for the same entity
    /// replaces an unsent older one.
    /// </summary>
    [MessagePackObject]
    public struct EntityState
    {
        [Key(0)] public uint EntityId;
        [Key(1)] public float X;
        [Key(2)] public float Y;
        [Key(3)] public float Z;

        /// <summary>Facing as a yaw angle in radians.</summary>
        [Key(4)] public float Facing;
    }
}
