using System.Collections.Concurrent;
using System.Numerics;
using Comet.Protocol;
using Comet.Protocol.Framing;
using Comet.Protocol.Messages;
using Comet.Server.Connections;
using Comet.Server.Movement;
using Comet.Simulation;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;

namespace ShapeLand.GameServer;

/// <summary>
/// ShapeLand's phase 0 game: players join with a name and shape, slide and jump on the test island, and chat in one
/// shared channel. Everyone sees everyone (grid interest management comes with the full load test).
/// <para>
/// Connections decode messages on their own receive flows and queue them; the tick thread handles the queue in
/// order, so all game state belongs to the tick thread.
/// </para>
/// </summary>
public sealed class ShapeLandGame(ShapeLandContent content, ConnectionRegistry registry, ILogger<ShapeLandGame> logger) : IConnectionHandler
{
    private readonly CollisionWorld _world = ShapeLandWorld.Create(content);
    private readonly MovementTolerances _tolerances = new();
    private readonly ConcurrentQueue<Input> _inbox = new();
    private readonly Dictionary<Connection, Player> _players = [];
    private readonly Random _random = new();

    public ShapeLandContent Content => content;

    public CollisionWorld World => _world;

    // Receive flows.

    public void OnConnected(Connection connection) => _inbox.Enqueue(new Input(connection, InputKind.Connected));

    public void OnMessage(Connection connection, uint clientTick, ushort messageId, ReadOnlyMemory<byte> payload)
    {
        var options = ShapeLandProtocol.Options;
        Input? input = messageId switch
        {
            MessageIds.PositionReport => new Input(connection, InputKind.Report) { Report = FrameReader.Decode<PositionReport>(payload, options), ClientTick = clientTick },
            ShapeLandMessageIds.JoinRequest => new Input(connection, InputKind.Join) { Join = FrameReader.Decode<JoinRequest>(payload, options) },
            ShapeLandMessageIds.ChatSend => new Input(connection, InputKind.Chat) { Chat = FrameReader.Decode<ChatSend>(payload, options) },
            _ => null, // unknown or not for the server: ignored, so older servers tolerate newer clients
        };
        if (input is not null)
        {
            _inbox.Enqueue(input);
        }
    }

    public void OnDisconnected(Connection connection) => _inbox.Enqueue(new Input(connection, InputKind.Disconnected));

    // Tick thread.

    public void Tick(uint tick)
    {
        while (_inbox.TryDequeue(out var input))
        {
            switch (input.Kind)
            {
                case InputKind.Connected:
                    _players[input.Connection] = new Player(input.Connection, ShapeLandRules.TickRate);
                    break;
                case InputKind.Join:
                    Join(_players[input.Connection], input.Join, tick);
                    break;
                case InputKind.Report:
                    Move(_players[input.Connection], input.Report, input.ClientTick, tick);
                    break;
                case InputKind.Chat:
                    Chat(_players[input.Connection], input.Chat, tick);
                    break;
                case InputKind.Disconnected:
                    Leave(input.Connection);
                    break;
            }
        }

        foreach (var mover in _players.Values)
        {
            if (!mover.Moved)
            {
                continue;
            }

            mover.Moved = false;
            var state = mover.State();
            foreach (var viewer in _players.Values)
            {
                if (viewer.Joined && viewer != mover)
                {
                    viewer.Connection.EntityStates.Set(mover.EntityId, state);
                }
            }
        }

        foreach (var connection in registry.Snapshot)
        {
            connection.Flush(tick);
        }
    }

    private void Join(Player player, JoinRequest request, uint tick)
    {
        if (player.Joined)
        {
            Reject(JoinRejection.AlreadyJoined);
            return;
        }

        if (!ShapeLandRules.TryNormaliseName(request.Name, out var name))
        {
            Reject(JoinRejection.InvalidName);
            return;
        }

        if (_players.Values.Any(p => p.Joined && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            Reject(JoinRejection.NameTaken);
            return;
        }

        if (!content.Shapes.TryGet(request.Shape, out var shape))
        {
            Reject(JoinRejection.UnknownShape);
            return;
        }

        player.Joined = true;
        player.Name = name;
        player.Shape = shape;
        player.Colour = ShapeLandRules.Colours[_random.Next(ShapeLandRules.Colours.Length)];
        player.Validator = new MovementValidator(_world, ShapeLandWorld.Rules, _tolerances, ShapeLandRules.TickRate);
        player.Validator.Reset(ShapeLandWorld.SpawnPoint(_world), (float)(_random.NextDouble() * Math.Tau), tick);
        player.StateTick = player.Stamp.StampServer(tick);

        var connection = player.Connection;
        connection.SendEvent(MessageIds.Welcome, new Welcome { EntityId = player.EntityId, TickRate = ShapeLandRules.TickRate });
        connection.SendEvent(ShapeLandMessageIds.PlayerSpawn, player.Spawn());
        var spawn = player.Spawn();
        foreach (var other in _players.Values)
        {
            if (other.Joined && other != player)
            {
                connection.SendEvent(ShapeLandMessageIds.PlayerSpawn, other.Spawn());
                other.Connection.SendEvent(ShapeLandMessageIds.PlayerSpawn, spawn);
            }
        }

        logger.LogInformation("{Name} joined as a {Shape} ({Players} online)", name, shape.DisplayName, _players.Values.Count(p => p.Joined));

        void Reject(JoinRejection reason) =>
            player.Connection.SendEvent(ShapeLandMessageIds.JoinRejected, new JoinRejected { Reason = reason });
    }

    private void Move(Player player, PositionReport report, uint clientTick, uint tick)
    {
        if (!player.Joined)
        {
            return;
        }

        var verdict = player.Validator.Check(report, tick, player.Shape.MaxSpeed, player.Shape.JumpVelocity);
        switch (verdict)
        {
            case MovementVerdict.Accepted:
                player.StateTick = player.Stamp.Stamp(clientTick, tick);
                player.Moved = true;
                break;
            case MovementVerdict.Stale:
                break;
            case MovementVerdict.FellOut:
                player.Connection.SendEvent(MessageIds.PositionCorrection, player.Validator.Correct(CorrectionReason.Respawn, tick));
                player.StateTick = player.Stamp.StampServer(tick);
                player.Moved = true;
                break;
            default:
                // Logged for moderation, never auto-banned (netcode.md): the 1st, 10th, 100th… violation, so a
                // persistent cheater doesn't flood the log.
                if (IsPowerOfTen(player.Validator.Violations))
                {
                    logger.LogWarning(
                        "Movement violation by {Name}: {Verdict} at ({X:F1}, {Y:F1}, {Z:F1}), {Count} so far",
                        player.Name, verdict, report.X, report.Y, report.Z, player.Validator.Violations);
                }

                player.Connection.SendEvent(MessageIds.PositionCorrection, player.Validator.Correct(CorrectionReason.SnapBack, tick));
                break;
        }
    }

    private static bool IsPowerOfTen(int n)
    {
        while (n >= 10 && n % 10 == 0)
        {
            n /= 10;
        }

        return n == 1;
    }

    private void Chat(Player player, ChatSend send, uint tick)
    {
        if (!player.Joined)
        {
            return;
        }

        if (!ShapeLandRules.TryNormaliseChat(send.Text, out var text))
        {
            player.Connection.SendEvent(ShapeLandMessageIds.ChatRejected, new ChatRejected { Reason = ChatRejection.InvalidText });
            return;
        }

        if (!player.ChatLimiter.TryTake(tick))
        {
            player.Connection.SendEvent(ShapeLandMessageIds.ChatRejected, new ChatRejected { Reason = ChatRejection.TooFast });
            return;
        }

        // Not logged (backend.md: phase 0 chat keeps nothing).
        var message = new ChatMessage { EntityId = player.EntityId, Text = text };
        foreach (var listener in _players.Values)
        {
            if (listener.Joined)
            {
                listener.Connection.SendEvent(ShapeLandMessageIds.ChatMessage, message);
            }
        }
    }

    private void Leave(Connection connection)
    {
        if (!_players.Remove(connection, out var player) || !player.Joined)
        {
            return;
        }

        foreach (var other in _players.Values)
        {
            if (other.Joined)
            {
                other.Connection.SendEvent(MessageIds.EntityDespawn, new EntityDespawn { EntityId = player.EntityId });
            }
        }

        logger.LogInformation("{Name} left ({Players} online)", player.Name, _players.Values.Count(p => p.Joined));
    }

    private enum InputKind
    {
        Connected,
        Join,
        Report,
        Chat,
        Disconnected,
    }

    private sealed record Input(Connection Connection, InputKind Kind)
    {
        public JoinRequest Join { get; init; }

        public PositionReport Report { get; init; }

        public ChatSend Chat { get; init; }

        /// <summary>The report frame's header: the client's estimate of the server tick when it sent the report.</summary>
        public uint ClientTick { get; init; }
    }

    private sealed class Player(Connection connection, int tickRate)
    {
        public Connection Connection { get; } = connection;

        public uint EntityId => Connection.Id;

        public bool Joined { get; set; }

        public string Name { get; set; } = "";

        public Shape Shape { get; set; } = null!;

        public uint Colour { get; set; }

        public MovementValidator Validator { get; set; } = null!;

        public ChatLimiter ChatLimiter { get; } = new(tickRate);

        public StateStamp Stamp { get; } = new(tickRate);

        /// <summary>The tick the current position is from (see <see cref="StateStamp"/>).</summary>
        public uint StateTick { get; set; }

        /// <summary>Accepted a new position this tick, to send to everyone else.</summary>
        public bool Moved { get; set; }

        public EntityState State() => new()
        {
            EntityId = EntityId,
            X = Validator.Position.X,
            Y = Validator.Position.Y,
            Z = Validator.Position.Z,
            Facing = Validator.Facing,
            Tick = StateTick,
        };

        public PlayerSpawn Spawn() => new()
        {
            EntityId = EntityId,
            Name = Name,
            Shape = Shape.Number,
            Colour = Colour,
            X = Validator.Position.X,
            Y = Validator.Position.Y,
            Z = Validator.Position.Z,
            Facing = Validator.Facing,
        };
    }
}
