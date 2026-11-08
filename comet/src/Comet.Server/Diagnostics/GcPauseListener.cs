using System.Diagnostics.Tracing;

namespace Comet.Server.Diagnostics;

/// <summary>
/// Times each garbage-collection pause (all managed threads suspended), from GCSuspendEEBegin to GCRestartEEEnd, including the short
/// pauses inside background collections. <see cref="PauseRecorded"/> is raised on the runtime's event thread, shortly after the pause.
/// </summary>
public sealed class GcPauseListener : EventListener
{
    private const string RuntimeSource = "Microsoft-Windows-DotNETRuntime";
    private const EventKeywords GcKeyword = (EventKeywords)0x1;
    private const int GCStart = 1;
    private const int GCHeapStats = 4;
    private const int GCRestartEEEnd = 3;
    private const int GCSuspendEEBegin = 9;

    private DateTime? _suspendStart;
    private GcCollection? _collection;
    private bool _startedInPause;
    private GcHeapStats? _heapStats;

    public event Action<GcPause>? PauseRecorded;

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == RuntimeSource)
        {
            EnableEvents(eventSource, EventLevel.Informational, GcKeyword);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        switch (eventData.EventId)
        {
            case GCSuspendEEBegin:
                _suspendStart = eventData.TimeStamp;
                _startedInPause = false;
                _heapStats = null;
                break;
            case GCStart:
                var collection = new GcCollection(
                    Payload(eventData, "Count"),
                    (int)Payload(eventData, "Depth"),
                    (GcReason)Payload(eventData, "Reason"),
                    (GcType)Payload(eventData, "Type")
                );
                // A background collection starts in the same pause as an ephemeral one; keep the deeper.
                if (!_startedInPause || _collection is not { } previous || collection.Generation > previous.Generation)
                {
                    _collection = collection;
                }
                _startedInPause = _suspendStart is not null;
                break;
            case GCHeapStats:
                _heapStats = new GcHeapStats(
                    Payload(eventData, "GenerationSize0"),
                    Payload(eventData, "GenerationSize1"),
                    Payload(eventData, "GenerationSize2"),
                    Payload(eventData, "GenerationSize3"),
                    Payload(eventData, "TotalPromotedSize0") + Payload(eventData, "TotalPromotedSize1") + Payload(eventData, "TotalPromotedSize2")
                );
                break;
            case GCRestartEEEnd when _suspendStart is { } start:
                _suspendStart = null;
                PauseRecorded?.Invoke(new GcPause(eventData.TimeStamp, eventData.TimeStamp - start, _collection, _startedInPause, _heapStats));
                break;
        }
    }

    private static ulong Payload(EventWrittenEventArgs eventData, string name)
    {
        var index = eventData.PayloadNames?.IndexOf(name) ?? -1;
        return index < 0 ? 0 : Convert.ToUInt64(eventData.Payload![index]);
    }
}

/// <summary>
/// One GC pause. <see cref="Collection"/> is the deepest collection that started in it, or, when <see cref="StartedInPause"/>
/// is false, the latest one before it (such as the background collection this pause belongs to).
/// </summary>
public readonly record struct GcPause(DateTime End, TimeSpan Duration, GcCollection? Collection, bool StartedInPause, GcHeapStats? HeapStats);

public readonly record struct GcCollection(ulong Number, int Generation, GcReason Reason, GcType Type);

public readonly record struct GcHeapStats(ulong Gen0Bytes, ulong Gen1Bytes, ulong Gen2Bytes, ulong LargeObjectBytes, ulong PromotedBytes);

public enum GcReason
{
    AllocSmall = 0,
    Induced = 1,
    LowMemory = 2,
    Empty = 3,
    AllocLarge = 4,
    OutOfSpaceSmallObjectHeap = 5,
    OutOfSpaceLargeObjectHeap = 6,
    InducedNotForced = 7,
    Internal = 8,
    InducedLowMemory = 9,
    InducedCompacting = 10,
    LowMemoryHost = 11,
    PinnedObjectFullGc = 12,
    LowMemoryHostBlocking = 13,
}

public enum GcType
{
    Blocking = 0,
    Background = 1,
    Foreground = 2,
}
