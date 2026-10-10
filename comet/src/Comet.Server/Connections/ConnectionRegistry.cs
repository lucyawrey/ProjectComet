using System.Net;
using System.Net.Sockets;
using Comet.Protocol;
using MessagePack;

namespace Comet.Server.Connections;

/// <summary>
/// The open connections. Reading <see cref="Snapshot"/> is lock-free and allocation-free, for the
/// tick thread; adding and removing copy the array, which is rare (the number of connections is capped by
/// <see cref="ConnectionLimits"/>).
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
    private readonly Dictionary<IPAddress, int> _openByAddress = [];
    private int _open;

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

    /// <summary>
    /// Counts a connection from <paramref name="address"/> before its WebSocket is accepted, or returns false if
    /// that would pass <paramref name="limits"/>. Each success is matched by one <see cref="Release"/>.
    /// </summary>
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

    // IPv4 as itself (also when mapped into IPv6); IPv6 by its /64.
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
