namespace Comet.Protocol
{
    /// <summary>
    /// Numeric message IDs. They are part of the network contract: change them only together with every client.
    /// </summary>
    public static class MessageIds
    {
        // Connection
        public const ushort Welcome = 1;
        public const ushort Ping = 2;
        public const ushort Pong = 3;

        // Movement
        public const ushort PositionReport = 10;
        public const ushort EntityState = 11;
    }
}
