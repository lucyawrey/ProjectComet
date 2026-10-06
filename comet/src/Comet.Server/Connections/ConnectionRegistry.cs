namespace Comet.Server.Connections;

/// <summary>
/// The open connections. Reading <see cref="Snapshot"/> is lock-free and allocation-free, for the
/// tick thread; adding and removing copy the array, which is rare.
/// </summary>
public sealed class ConnectionRegistry
{
    private readonly Lock _lock = new();
    private Connection[] _snapshot = [];
    private uint _lastId;

    public NetworkStats Stats { get; } = new();

    public Connection[] Snapshot => Volatile.Read(ref _snapshot);

    internal uint NextId() => Interlocked.Increment(ref _lastId);

    internal void Add(Connection connection)
    {
        lock (_lock)
        {
            Volatile.Write(ref _snapshot, [.. _snapshot, connection]);
        }
    }

    internal void Remove(Connection connection)
    {
        lock (_lock)
        {
            Volatile.Write(ref _snapshot, Array.FindAll(_snapshot, c => c != connection));
        }
    }
}
