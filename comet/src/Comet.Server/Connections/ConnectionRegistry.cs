using Comet.Protocol;
using MessagePack;

namespace Comet.Server.Connections;

/// <summary>
/// The open connections. Reading <see cref="Snapshot"/> is lock-free and allocation-free, for the
/// tick thread; adding and removing copy the array, which is rare.
/// </summary>
/// <param name="serializerOptions">
/// How connections encode messages: a game with its own messages passes
/// <see cref="ProtocolSerializer.CreateOptions"/> with its resolver; null means Comet's messages only.
/// </param>
public sealed class ConnectionRegistry(MessagePackSerializerOptions? serializerOptions = null)
{
    private readonly Lock _lock = new();
    private Connection[] _snapshot = [];
    private uint _lastId;

    public NetworkStats Stats { get; } = new();

    public MessagePackSerializerOptions SerializerOptions { get; } = serializerOptions ?? ProtocolSerializer.Options;

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
