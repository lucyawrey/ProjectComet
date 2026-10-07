using MessagePack;

namespace ShapeLand.Shared.Messages
{
    /// <summary>Client input: join the game with a name and a shape. Sent once after connecting, or again after a rejection.</summary>
    [MessagePackObject]
    public struct JoinRequest
    {
        [Key(0)] public string Name;

        /// <summary>The shape's content number.</summary>
        [Key(1)] public int Shape;
    }

    /// <summary>Why a join was refused.</summary>
    public enum JoinRejection : byte
    {
        /// <summary>Empty, too long, or with characters other than letters, digits, spaces, '-' and '_'.</summary>
        InvalidName = 1,

        /// <summary>Someone online already has this name (names are compared ignoring case).</summary>
        NameTaken = 2,

        UnknownShape = 3,

        /// <summary>This connection has already joined.</summary>
        AlreadyJoined = 4,
    }

    /// <summary>Server event: the join was refused; the client may try again.</summary>
    [MessagePackObject]
    public struct JoinRejected
    {
        [Key(0)] public JoinRejection Reason;
    }

    /// <summary>
    /// Server event: a player is in view, with everything needed to draw them. The client's own player comes first,
    /// right after Comet's <c>Welcome</c>; later positions arrive as <c>EntityState</c>, and leaving as <c>EntityDespawn</c>.
    /// </summary>
    [MessagePackObject]
    public struct PlayerSpawn
    {
        [Key(0)] public uint EntityId;
        [Key(1)] public string Name;

        /// <summary>The shape's content number.</summary>
        [Key(2)] public int Shape;

        /// <summary>The player's colour as 0xRRGGBB.</summary>
        [Key(3)] public uint Colour;

        [Key(4)] public float X;
        [Key(5)] public float Y;
        [Key(6)] public float Z;
        [Key(7)] public float Facing;
    }
}
