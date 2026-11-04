using MessagePack;

namespace ShapeLand.Shared.Messages
{
    /// <summary>
    /// Client input: join the game with a join ticket from the Login server, which names the player's character. Sent
    /// once after connecting; after a rejection, the client fetches a new ticket and connects again.
    /// </summary>
    [MessagePackObject]
    public struct JoinRequest
    {
        /// <summary>The join ticket (a signed JWT), good once, for this server, for a minute.</summary>
        [Key(0)] public string Ticket;

        /// <summary>The client's <c>ShapeLandContent.Fingerprint</c>, which must match the server's.</summary>
        [Key(1)] public ulong ContentFingerprint;
    }

    /// <summary>
    /// Client input, in the Unity editor with accounts off: join the editor's own game server without a Login server or
    /// ticket, as the character with this name, made with this look if it's new (a returning name keeps its look and
    /// spot). Only a Debug game server started for development takes it; any other answers
    /// <see cref="JoinRejection.AccountsNeeded"/>.
    /// </summary>
    [MessagePackObject]
    public struct DevJoinRequest
    {
        [Key(0)] public string Name;

        /// <summary>The shape's content number.</summary>
        [Key(1)] public int Shape;

        /// <summary>The body color's content number.</summary>
        [Key(2)] public int Color;

        /// <summary>The eye color's content number.</summary>
        [Key(3)] public int EyeColor;

        /// <summary>The client's <c>ShapeLandContent.Fingerprint</c>, which must match the server's.</summary>
        [Key(4)] public ulong ContentFingerprint;

        /// <summary>Whether it's a bot's join: bots' characters are kept apart from people's.</summary>
        [Key(5)] public bool Bot;
    }

    /// <summary>Why a join was refused.</summary>
    public enum JoinRejection : byte
    {
        /// <summary>The client's content isn't the server's (another build of the game, or content rebuilt since).</summary>
        DifferentContent = 1,

        /// <summary>The ticket is malformed, forged, or for someone else's character.</summary>
        BadTicket = 2,

        /// <summary>The ticket expired or is from before the server started: fetch a new one.</summary>
        ExpiredTicket = 3,

        /// <summary>The ticket was used already: fetch a new one.</summary>
        UsedTicket = 4,

        /// <summary>The ticket is for another game server.</summary>
        WrongServer = 5,

        /// <summary>The character is gone (deleted).</summary>
        NoCharacter = 6,

        /// <summary>The server couldn't load the character in time: try again soon.</summary>
        Busy = 7,

        /// <summary>A newer join for the same character came in while this one was loading.</summary>
        Replaced = 8,

        /// <summary>A join without a ticket (<see cref="DevJoinRequest"/>) to a server that needs accounts.</summary>
        AccountsNeeded = 9,

        /// <summary>A <see cref="DevJoinRequest"/>'s name breaks the name rules.</summary>
        BadName = 10,

        /// <summary>A <see cref="DevJoinRequest"/>'s name is playing on the server already.</summary>
        NameOnline = 11,

        /// <summary>A <see cref="DevJoinRequest"/>'s name is a bot's, or a bot's is a person's.</summary>
        NameTaken = 12,
    }

    /// <summary>Server event: the join was refused. The connection stays open but can't play.</summary>
    [MessagePackObject]
    public struct JoinRejected
    {
        [Key(0)] public JoinRejection Reason;
    }

    /// <summary>
    /// Client input, once joined: change the look. The player stays where they are and everyone gets a new
    /// <see cref="PlayerSpawn"/>; the look is saved at once.
    /// </summary>
    [MessagePackObject]
    public struct LookChange
    {
        /// <summary>The shape's content number.</summary>
        [Key(0)] public int Shape;

        /// <summary>The body color's content number.</summary>
        [Key(1)] public int Color;

        /// <summary>The eye color's content number.</summary>
        [Key(2)] public int EyeColor;
    }

    /// <summary>Why a look change was refused.</summary>
    public enum LookRejection : byte
    {
        UnknownShape = 1,

        /// <summary>A body or eye color the player may not use.</summary>
        UnavailableColor = 2,

        /// <summary>Changes came too fast.</summary>
        TooFast = 3,
    }

    /// <summary>Server event: the look change was refused; the old look stays.</summary>
    [MessagePackObject]
    public struct LookRejected
    {
        [Key(0)] public LookRejection Reason;
    }

    /// <summary>
    /// Client input, once joined: the player renamed their account at the Login server, so the server reloads the
    /// name and everyone gets a new <see cref="PlayerSpawn"/>. Carries nothing: the server reads the name itself.
    /// </summary>
    [MessagePackObject]
    public struct NameChanged
    {
    }

    /// <summary>Why the server dropped a joined player.</summary>
    public enum DropReason : byte
    {
        /// <summary>The same character joined again, from another tab or device, which plays on instead.</summary>
        Replaced = 1,

        /// <summary>
        /// The account was banned or deleted, or the character was taken by another server. Clients don't reconnect
        /// on their own after this or <see cref="Replaced"/>, so two tabs can't keep taking the character from each other.
        /// </summary>
        Removed = 2,
    }

    /// <summary>Server event: the player was taken out of the game, and the connection closes.</summary>
    [MessagePackObject]
    public struct Dropped
    {
        [Key(0)] public DropReason Reason;
    }

    /// <summary>
    /// Server event: a player is in view, with everything needed to draw them. The client's own player comes first,
    /// right after Comet's <c>Welcome</c>; later positions arrive as <c>EntityState</c>, and leaving as <c>EntityDespawn</c>.
    /// Another spawn for a player already in view gives their new look or name, where they stand.
    /// </summary>
    [MessagePackObject]
    public struct PlayerSpawn
    {
        [Key(0)] public uint EntityId;
        [Key(1)] public string Name;

        /// <summary>The shape's content number.</summary>
        [Key(2)] public int Shape;

        /// <summary>The body color's content number.</summary>
        [Key(3)] public int Color;

        [Key(4)] public float X;
        [Key(5)] public float Y;
        [Key(6)] public float Z;
        [Key(7)] public float Facing;

        /// <summary>The eye color's content number.</summary>
        [Key(8)] public int EyeColor;
    }
}
