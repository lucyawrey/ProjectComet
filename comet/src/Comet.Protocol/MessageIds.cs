namespace Comet.Protocol
{
    /// <summary>
    /// Numeric message IDs. They are part of the network contract: change them only together with every client.
    /// Comet uses 1 to 63; games number their own messages from <see cref="FirstGameMessage"/>, so every ID
    /// still fits in one varint byte up to 127.
    /// </summary>
    public static class MessageIds
    {
        public const ushort FirstGameMessage = 64;

        // Connection
        public const ushort Welcome = 1;
        public const ushort Ping = 2;
        public const ushort Pong = 3;

        // Movement
        public const ushort PositionReport = 10;
        public const ushort EntityState = 11;
        public const ushort EntityDespawn = 12;
        public const ushort PositionCorrection = 13;
    }
}
