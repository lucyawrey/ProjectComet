using System.Diagnostics.Tracing;

namespace Comet.Server.Diagnostics;

/// <summary>
/// Times every garbage-collection pause (all managed threads suspended) from the runtime's own
/// events, from GCSuspendEEBegin to GCRestartEEEnd. This includes the short pauses inside
/// background collections, whose total collection time is mostly concurrent and not a pause.
/// <see cref="PauseRecorded"/> is raised on the runtime's event thread, shortly after the pause.
/// </summary>
public sealed class GcPauseListener : EventListener
{
    private const string RuntimeSource = "Microsoft-Windows-DotNETRuntime";
    private const EventKeywords GcKeyword = (EventKeywords)0x1;
    private const int GCRestartEEEnd = 3;
    private const int GCSuspendEEBegin = 9;

    private DateTime? _suspendStart;

    public event Action<TimeSpan>? PauseRecorded;

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
                break;
            case GCRestartEEEnd when _suspendStart is { } start:
                _suspendStart = null;
                PauseRecorded?.Invoke(eventData.TimeStamp - start);
                break;
        }
    }
}
