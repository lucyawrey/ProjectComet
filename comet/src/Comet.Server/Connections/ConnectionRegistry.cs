using System.Net;
using System.Net.Sockets;
using Comet.Protocol;
using MessagePack;

namespace Comet.Server.Connections;

/// <summary><see cref="Snapshot"/> is lock-free and allocation-free for the tick thread; adding and removing copy the array, which is rare.</summary>
/// <param name="serializerOptions">A game with its own messages passes <see cref="ProtocolSerializer.CreateOptions"/>; null is Comet's only.</param>
public sealed class ConnectionRegistry(MessagePackSerializerOptions? serializerOptions = null)
{
    private readonly Lock _lock = new();
    private Connection[] _snapshot = [];
    private uint _lastId;
    private readonly Dictionary<IPAddress, int> _openByAddress = [];
    private int _open;

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

    internal bool TryReserve(IPAddress? address, ConnectionLimits limits)
    {
        var key = AddressKey(address);
        lock (_lock)
        {
            var fromAddress = _openByAddress.GetValueOrDefault(key);
            if (_open >= limits.MaxConnections || fromAddress >= limits.MaxConnectionsPerAddress)
            {
                return false;
            }

            _open++;
            _openByAddress[key] = fromAddress + 1;
            return true;
        }
    }

    internal void Release(IPAddress? address)
    {
        var key = AddressKey(address);
        lock (_lock)
        {
            _open--;
            if (--_openByAddress[key] == 0)
            {
                _openByAddress.Remove(key);
            }
        }
    }

    private static IPAddress AddressKey(IPAddress? address)
    {
        if (address is null)
        {
            return IPAddress.None;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address;
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes);
    }
}
