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

        /// <summary>
        /// The last <see cref="PositionCorrection.Sequence"/> the client applied. The server ignores reports
        /// sent before its latest correction arrived.
        /// </summary>
        [Key(7)] public uint CorrectionSequence;
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

        /// <summary>
        /// The server tick this state is from: for a player, when it sent the report (bounded by the server),
        /// so clients interpolate on the sender's timing rather than on when the report happened to arrive.
        /// </summary>
        [Key(5)] public uint Tick;

        /// <summary>
        /// Velocity in meters per second, as the mover reported it: clients that run out of states keep drawing
        /// the entity along it until the next one arrives (dead reckoning).
        /// </summary>
        [Key(6)] public float VelocityX;
        [Key(7)] public float VelocityY;
        [Key(8)] public float VelocityZ;
    }

    /// <summary>Server event: an entity is no longer visible to this client (left, or out of view).</summary>
    [MessagePackObject]
    public struct EntityDespawn
    {
        [Key(0)] public uint EntityId;
    }

    /// <summary>Why the server moved a player.</summary>
    public enum CorrectionReason : byte
    {
        /// <summary>The move broke a movement rule (too fast, too high); the player is put back.</summary>
        SnapBack = 1,

        /// <summary>The player fell below the world and is put back on safe ground.</summary>
        Respawn = 2,

        /// <summary>The player was sent somewhere new by the game (a teleporter).</summary>
        Teleport = 3,
    }

    /// <summary>What a position report's check found.</summary>
    public enum MovementVerdict : byte
    {
        Accepted,

        /// <summary>A number that isn't finite (NaN or infinity): never valid, and every comparison with NaN is false.</summary>
        NotFinite,

        /// <summary>Sent before the client applied the latest correction; ignored.</summary>
        Stale,

        /// <summary>Moved further than the speed budget allows.</summary>
        TooFast,

        /// <summary>Higher than gravity allows: above the arc of a jump from the last ground at this point in it.</summary>
        TooHigh,

        /// <summary>The body overlaps something solid: terrain or a placed object.</summary>
        InsideSolid,

        /// <summary>Fell below the world's kill height; respawn.</summary>
        FellOut,
    }

    /// <summary>
    /// Server event: the server overrides the client's own position. The client moves there (blending
    /// a snap-back, fading a respawn) and echoes <see cref="Sequence"/> in its later reports.
    /// </summary>
    [MessagePackObject]
    public struct PositionCorrection
    {
        /// <summary>Increases with each correction to this player.</summary>
        [Key(0)] public uint Sequence;
        [Key(1)] public CorrectionReason Reason;
        [Key(2)] public float X;
        [Key(3)] public float Y;
        [Key(4)] public float Z;
        [Key(5)] public float Facing;

        /// <summary>For a snap-back, the rule the rejected report broke; <see cref="MovementVerdict.Accepted"/> when none did.</summary>
        [Key(6)] public MovementVerdict Verdict;
    }
}
