using MessagePack;

namespace ShapeLand.Shared.Messages
{
    /// <summary>
    /// Client input: join the game with a name, a shape and colors. Sent after connecting, again after a rejection,
    /// and once joined to change any of them: the player stays where they are and everyone gets a new <see cref="PlayerSpawn"/>.
    /// </summary>
    [MessagePackObject]
    public struct JoinRequest
    {
        [Key(0)] public string Name;

        /// <summary>The shape's content number.</summary>
        [Key(1)] public int Shape;

        /// <summary>The body color as 0xRRGGBB, one the player may use.</summary>
        [Key(2)] public uint Color;

        /// <summary>The eye color as 0xRRGGBB, one the player may use.</summary>
        [Key(3)] public uint EyeColor;

        /// <summary>The client's <c>ShapeLandContent.Fingerprint</c>, which must match the server's.</summary>
        [Key(4)] public ulong ContentFingerprint;
    }

    /// <summary>Why a join was refused.</summary>
    public enum JoinRejection : byte
    {
        /// <summary>Empty, too long, or with characters other than letters, digits, spaces, '-' and '_'.</summary>
        InvalidName = 1,

        /// <summary>Someone online already has this name (names are compared ignoring case).</summary>
        NameTaken = 2,

        UnknownShape = 3,

        /// <summary>A body or eye color the player may not use (not in the starting sets).</summary>
        UnavailableColor = 5,

        /// <summary>The client's content isn't the server's (another build of the game, or content rebuilt since).</summary>
        DifferentContent = 6,
    }

    /// <summary>Server event: the join or change was refused; the client may try again.</summary>
    [MessagePackObject]
    public struct JoinRejected
    {
        [Key(0)] public JoinRejection Reason;
    }

    /// <summary>
    /// Server event: a player is in view, with everything needed to draw them. The client's own player comes first,
    /// right after Comet's <c>Welcome</c>; later positions arrive as <c>EntityState</c>, and leaving as <c>EntityDespawn</c>.
    /// Another spawn for a player already in view gives their new look, where they stand.
    /// </summary>
    [MessagePackObject]
    public struct PlayerSpawn
    {
        [Key(0)] public uint EntityId;
        [Key(1)] public string Name;

        /// <summary>The shape's content number.</summary>
        [Key(2)] public int Shape;

        /// <summary>The player's body color as 0xRRGGBB.</summary>
        [Key(3)] public uint Color;

        [Key(4)] public float X;
        [Key(5)] public float Y;
        [Key(6)] public float Z;
        [Key(7)] public float Facing;

        /// <summary>The player's eye color as 0xRRGGBB.</summary>
        [Key(8)] public uint EyeColor;
    }
}
