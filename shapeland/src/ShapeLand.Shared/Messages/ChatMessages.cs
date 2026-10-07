using MessagePack;

namespace ShapeLand.Shared.Messages
{
    /// <summary>Client input: say something in the shared channel.</summary>
    [MessagePackObject]
    public struct ChatSend
    {
        [Key(0)] public string Text;
    }

    /// <summary>Server event: someone said something (including the sender, so everyone sees the same order).</summary>
    [MessagePackObject]
    public struct ChatMessage
    {
        [Key(0)] public uint EntityId;
        [Key(1)] public string Text;
    }

    public enum ChatRejection : byte
    {
        /// <summary>Empty after trimming, or longer than <see cref="ShapeLand.Shared.World.ShapeLandRules.MaxChatLength"/>.</summary>
        InvalidText = 1,

        /// <summary>Sent too many messages too quickly.</summary>
        TooFast = 2,
    }

    /// <summary>Server event: the sender's message wasn't delivered.</summary>
    [MessagePackObject]
    public struct ChatRejected
    {
        [Key(0)] public ChatRejection Reason;
    }
}
