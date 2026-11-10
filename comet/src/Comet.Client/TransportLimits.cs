namespace Comet.Client
{
    public static class TransportLimits
    {
        public const double ConnectTimeoutSeconds = 10;

        public const int MaxQueuedBytes = 8 * 1024 * 1024;
    }
}
