using Comet.Protocol;
using Comet.Protocol.Framing;
using Comet.Protocol.Messages;
using Comet.Server.Connections;
using Microsoft.Extensions.Options;

namespace StackBench.Server;

/// <summary>
/// No game logic: each connection has one entity that takes its position from the bot's reports.
/// Each tick, every connection gets a budgeted batch of entity updates, chosen round-robin from all
/// entities, mirroring the per-client budget in the netcode design.
/// </summary>
public sealed class BenchGame(IOptions<BenchOptions> options, ConnectionRegistry registry) : IConnectionHandler
{
    private readonly int _updatesPerTick = options.Value.UpdatesPerTick;
    private readonly int _tickRate = options.Value.TickRate;

    public event Action? FirstConnection;

    private int _connected;

    public void OnConnected(Connection connection)
    {
        connection.Tag = new BotEntity(connection.Id);
        connection.SendImmediate(MessageIds.Welcome, new Welcome { EntityId = connection.Id, TickRate = _tickRate });
        if (Interlocked.Exchange(ref _connected, 1) == 0)
        {
            FirstConnection?.Invoke();
        }
    }

    public void OnMessage(Connection connection, uint clientTick, ushort messageId, ReadOnlyMemory<byte> payload)
    {
        if (messageId == MessageIds.PositionReport)
        {
            var report = FrameReader.Decode<PositionReport>(payload);
            ((BotEntity)connection.Tag!).Update(report);
        }
    }

    public void OnDisconnected(Connection connection)
    {
    }

    /// <summary>Tick thread.</summary>
    public void Tick(uint tick)
    {
        var connections = registry.Snapshot;
        var count = connections.Length;
        var updates = Math.Min(_updatesPerTick, count);

        foreach (var connection in connections)
        {
            var viewer = (BotEntity)connection.Tag!;
            for (var i = 0; i < updates; i++)
            {
                var target = (BotEntity)connections[(viewer.Cursor + i) % count].Tag!;
                var state = target.Read();
                connection.EntityStates.Set(state.EntityId, state);
            }
            viewer.Cursor = (viewer.Cursor + updates) % Math.Max(count, 1);
        }

        foreach (var connection in connections)
        {
            connection.Flush(tick);
        }
    }

    private sealed class BotEntity(uint id)
    {
        private readonly Lock _lock = new();
        private EntityState _state = new() { EntityId = id };

        /// <summary>Where this viewer's next round-robin batch starts. Tick thread.</summary>
        public int Cursor { get; set; }

        public void Update(in PositionReport report)
        {
            lock (_lock)
            {
                _state.X = report.X;
                _state.Y = report.Y;
                _state.Z = report.Z;
                _state.Facing = report.Facing;
            }
        }

        public EntityState Read()
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }
}
