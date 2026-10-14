namespace Comet.Client
{
    /// <summary>Limits every client transport keeps.</summary>
    public static class TransportLimits
    {
        /// <summary>How long connecting may take before it fails, in seconds.</summary>
        public const double ConnectTimeoutSeconds = 10;

        /// <summary>
        /// Received frames waiting to be taken, in bytes, before the connection is given up on: they pile up while the
        /// game doesn't run (a hidden browser tab, a paused editor), and would all be handled at once on its return.
        /// </summary>
        public const int MaxQueuedBytes = 8 * 1024 * 1024;
    }
}
