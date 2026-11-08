using MessagePack;

namespace ShapeLand.Shared.Messages
{
    [MessagePackObject]
    public struct ChatSend
    {
        [Key(0)]
        public string Text;
    }

    /// <summary>
    /// Includes the sender, so everyone sees the same order. Carries the sender's name: chat is server-wide, so they
    /// may be in another zone.
    /// </summary>
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
        /// <summary>
        /// Empty after trimming, or longer than <see cref="ShapeLand.Shared.World.ShapeLandRules.MaxChatLength"/>.
        /// </summary>
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
