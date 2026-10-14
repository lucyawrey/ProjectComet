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
    private readonly Spawns _spawns = new(ShapeLandWorld.Zone(content));
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
            ShapeLandMessageIds.ReturnToSpawn => new Input(connection, InputKind.Return),
            _ => null, // unknown or not for the server: ignored, so older servers tolerate newer clients
        };
        if (input is not null)
        {
            _inbox.Enqueue(input);
        }
    }

    public void OnDisconnected(Connection connection) => _inbox.Enqueue(new Input(connection, InputKind.Disconnected));

    // Tick thread.

    /// <summary>Runs one tick on the tick thread: the inputs that arrived since the last, falls and join deadlines, then the states of players who moved, sent to everyone else.</summary>
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
                case InputKind.Return:
                    Return(_players[input.Connection], tick);
                    break;
                case InputKind.Disconnected:
                    Leave(input.Connection);
                    break;
            }
        }

        // The server knows gravity: players silent in the air fall, so going quiet mid-jump can't be used to hover. Connections that never join are
        // dropped, so they can't sit holding a place.
        foreach (var player in _players.Values)
        {
            if (player.Joined)
            {
                Fall(player, tick);
                Claim(player, tick);
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

    // A joined player's join request changes their look (name, shape, colors) where they stand.
    private void Join(Player player, JoinRequest request, uint tick)
    {
        if (!player.JoinLimiter.TryTake(tick))
        {
            // No honest client asks this often.
            player.Connection.Disconnect();
            return;
        }

        if (request.ContentFingerprint != content.Fingerprint)
        {
            Reject(JoinRejection.DifferentContent);
            return;
        }

        if (!ShapeLandRules.TryNormalizeName(request.Name, out var name))
        {
            Reject(JoinRejection.InvalidName);
            return;
        }

        if (_players.Values.Any(p => p.Joined && p != player && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            Reject(JoinRejection.NameTaken);
            return;
        }

        if (!content.Shapes.TryGet(request.Shape, out var shape))
        {
            Reject(JoinRejection.UnknownShape);
            return;
        }

        if (Array.IndexOf(content.Settings.Colors.Body, request.Color) < 0 || Array.IndexOf(content.Settings.Colors.Eyes, request.EyeColor) < 0)
        {
            Reject(JoinRejection.UnavailableColor);
            return;
        }

        var changing = player.Joined;
        var oldName = player.Name;
        player.Joined = true;
        player.Name = name;
        player.Shape = shape;
        player.Color = request.Color;
        player.EyeColor = request.EyeColor;
        if (changing)
        {
            ChangeLook(player, oldName, tick);
            return;
        }

        player.Validator = new MovementValidator(_world, content.Movement, _tolerances, ShapeLandRules.TickRate);
        _spawns.TryGet(shape.Spawn, out var spawnPoint); // the content build checks every shape's spawn exists
        player.SpawnPoint = spawnPoint;
        var (feet, facing) = Spawns.Arrive(spawnPoint, _world, content.Movement, _random);
        player.Validator.Reset(feet, facing, tick);
        player.PlacedByServer(tick); // the others learn of them from the spawn, not a state

        var connection = player.Connection;
        var spawn = player.Spawn();
        connection.SendEvent(MessageIds.Welcome, new Welcome { EntityId = player.EntityId, TickRate = ShapeLandRules.TickRate });
        connection.SendEvent(ShapeLandMessageIds.PlayerSpawn, spawn);
        connection.SendEvent(ShapeLandMessageIds.SpawnSet, new SpawnSet { Spawn = spawnPoint.Number });
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

    // The player stays where they are: a snap-back to there makes the reports still in flight, moved with the old
    // shape's speed and jump, stale. Then everyone, the player too, gets a fresh spawn with the new look.
    private void ChangeLook(Player player, string oldName, uint tick)
    {
        player.Connection.SendEvent(MessageIds.PositionCorrection, player.Validator.Correct(CorrectionReason.SnapBack, tick));
        var spawn = player.Spawn();
        foreach (var listener in _players.Values)
        {
            if (listener.Joined)
            {
                // A state still queued would follow the spawn in the next frame (events go first).
                listener.Connection.EntityStates.Remove(player.EntityId);
                listener.Connection.SendEvent(ShapeLandMessageIds.PlayerSpawn, spawn);
            }
        }

        logger.LogInformation("{OldName} is now {Name}, a {Shape}", oldName, player.Name, player.Shape.DisplayName);
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
                ToSpawn(player, tick);
                break;
            default:
                // Logged for moderation, never banned automatically: the 1st, 10th, 100th… violation, so a
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
                player.MovedByServer(tick);
                break;
            case FallResult.Landed:
                player.Connection.SendEvent(MessageIds.PositionCorrection, player.Validator.Landing());
                player.MovedByServer(tick);
                break;
            case FallResult.FellOut:
                ToSpawn(player, tick);
                break;
        }
    }

    // To the player's spawn point, telling them, and everyone else seeing them reappear there.
    private void ToSpawn(Player player, uint tick)
    {
        var (feet, facing) = Spawns.Arrive(player.SpawnPoint, _world, content.Movement, _random);
        player.Connection.SendEvent(MessageIds.PositionCorrection, player.Validator.Teleport(feet, facing, tick));
        player.Claiming = null;
        player.MovedByServer(tick);
    }

    private void Return(Player player, uint tick)
    {
        if (player.Joined && player.ReturnLimiter.TryTake(tick))
        {
            ToSpawn(player, tick);
        }
    }

    // Standing in another spawn point's radius for the claim time makes it the player's. Stepping out, or into
    // another, starts over.
    private void Claim(Player player, uint tick)
    {
        var standingIn = _spawns.StandingIn(player.Validator.Position, ShapeLandRules.SpawnClaimReach);
        if (standingIn == null || standingIn == player.SpawnPoint)
        {
            player.Claiming = null;
            return;
        }

        if (player.Claiming is not { } claiming || claiming.Spawn != standingIn)
        {
            player.Claiming = (standingIn, tick);
            return;
        }

        if (tick - claiming.Since >= content.Settings.Spawns.ClaimSeconds * ShapeLandRules.TickRate)
        {
            player.SpawnPoint = standingIn;
            player.Claiming = null;
            player.Connection.SendEvent(ShapeLandMessageIds.SpawnSet, new SpawnSet { Spawn = standingIn.Number });
            logger.LogInformation("{Name}'s spawn is now {Spawn}", player.Name, standingIn.Id);
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

        if (!ShapeLandRules.TryNormalizeChat(send.Text, out var text))
        {
            player.Connection.SendEvent(ShapeLandMessageIds.ChatRejected, new ChatRejected { Reason = ChatRejection.InvalidText });
            return;
        }

        if (!player.ChatLimiter.TryTake(tick))
        {
            player.Connection.SendEvent(ShapeLandMessageIds.ChatRejected, new ChatRejected { Reason = ChatRejection.TooFast });
            return;
        }

        // Not logged: ShapeLand's chat keeps nothing.
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

        // For tuning the tolerances (MovementTolerances): how much of each this player needed.
        var headroom = player.Validator.Headroom;
        var bursts = string.Join(" ", MovementHeadroom.SpeedFactors.Select((factor, i) =>
            FormattableString.Invariant($"x{factor:0.00}={headroom.BurstSecondsNeeded(i):0.000}")));
        logger.LogInformation(
            "{Name} headroom: burst seconds {Bursts}; rise over apex {Rise} m; air slack {AirSlack} s",
            player.Name, bursts, headroom.MaxRiseOverApex.ToString("0.000", CultureInfo.InvariantCulture), headroom.MaxAirSlack.ToString("0.000", CultureInfo.InvariantCulture));
    }

    private enum InputKind
    {
        Return,
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
}
