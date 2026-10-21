using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using Comet.Content;
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
/// ShapeLand's game: players join with a name and shape, slide and jump, and chat in one server-wide channel. The
/// server plays every zone; each player is in one and sees everyone in it (grid interest management comes with the
/// full load test). Spawn points are claimed and teleporters used per player; triggers flip placements for the whole
/// zone.
/// <para>
/// Connections decode messages on their own receive flows and queue them; the tick thread handles the queue in
/// order, so all game state belongs to the tick thread.
/// </para>
/// </summary>
public sealed class ShapeLandGame : IConnectionHandler
{
    private readonly ShapeLandContent content;
    private readonly ConnectionRegistry registry;
    private readonly ILogger<ShapeLandGame> logger;
    private readonly Dictionary<int, ZoneState> _zones = [];
    // Every spawn point and teleporter, with its zone, for going there from any zone.
    private readonly Dictionary<int, (ZoneState Zone, SpawnPoint Spawn)> _spawns = [];
    private readonly Dictionary<string, (ZoneState Zone, Teleporter Teleporter)> _teleporters = [];
    // Where every new player starts instead of their shape's spawn (ShapeLand:StartSpawn), if set.
    private readonly (ZoneState Zone, SpawnPoint Spawn)? _startSpawn;
    private readonly MovementTolerances _tolerances = new();
    private readonly ConcurrentQueue<Input> _inbox = new();
    private readonly Dictionary<Connection, Player> _players = [];
    private readonly Random _random = new();

    /// <summary>The game of <paramref name="content"/>'s zones; an unknown <paramref name="startSpawn"/> (a spawn point's key, or empty for none) stops it, listing the spawn points.</summary>
    public ShapeLandGame(ShapeLandContent content, string startSpawn, ConnectionRegistry registry, ILogger<ShapeLandGame> logger)
    {
        this.content = content;
        this.registry = registry;
        this.logger = logger;
        foreach (var zone in content.Zones.All)
        {
            var state = new ZoneState(content, zone);
            _zones.Add(zone.Number, state);
            foreach (var spawn in state.Triggers.Spawns)
            {
                _spawns.Add(spawn.Number, (state, spawn));
            }

            foreach (var teleporter in state.Triggers.Teleporters)
            {
                _teleporters.Add(teleporter.Id, (state, teleporter));
            }
        }

        if (!string.IsNullOrEmpty(startSpawn))
        {
            _startSpawn = _spawns.Values.FirstOrDefault(s => s.Spawn.Id == startSpawn);
            if (_startSpawn?.Spawn is null)
            {
                throw new InvalidOperationException($"ShapeLand:StartSpawn: there's no spawn point '{startSpawn}'; spawn points: {string.Join(", ", _spawns.Values.Select(s => s.Spawn.Id).Order())}.");
            }
        }
    }

    public ShapeLandContent Content => content;

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
        var seconds = (float)tick / ShapeLandRules.TickRate;
        foreach (var player in _players.Values)
        {
            if (player.Joined)
            {
                Fall(player, tick);
                KeepPassingThrough(player);
                Claim(player, seconds);
                Teleport(player, tick, seconds);
            }
            else if (tick - player.ConnectedTick > ShapeLandRules.JoinDeadlineSeconds * ShapeLandRules.TickRate)
            {
                player.Connection.Disconnect();
            }
        }

        foreach (var zone in _zones.Values)
        {
            zone.Tick(tick, seconds, (tag, flipped) =>
            {
                foreach (var player in zone.Players)
                {
                    player.Connection.SendEvent(ShapeLandMessageIds.PlacementsFlipped, new PlacementsFlipped { Tag = tag, Flipped = flipped });
                }
            });
        }

        foreach (var mover in _players.Values)
        {
            if (!mover.Moved)
            {
                continue;
            }

            mover.Moved = false;
            var state = mover.State();
            foreach (var viewer in mover.Zone.Players)
            {
                if (viewer != mover)
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

        // The shape's spawn (the content build checks it's in a zone), unless the server starts everyone at one.
        var (zone, spawnPoint) = _startSpawn ?? _spawns.Values.First(s => s.Spawn.Id == shape.Spawn);
        player.SpawnPoint = spawnPoint;
        player.Zone = zone;
        player.Validator = new MovementValidator(zone.World, content.Movement, _tolerances, ShapeLandRules.TickRate);
        var (feet, facing) = Comet.Simulation.Triggers.Arrive(spawnPoint.Volume, spawnPoint.Facing, zone.World, content.Movement, _random);
        player.Validator.Reset(feet, facing, tick);
        player.PlacedByServer(tick); // the others learn of them from the spawn, not a state

        var connection = player.Connection;
        connection.SendEvent(MessageIds.Welcome, new Welcome { EntityId = player.EntityId, TickRate = ShapeLandRules.TickRate, Zone = zone.Zone.Number });
        connection.SendEvent(ShapeLandMessageIds.ZoneEntered, new ZoneEntered { Zone = zone.Zone.Number, Flipped = zone.Flipped.ToArray() });
        connection.SendEvent(ShapeLandMessageIds.PlayerSpawn, player.Spawn());
        connection.SendEvent(ShapeLandMessageIds.SpawnSet, new SpawnSet { Spawn = spawnPoint.Number });
        // Standing on the spawn already: it isn't claimed again until they step out and in.
        player.SpawnCharge.Arrived(spawnPoint, spawnPoint.Volume);
        Arrive(player, zone);

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
        foreach (var listener in player.Zone.Players)
        {
            // A state still queued would follow the spawn in the next frame (events go first).
            listener.Connection.EntityStates.Remove(player.EntityId);
            listener.Connection.SendEvent(ShapeLandMessageIds.PlayerSpawn, spawn);
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

    // To the player's spawn point, telling them, and everyone else seeing them reappear there; it may be in another zone.
    private void ToSpawn(Player player, uint tick)
    {
        var (zone, _) = _spawns[player.SpawnPoint.Number];
        MoveTo(player, zone, player.SpawnPoint.Volume, player.SpawnPoint.Facing, CorrectionReason.Respawn, tick);
        player.SpawnCharge.Arrived(player.SpawnPoint, player.SpawnPoint.Volume);
    }

    private void Return(Player player, uint tick)
    {
        if (player.Joined && player.ReturnLimiter.TryTake(tick))
        {
            ToSpawn(player, tick);
        }
    }

    // Standing in another spawn point for its stand time makes it the player's. Stepping out, or into another,
    // starts over.
    private void Claim(Player player, float seconds)
    {
        var standingIn = player.Zone.Triggers.SpawnAt(player.Validator.Position);
        if (!player.SpawnCharge.Step(standingIn, standingIn?.Volume, seconds) || standingIn == player.SpawnPoint)
        {
            return;
        }

        player.SpawnPoint = standingIn!;
        player.Connection.SendEvent(ShapeLandMessageIds.SpawnSet, new SpawnSet { Spawn = standingIn!.Number, Claimed = true });
        logger.LogInformation("{Name}'s spawn is now {Spawn}", player.Name, standingIn.Id);
    }

    // Standing in a teleporter for its stand time sends the player to its target, which doesn't send them on until
    // they step out and in again.
    private void Teleport(Player player, uint tick, float seconds)
    {
        var standingIn = player.Zone.Triggers.TeleporterAt(player.Validator.Position);
        if (!player.TeleportCharge.Step(standingIn, standingIn?.Volume, seconds) || !_teleporters.TryGetValue(standingIn!.Target, out var target))
        {
            return;
        }

        MoveTo(player, target.Zone, target.Teleporter.Volume, target.Teleporter.Facing, CorrectionReason.Teleport, tick);
        player.TeleportCharge.Arrived(target.Teleporter, target.Teleporter.Volume);
        logger.LogInformation("{Name} took {Teleporter} to {Target}", player.Name, standingIn.Id, target.Teleporter.Id);
    }

    // Moves a player to arrive in a volume, in their zone or another. Into another zone: the player learns the zone,
    // its old players see them leave, and they and its players see each other arrive.
    private void MoveTo(Player player, ZoneState zone, TriggerVolume volume, SpawnFacing facing, CorrectionReason reason, uint tick)
    {
        var (feet, yaw) = Comet.Simulation.Triggers.Arrive(volume, facing, zone.World, content.Movement, _random);
        if (zone == player.Zone)
        {
            player.Connection.SendEvent(MessageIds.PositionCorrection, player.Validator.Teleport(feet, yaw, tick, reason));
            player.MovedByServer(tick);
            return;
        }

        LeaveZone(player);
        player.Connection.SendEvent(ShapeLandMessageIds.ZoneEntered, new ZoneEntered { Zone = zone.Zone.Number, Flipped = zone.Flipped.ToArray() });
        player.Connection.SendEvent(MessageIds.PositionCorrection, player.Validator.Teleport(feet, yaw, tick, reason, zone.World));
        player.SpawnCharge.Clear();
        player.TeleportCharge.Clear();
        player.PlacedByServer(tick);
        Arrive(player, zone);
        logger.LogInformation("{Name} went to {Zone}", player.Name, zone.Zone.Id);
    }

    // Puts a placed player in a zone: they and the players already there see each other.
    private void Arrive(Player player, ZoneState zone)
    {
        player.Zone = zone;
        var spawn = player.Spawn();
        foreach (var other in zone.Players)
        {
            player.Connection.SendEvent(ShapeLandMessageIds.PlayerSpawn, other.Spawn());
            other.Connection.SendEvent(ShapeLandMessageIds.PlayerSpawn, spawn);
        }

        zone.Players.Add(player);
    }

    // Takes a player out of their zone: everyone else there sees them go, and they stop seeing them.
    private static void LeaveZone(Player player)
    {
        var zone = player.Zone;
        zone.Players.Remove(player);
        foreach (var other in zone.Players)
        {
            // A state still queued would follow the despawn in the next frame (events go first).
            other.Connection.EntityStates.Remove(player.EntityId);
            other.Connection.SendEvent(MessageIds.EntityDespawn, new EntityDespawn { EntityId = player.EntityId });
            player.Connection.EntityStates.Remove(other.EntityId);
            player.Connection.SendEvent(MessageIds.EntityDespawn, new EntityDespawn { EntityId = other.EntityId });
        }
    }

    // A placement turned on around the player stops passing through once they're fully out of it.
    private void KeepPassingThrough(Player player) =>
        player.Validator.PassThrough.Prune(player.Zone.World, player.Validator.Position, content.Movement.BodyRadius, content.Movement.BodyHeight);

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
        var message = new ChatMessage { EntityId = player.EntityId, Text = text, Name = player.Name };
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

        LeaveZone(player);

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
