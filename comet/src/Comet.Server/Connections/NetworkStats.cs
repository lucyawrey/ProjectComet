namespace Comet.Server.Connections;

/// <summary>Running totals across all connections. Safe to update and read from any thread.</summary>
public sealed class NetworkStats
{
    private long _bytesSent;
    private long _bytesReceived;
    private long _framesSent;
    private long _framesReceived;
    private long _flushesSkipped;
    private long _stateReplaced;
    private long _protocolErrors;
    private long _slowClientsDropped;
    private long _connectionsRefused;

    public long BytesSent => Interlocked.Read(ref _bytesSent);

    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    public long FramesSent => Interlocked.Read(ref _framesSent);

    public long FramesReceived => Interlocked.Read(ref _framesReceived);

    /// <summary>Ticks where a connection's previous frame was still sending, so its flush was skipped.</summary>
    public long FlushesSkipped => Interlocked.Read(ref _flushesSkipped);

    /// <summary>State messages replaced by a newer one before they were sent.</summary>
    public long StateReplaced => Interlocked.Read(ref _stateReplaced);

    public long ProtocolErrors => Interlocked.Read(ref _protocolErrors);

    /// <summary>Clients dropped because they stopped reading (their unsent events piled up).</summary>
    public long SlowClientsDropped => Interlocked.Read(ref _slowClientsDropped);

    /// <summary>Connections refused for passing <see cref="ConnectionLimits"/>.</summary>
    public long ConnectionsRefused => Interlocked.Read(ref _connectionsRefused);

    internal void AddSent(int bytes)
    {
        Interlocked.Add(ref _bytesSent, bytes);
        Interlocked.Increment(ref _framesSent);
    }

    internal void AddReceived(int bytes)
    {
        Interlocked.Add(ref _bytesReceived, bytes);
        Interlocked.Increment(ref _framesReceived);
    }

    internal void AddFlushSkipped() => Interlocked.Increment(ref _flushesSkipped);

    internal void AddStateReplaced(int count) => Interlocked.Add(ref _stateReplaced, count);

    internal void AddProtocolError() => Interlocked.Increment(ref _protocolErrors);

    internal void AddSlowClientDropped() => Interlocked.Increment(ref _slowClientsDropped);

    internal void AddConnectionRefused() => Interlocked.Increment(ref _connectionsRefused);
}
