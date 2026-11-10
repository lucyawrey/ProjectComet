namespace Comet.Protocol
{
    /// <summary>Network contract: Comet uses 1 to 63 and games start at <see cref="FirstGameMessage"/>, so IDs up to 127 take one byte.</summary>
    public static class MessageIds
    {
        public const ushort FirstGameMessage = 64;

        public const ushort Welcome = 1;
        public const ushort Ping = 2;
        public const ushort Pong = 3;

        public const ushort PositionReport = 10;
        public const ushort EntityState = 11;
        public const ushort EntityDespawn = 12;
        public const ushort PositionCorrection = 13;
    }
}
