using MessagePack;

namespace Comet.Protocol.Messages
{
    /// <summary>
    /// Client input: the client's own position, which the server validates. Sent about 15 times a second while moving;
    /// the frame header carries the client's estimate of the server tick.
    /// </summary>
    [MessagePackObject]
    public struct PositionReport
    {
        [Key(0)]
        public float X;

        [Key(1)]
        public float Y;

        [Key(2)]
        public float Z;

        [Key(3)]
        public float VelocityX;

        [Key(4)]
        public float VelocityY;

        [Key(5)]
        public float VelocityZ;

        /// <summary>Facing as a yaw angle in radians.</summary>
        [Key(6)]
        public float Facing;

        [Key(7)]
        public uint CorrectionSequence;
    }

    /// <summary>
    /// Server state: where an entity is. Latest-only: a newer update for the same entity
    /// replaces an unsent older one.
    /// </summary>
    [MessagePackObject]
    public struct EntityState
    {
        [Key(0)]
        public uint EntityId;

        [Key(1)]
        public float X;

        [Key(2)]
        public float Y;

        [Key(3)]
        public float Z;

        [Key(4)]
        public float Facing;

        [Key(5)]
        public uint Tick;

        /// <summary>
        /// Velocity in meters per second, as the mover reported it: clients that run out of states keep drawing
        /// the entity along it until the next one arrives (dead reckoning).
        /// </summary>
        [Key(6)]
        public float VelocityX;

        [Key(7)]
        public float VelocityY;

        [Key(8)]
        public float VelocityZ;
    }

    [MessagePackObject]
    public struct EntityDespawn
    {
        [Key(0)]
        public uint EntityId;
    }

    public enum CorrectionReason : byte
    {
        SnapBack = 1,

        Respawn = 2,

        Teleport = 3,
    }

    public enum MovementVerdict : byte
    {
        Accepted,

        NotFinite,

        Stale,

        TooFast,

        TooHigh,

        InsideSolid,

        FellOut,
    }

    [MessagePackObject]
    public struct PositionCorrection
    {
        /// <summary>Increases with each correction to this player.</summary>
        [Key(0)]
        public uint Sequence;

        [Key(1)]
        public CorrectionReason Reason;

        [Key(2)]
        public float X;

        [Key(3)]
        public float Y;

        [Key(4)]
        public float Z;

        [Key(5)]
        public float Facing;

        /// <summary>For a snap-back, the rule the rejected report broke; <see cref="MovementVerdict.Accepted"/> when none did.</summary>
        [Key(6)]
        public MovementVerdict Verdict;
    }
}
