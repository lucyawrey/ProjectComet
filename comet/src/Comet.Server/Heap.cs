using System.Runtime;

namespace Comet.Server;

/// <summary>Garbage-collector housekeeping for game servers.</summary>
public static class Heap
{
    /// <summary>
    /// Runs one full, compacting collection, so long-lived objects made at startup (hosting, loaded
    /// content and zones) settle in the oldest generation now, rather than being promoted later in
    /// one long pause while players are connected. Call it once startup work is done, before players
    /// arrive.
    /// </summary>
    public static void Settle()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }
}
