using MessagePack;

namespace ShapeLand.Shared.Messages
{
    /// <summary>
    /// Sent once after connecting; after a rejection, the client fetches a new ticket and connects again.
    /// </summary>
    [MessagePackObject]
    public struct JoinRequest
    {
        /// <summary>Good once, for this server, for a minute.</summary>
        [Key(0)]
        public string Ticket;

        /// <summary>The client's <c>ShapeLandContent.Fingerprint</c>, which must match the server's.</summary>
        [Key(1)]
        public ulong ContentFingerprint;
    }

    /// <summary>
    /// Joins by name without a ticket. Only a Debug game server started for development takes it; any other answers
    /// <see cref="JoinRejection.AccountsNeeded"/>.
    /// </summary>
    [MessagePackObject]
    public struct DevJoinRequest
    {
        [Key(0)]
        public string Name;

        [Key(1)]
        public int Shape;

        [Key(2)]
        public int Color;

        [Key(3)]
        public int EyeColor;

        /// <summary>The client's <c>ShapeLandContent.Fingerprint</c>, which must match the server's.</summary>
        [Key(4)]
        public ulong ContentFingerprint;

        [Key(5)]
        public bool Bot;
    }

    public enum JoinRejection : byte
    {
        DifferentContent = 1,

        BadTicket = 2,

        /// <summary>The ticket expired or is from before the server started: fetch a new one.</summary>
        ExpiredTicket = 3,

        /// <summary>The ticket was used already: fetch a new one.</summary>
        UsedTicket = 4,

        WrongServer = 5,

        NoCharacter = 6,

        /// <summary>The server couldn't load the character in time: try again soon.</summary>
        Busy = 7,

        /// <summary>A newer join for the same character came in while this one was loading.</summary>
        Replaced = 8,

        AccountsNeeded = 9,

        BadName = 10,

        NameOnline = 11,

        NameTaken = 12,
    }

    [MessagePackObject]
    public struct JoinRejected
    {
        [Key(0)]
        public JoinRejection Reason;
    }

    [MessagePackObject]
    public struct LookChange
    {
        [Key(0)]
        public int Shape;

        [Key(1)]
        public int Color;

        [Key(2)]
        public int EyeColor;
    }

    public enum LookRejection : byte
    {
        UnknownShape = 1,

        UnavailableColor = 2,

        TooFast = 3,
    }

    [MessagePackObject]
    public struct LookRejected
    {
        [Key(0)]
        public LookRejection Reason;
    }

    /// <summary>Carries nothing: the server reads the new name itself.</summary>
    [MessagePackObject]
    public struct NameChanged { }

    public enum DropReason : byte
    {
        Replaced = 1,

        /// <summary>
        /// Clients don't reconnect on their own after this or <see cref="Replaced"/>, so two tabs can't keep taking the
        /// character from each other.
        /// </summary>
        Removed = 2,
    }

    [MessagePackObject]
    public struct Dropped
    {
        [Key(0)]
        public DropReason Reason;
    }

    /// <summary>
    /// The client's own player comes first, right after Comet's <c>Welcome</c>. Another spawn for a player already in
    /// view gives their new look or name, where they stand.
    /// </summary>
    [MessagePackObject]
    public struct PlayerSpawn
    {
        [Key(0)]
        public uint EntityId;

        [Key(1)]
        public string Name;

        [Key(2)]
        public int Shape;

        [Key(3)]
        public int Color;

        [Key(4)]
        public float X;

        [Key(5)]
        public float Y;

        [Key(6)]
        public float Z;

        [Key(7)]
        public float Facing;

        [Key(8)]
        public int EyeColor;
    }
}
