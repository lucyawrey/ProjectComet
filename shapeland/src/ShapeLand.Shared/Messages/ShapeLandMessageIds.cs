using Comet.Protocol;

namespace ShapeLand.Shared.Messages
{
    public static class ShapeLandMessageIds
    {
        public const ushort JoinRequest = MessageIds.FirstGameMessage;
        public const ushort JoinRejected = MessageIds.FirstGameMessage + 1;
        public const ushort PlayerSpawn = MessageIds.FirstGameMessage + 2;
        public const ushort LookChange = MessageIds.FirstGameMessage + 3;
        public const ushort LookRejected = MessageIds.FirstGameMessage + 4;
        public const ushort NameChanged = MessageIds.FirstGameMessage + 5;
        public const ushort Dropped = MessageIds.FirstGameMessage + 6;
        public const ushort DevJoinRequest = MessageIds.FirstGameMessage + 7;

        public const ushort ChatSend = MessageIds.FirstGameMessage + 10;
        public const ushort ChatMessage = MessageIds.FirstGameMessage + 11;
        public const ushort ChatRejected = MessageIds.FirstGameMessage + 12;

        public const ushort SpawnSet = MessageIds.FirstGameMessage + 20;
        public const ushort ReturnToSpawn = MessageIds.FirstGameMessage + 21;

        public const ushort ZoneEntered = MessageIds.FirstGameMessage + 30;
        public const ushort PlacementsFlipped = MessageIds.FirstGameMessage + 31;
    }
}
