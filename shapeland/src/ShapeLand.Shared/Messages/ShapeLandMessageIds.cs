using Comet.Protocol;

namespace ShapeLand.Shared.Messages
{
    /// <summary>ShapeLand's message IDs, after Comet's (<see cref="MessageIds.FirstGameMessage"/>).</summary>
    public static class ShapeLandMessageIds
    {
        // Joining
        public const ushort JoinRequest = MessageIds.FirstGameMessage;
        public const ushort JoinRejected = MessageIds.FirstGameMessage + 1;
        public const ushort PlayerSpawn = MessageIds.FirstGameMessage + 2;

        // Chat
        public const ushort ChatSend = MessageIds.FirstGameMessage + 10;
        public const ushort ChatMessage = MessageIds.FirstGameMessage + 11;
        public const ushort ChatRejected = MessageIds.FirstGameMessage + 12;

        // Spawn points
        public const ushort SpawnSet = MessageIds.FirstGameMessage + 20;
        public const ushort ReturnToSpawn = MessageIds.FirstGameMessage + 21;
    }
}
