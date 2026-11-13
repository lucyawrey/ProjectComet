using MessagePack;

namespace ShapeLand.Shared.Messages
{
    [MessagePackObject]
    public struct JoinRequest
    {
        [Key(0)]
        public string Ticket;

        [Key(1)]
        public ulong ContentFingerprint;
    }

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

        [Key(4)]
        public ulong ContentFingerprint;

        [Key(5)]
        public bool Bot;
    }

    public enum JoinRejection : byte
    {
        DifferentContent = 1,

        BadTicket = 2,

        ExpiredTicket = 3,

        UsedTicket = 4,

        WrongServer = 5,

        NoCharacter = 6,

        Busy = 7,

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

    [MessagePackObject]
    public struct NameChanged { }

    public enum DropReason : byte
    {
        Replaced = 1,

        Removed = 2,
    }

    [MessagePackObject]
    public struct Dropped
    {
        [Key(0)]
        public DropReason Reason;
    }

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
