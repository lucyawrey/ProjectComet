using MessagePack;

namespace ShapeLand.Shared.Messages
{
    [MessagePackObject]
    public struct ChatSend
    {
        [Key(0)]
        public string Text;
    }

    [MessagePackObject]
    public struct ChatMessage
    {
        [Key(0)]
        public uint EntityId;

        [Key(1)]
        public string Text;

        [Key(2)]
        public string Name;
    }

    public enum ChatRejection : byte
    {
        InvalidText = 1,

        TooFast = 2,
    }

    [MessagePackObject]
    public struct ChatRejected
    {
        [Key(0)]
        public ChatRejection Reason;
    }
}
