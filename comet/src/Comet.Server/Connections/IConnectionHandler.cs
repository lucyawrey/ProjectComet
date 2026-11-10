namespace Comet.Server.Connections;

/// <summary>Called on the connection's receive flow, not the tick thread: hand anything the simulation needs to the tick thread safely.</summary>
public interface IConnectionHandler
{
    void OnConnected(Connection connection);

    /// <param name="clientTick">The client's estimate of the server tick, from the frame header.</param>
    /// <param name="payload">The MessagePack payload; only valid during this call.</param>
    void OnMessage(Connection connection, uint clientTick, ushort messageId, ReadOnlyMemory<byte> payload);

    void OnDisconnected(Connection connection);
}
