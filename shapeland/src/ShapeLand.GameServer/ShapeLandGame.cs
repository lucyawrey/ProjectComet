using System.Collections.Concurrent;
using System.Globalization;
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
                    _players[input.Connection] = new Player(input.Connection, ShapeLandRules.TickRate) { ConnectedTick = tick };
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

        // The server knows gravity: players silent in the air fall (netcode.md). Connections that never join are
        // dropped, so they can't sit holding a place.
        foreach (var player in _players.Values)
        {
            if (player.Joined)
            {
                Fall(player, tick);
            }
            else if (tick - player.ConnectedTick > ShapeLandRules.JoinDeadlineSeconds * ShapeLandRules.TickRate)
            {
                player.Connection.Disconnect();
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
        if (!player.JoinLimiter.TryTake(tick))
        {
            // No honest client asks this often.
            player.Connection.Disconnect();
            return;
        }

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

        if (Array.IndexOf(ShapeLandRules.BodyColours, request.Colour) < 0 || Array.IndexOf(ShapeLandRules.EyeColours, request.EyeColour) < 0)
        {
            Reject(JoinRejection.UnavailableColour);
            return;
        }

        player.Joined = true;
        player.Name = name;
        player.Shape = shape;
        player.Colour = request.Colour;
        player.EyeColour = request.EyeColour;
        player.Validator = new MovementValidator(_world, ShapeLandWorld.Rules, _tolerances, ShapeLandRules.TickRate);
        player.Validator.Reset(ShapeLandWorld.SpawnPoint(_world), (float)(_random.NextDouble() * Math.Tau), tick);
        player.Stamp.StampServer(tick);
        player.StateTick = player.Stamp.Broadcast;

        var connection = player.Connection;
        var spawn = player.Spawn();
        connection.SendEvent(MessageIds.Welcome, new Welcome { EntityId = player.EntityId, TickRate = ShapeLandRules.TickRate });
        connection.SendEvent(ShapeLandMessageIds.PlayerSpawn, spawn);
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

        var stamp = player.Stamp.Stamp(clientTick, tick);
        var verdict = player.Validator.Check(report, tick, player.Shape.MaxSpeed, player.Shape.JumpVelocity, stamp);
        switch (verdict)
        {
            case MovementVerdict.Accepted:
                player.StateTick = player.Stamp.Broadcast; // shifted by the player's latency (StateStamp)
                player.Moved = true;
                break;
            case MovementVerdict.Stale:
                break;
            case MovementVerdict.FellOut:
                player.Connection.SendEvent(MessageIds.PositionCorrection, player.Validator.Correct(CorrectionReason.Respawn, tick));
                player.Stamp.StampServer(tick);
                player.StateTick = player.Stamp.Broadcast;
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

    private void Fall(Player player, uint tick)
    {
        var wasFalling = player.Validator.ServerFalling;
        var result = player.Validator.Fall(tick, player.Shape.MaxSpeed, player.Shape.JumpVelocity);
        // A fall that began during a long silence can land on the very tick the server takes it over.
        if (!wasFalling && result != FallResult.None)
        {
            player.ServerFalls++;
            logger.LogInformation("Finishing the fall of {Name}, silent in the air, {Count} so far", player.Name, player.ServerFalls);
        }

        switch (result)
        {
            case FallResult.Moved:
                player.Stamp.StampServer(tick);
                player.StateTick = player.Stamp.Broadcast;
                player.Moved = true;
                break;
            case FallResult.Landed:
                player.Connection.SendEvent(MessageIds.PositionCorrection, player.Validator.Landing());
                player.Stamp.StampServer(tick);
                player.StateTick = player.Stamp.Broadcast;
                player.Moved = true;
                break;
            case FallResult.FellOut:
                player.Connection.SendEvent(MessageIds.PositionCorrection, player.Validator.Correct(CorrectionReason.Respawn, tick));
                player.Stamp.StampServer(tick);
                player.StateTick = player.Stamp.Broadcast;
                player.Moved = true;
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
                // A state still queued would follow the despawn in the next frame (events go first).
                other.Connection.EntityStates.Remove(player.EntityId);
                other.Connection.SendEvent(MessageIds.EntityDespawn, new EntityDespawn { EntityId = player.EntityId });
            }
        }

        logger.LogInformation(
            "{Name} left ({Players} online); {Violations} movement violations, {Falls} falls finished by the server, {Clamped} of {Stamps} stamps clamped",
            player.Name, _players.Values.Count(p => p.Joined), player.Validator.Violations, player.ServerFalls, player.Stamp.Clamped, player.Stamp.Broadcasts);

        // For tuning the tolerances (prototype.md): how much of each this player needed.
        var headroom = player.Validator.Headroom;
        var bursts = string.Join(" ", MovementHeadroom.SpeedFactors.Select((factor, i) =>
            FormattableString.Invariant($"x{factor:0.00}={headroom.BurstSecondsNeeded(i):0.000}")));
        logger.LogInformation(
            "{Name} headroom: burst seconds {Bursts}; rise over apex {Rise} m; air slack {AirSlack} s",
            player.Name, bursts, headroom.MaxRiseOverApex.ToString("0.000", CultureInfo.InvariantCulture), headroom.MaxAirSlack.ToString("0.000", CultureInfo.InvariantCulture));
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

        public uint EyeColour { get; set; }

        public MovementValidator Validator { get; set; } = null!;

        public TickLimiter ChatLimiter { get; } = TickLimiter.Chat(tickRate);

        public TickLimiter JoinLimiter { get; } = TickLimiter.Join(tickRate);

        /// <summary>The tick the connection arrived; it must join within <see cref="ShapeLandRules.JoinDeadlineSeconds"/>.</summary>
        public uint ConnectedTick { get; init; }

        public StateStamp Stamp { get; } = new(tickRate);

        /// <summary>The tick the current position is from (see <see cref="StateStamp"/>).</summary>
        public uint StateTick { get; set; }

        /// <summary>How often the server has taken over this player's fall because they went silent in the air.</summary>
        public int ServerFalls { get; set; }

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
            VelocityX = Validator.Velocity.X,
            VelocityY = Validator.Velocity.Y,
            VelocityZ = Validator.Velocity.Z,
        };

        public PlayerSpawn Spawn() => new()
        {
            EntityId = EntityId,
            Name = Name,
            Shape = Shape.Number,
            Colour = Colour,
            EyeColour = EyeColour,
            X = Validator.Position.X,
            Y = Validator.Position.Y,
            Z = Validator.Position.Z,
            Facing = Validator.Facing,
        };
    }
}
