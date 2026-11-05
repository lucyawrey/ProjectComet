using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using Comet.Content;
using Comet.Data.Characters;
using Comet.Protocol;
using Comet.Protocol.Framing;
using Comet.Protocol.Messages;
using Comet.Server.Connections;
using Comet.Server.Joining;
using Comet.Server.Movement;
using ShapeLand.Data;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;

namespace ShapeLand.GameServer;

/// <summary>
/// ShapeLand's game: players join with a ticket from the Login server as their saved character, slide and jump, and
/// chat in one server-wide channel. Where they are is saved as they move (batched) and as they leave; looks as they
/// change. The
/// server plays every zone; each player is in one and sees everyone in it (grid interest management comes with the
/// full load test). Spawn points are claimed and teleporters used per player; triggers flip placements for the whole
/// zone.
/// <para>
/// Connections decode messages on their own receive flows and queue them; the tick thread handles the queue in
/// order, so all game state belongs to the tick thread.
/// </para>
/// </summary>
public sealed class ShapeLandGame : IConnectionHandler, IJoinHost<Connection, ShapeLandCharacter>
{
    private readonly ShapeLandContent _content;
    private readonly ConnectionRegistry _registry;
    private readonly ILogger<ShapeLandGame> _logger;
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
    private readonly JoinedCharacters<Connection, ShapeLandCharacter> _joins;
    private readonly ICharacterRepository<ShapeLandCharacter> _repository;
    private uint _tick;

    /// <summary>
    /// The game of <paramref name="content"/>'s zones, saving characters through <paramref name="repository"/>; an
    /// unknown <paramref name="startSpawn"/> (a spawn point's key, or empty for none) stops it, listing the spawn points.
    /// </summary>
    public ShapeLandGame(
        ShapeLandContent content,
        string startSpawn,
        ConnectionRegistry registry,
        Comet.Tickets.TicketChecker? tickets,
        ICharacterRepository<ShapeLandCharacter> repository,
        ILogger<ShapeLandGame> logger
    )
    {
        _content = content;
        _registry = registry;
        _logger = logger;
        _repository = repository;
        _joins = new(tickets, repository, TimeSpan.FromSeconds(ShapeLandRules.ClaimDeadlineSeconds), logger);
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
                throw new InvalidOperationException(
                    $"ShapeLand:StartSpawn: there's no spawn point '{startSpawn}'; spawn points: {string.Join(", ", _spawns.Values.Select(s => s.Spawn.Id).Order())}."
                );
            }
        }
    }

    public ShapeLandContent Content => _content;

#if DEBUG
    /// <summary>With joins without accounts (<see cref="ShapeLandServerOptions.DevJoins"/>), the characters they pick from.</summary>
    public DevCharacters? DevCharacters { get; init; }
#endif

    // Receive flows.

    public void OnConnected(Connection connection) => _inbox.Enqueue(new Input(connection, InputKind.Connected));

    public void OnMessage(Connection connection, uint clientTick, ushort messageId, ReadOnlyMemory<byte> payload)
    {
        var options = ShapeLandProtocol.Options;
        var input = messageId switch
        {
            MessageIds.PositionReport => new Input(connection, InputKind.Report)
            {
                Report = FrameReader.Decode<PositionReport>(payload, options),
                ClientTick = clientTick,
            },
            ShapeLandMessageIds.JoinRequest => new Input(connection, InputKind.Join) { Join = FrameReader.Decode<JoinRequest>(payload, options) },
            ShapeLandMessageIds.DevJoinRequest => new Input(connection, InputKind.DevJoin)
            {
                DevJoin = FrameReader.Decode<DevJoinRequest>(payload, options),
            },
            ShapeLandMessageIds.LookChange => new Input(connection, InputKind.Look) { Look = FrameReader.Decode<LookChange>(payload, options) },
            ShapeLandMessageIds.NameChanged => new Input(connection, InputKind.NameChanged),
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
        _tick = tick;
        while (_inbox.TryDequeue(out var input))
        {
            if (
                input.Kind != InputKind.Connected
                && input.Kind != InputKind.Disconnected
                && (!_players.TryGetValue(input.Connection, out var from) || from.Dropped)
            )
            {
                continue; // a dropped player's inputs, until its connection closes
            }

            switch (input.Kind)
            {
                case InputKind.Connected:
                    _players[input.Connection] = new Player(input.Connection, ShapeLandRules.TickRate) { ConnectedTick = tick };
                    break;
                case InputKind.Join:
                    Join(_players[input.Connection], input.Join, tick);
                    break;
                case InputKind.DevJoin:
                    DevJoin(_players[input.Connection], input.DevJoin, tick);
                    break;
                case InputKind.Look:
                    ChangeLook(_players[input.Connection], input.Look, tick);
                    break;
                case InputKind.NameChanged:
                    ReloadName(_players[input.Connection], tick);
                    break;
                case InputKind.NameLoaded:
                    Rename(_players[input.Connection], input.CharacterId, input.Name, tick);
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

        // Finished joins, and players dropped because their character was taken or removed.
        _joins.Update(this);

        // The server knows gravity: players silent in the air fall, so going quiet mid-jump can't be used to hover. Connections that never join are
        // dropped, so they can't sit holding a place (a join in progress has its own deadline).
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
            else if (
                player.Dropped
                    ? tick >= player.CloseAtTick
                    : !player.Joining && tick - player.ConnectedTick > ShapeLandRules.JoinDeadlineSeconds * ShapeLandRules.TickRate
            )
            {
                player.Connection.Disconnect();
            }
        }

        foreach (var zone in _zones.Values)
        {
            zone.Tick(
                tick,
                seconds,
                (tag, flipped) =>
                {
                    foreach (var player in zone.Players)
                    {
                        player.Connection.SendEvent(ShapeLandMessageIds.PlacementsFlipped, new PlacementsFlipped { Tag = tag, Flipped = flipped });
                    }
                }
            );
        }

        foreach (var mover in _players.Values)
        {
            if (!mover.Moved)
            {
                continue;
            }

            mover.Moved = false;
            SavePosition(mover);
            var state = mover.State();
            foreach (var viewer in mover.Zone.Players)
            {
                if (viewer != mover)
                {
                    viewer.Connection.EntityStates.Set(mover.EntityId, state);
                }
            }
        }

        foreach (var connection in _registry.Snapshot)
        {
            connection.Flush(tick);
        }
    }

    // A join hands the ticket to JoinedCharacters, which checks it and loads the character off the tick thread;
    // the player spawns when it comes back (Joined) on a later tick.
    private void Join(Player player, JoinRequest request, uint tick)
    {
        if (CanJoin(player, request.ContentFingerprint, tick))
        {
            player.Joining = _joins.Begin(player.Connection, request.Ticket ?? "");
        }
    }

    // A join by name, without a ticket: only a Debug server started with ShapeLand:DevJoins takes it. A name that's
    // playing is refused before the claim, so a dev join never replaces anyone.
    private void DevJoin(Player player, DevJoinRequest request, uint tick)
    {
        if (!CanJoin(player, request.ContentFingerprint, tick))
        {
            return;
        }

#if DEBUG
        if (DevCharacters is { } characters)
        {
            if (!ShapeLandRules.TryNormalizeName(request.Name, out var name))
            {
                Reject(player, JoinRejection.BadName);
                return;
            }

            if (characters.FindOrAdd(name, request.Bot, request.Shape, request.Color, request.EyeColor) is not { } characterId)
            {
                Reject(player, JoinRejection.NameTaken);
                return;
            }

            if (_joins.IsPlaying(characterId))
            {
                Reject(player, JoinRejection.NameOnline);
                return;
            }

            player.Joining = _joins.Begin(player.Connection, characterId, characters.AccountOf(characterId));
            return;
        }
#endif
        Reject(player, JoinRejection.AccountsNeeded);
    }

    private bool CanJoin(Player player, ulong contentFingerprint, uint tick)
    {
        if (player.Joining || player.Joined || !player.JoinLimiter.TryTake(tick))
        {
            // One join at a time, and retries after a failure only at a pace no honest client exceeds.
            player.Connection.Disconnect();
            return false;
        }

        if (contentFingerprint != _content.Fingerprint)
        {
            Reject(player, JoinRejection.DifferentContent);
            return false;
        }

        return true;
    }

    private static void Reject(Player player, JoinRejection reason) =>
        player.Connection.SendEvent(ShapeLandMessageIds.JoinRejected, new JoinRejected { Reason = reason });

    void IJoinHost<Connection, ShapeLandCharacter>.JoinFailed(Connection connection, JoinFailure failure)
    {
        if (!_players.TryGetValue(connection, out var player))
        {
            return;
        }

        player.Joining = false;
        var reason = failure switch
        {
            JoinFailure.ExpiredTicket => JoinRejection.ExpiredTicket,
            JoinFailure.UsedTicket => JoinRejection.UsedTicket,
            JoinFailure.WrongServer => JoinRejection.WrongServer,
            JoinFailure.NoCharacter => JoinRejection.NoCharacter,
            JoinFailure.Busy => JoinRejection.Busy,
            JoinFailure.Replaced => JoinRejection.Replaced,
            _ => JoinRejection.BadTicket,
        };
        connection.SendEvent(ShapeLandMessageIds.JoinRejected, new JoinRejected { Reason = reason });
        _logger.LogInformation("A join was refused: {Failure}", failure);
    }

    void IJoinHost<Connection, ShapeLandCharacter>.Joined(Connection connection, CharacterClaim<ShapeLandCharacter> claim)
    {
        if (!_players.TryGetValue(connection, out var player))
        {
            _ = _joins.Leave(connection, null); // closed meanwhile
            return;
        }

        player.Joining = false;
        player.Joined = true;
        Spawn(player, claim.Character!, claim.Position, _tick);
    }

    CharacterPosition? IJoinHost<Connection, ShapeLandCharacter>.Dropped(Connection connection, bool replaced)
    {
        if (!_players.TryGetValue(connection, out var player) || !player.Joined)
        {
            return null;
        }

        var position = PositionOf(player);
        LeaveZone(player);
        player.Joined = false;
        player.Dropped = true;
        player.CloseAtTick = _tick + 2; // after this tick's frame, with the reason, has gone out
        connection.SendEvent(ShapeLandMessageIds.Dropped, new Dropped { Reason = replaced ? DropReason.Replaced : DropReason.Removed });
        _logger.LogInformation("{Name} was dropped: {Reason}", player.Name, replaced ? "joined again elsewhere" : "removed");
        return position;
    }

    // Puts a newly joined character in the world: where it was saved, or (a missing zone, or a spot that's no longer
    // in the zone) at its shape's spawn. A shape or color no longer in the content becomes the first one there is.
    private void Spawn(Player player, ShapeLandCharacter character, CharacterPosition? saved, uint tick)
    {
        player.Character = character;
        var lookChanged = false;
        if (!_content.Shapes.TryGet(character.Shape, out var shape))
        {
            shape = _content.Shapes.All[0];
            character.Shape = shape.Number;
            lookChanged = true;
        }

        foreach (var eyes in new[] { false, true })
        {
            var color = eyes ? character.EyeColor : character.Color;
            if (!_content.Colors.TryGet(color, out _))
            {
                if (eyes)
                {
                    character.EyeColor = _content.Colors.All[0].Number;
                }
                else
                {
                    character.Color = _content.Colors.All[0].Number;
                }

                lookChanged = true;
            }
        }

        if (lookChanged)
        {
            _joins.SaveCharacter(player.Connection, character);
        }

        player.Shape = shape;
        var shapeSpawn = _startSpawn ?? _spawns.Values.First(s => s.Spawn.Id == shape.Spawn);
        player.SpawnPoint = shapeSpawn.Spawn;

        ZoneState zone;
        Vector3 feet;
        float facing;
        var atSpawn = false;
        if (
            _startSpawn == null
            && saved is { } spot
            && _zones.TryGetValue(spot.Zone, out var savedZone)
            && Fits(savedZone, new Vector3(spot.X, spot.Y, spot.Z))
        )
        {
            zone = savedZone;
            feet = new Vector3(spot.X, spot.Y, spot.Z);
            facing = spot.Facing;
        }
        else
        {
            zone = shapeSpawn.Zone;
            (feet, facing) = Comet.Simulation.Triggers.Arrive(
                shapeSpawn.Spawn.Volume,
                shapeSpawn.Spawn.Facing,
                zone.World,
                _content.Movement,
                _random
            );
            atSpawn = true;
        }

        player.Zone = zone;
        player.Validator = new MovementValidator(zone.World, _content.Movement, _tolerances, ShapeLandRules.TickRate);
        player.Validator.Reset(feet, facing, tick);
        player.PlacedByServer(tick); // the others learn of them from the spawn, not a state

        var connection = player.Connection;
        connection.SendEvent(
            MessageIds.Welcome,
            new Welcome
            {
                EntityId = player.EntityId,
                TickRate = ShapeLandRules.TickRate,
                Zone = zone.Zone.Number,
            }
        );
        connection.SendEvent(ShapeLandMessageIds.ZoneEntered, new ZoneEntered { Zone = zone.Zone.Number, Flipped = zone.Flipped.ToArray() });
        connection.SendEvent(ShapeLandMessageIds.PlayerSpawn, player.Spawn());
        connection.SendEvent(ShapeLandMessageIds.SpawnSet, new SpawnSet { Spawn = player.SpawnPoint.Number });
        if (atSpawn)
        {
            // Standing on the spawn already: it isn't claimed again until they step out and in.
            player.SpawnCharge.Arrived(player.SpawnPoint, player.SpawnPoint.Volume);
        }

        Arrive(player, zone);
        _logger.LogInformation("{Name} joined as a {Shape} ({Players} online)", player.Name, shape.DisplayName, _players.Values.Count(p => p.Joined));
    }

    // A saved spot still fits its zone: in one of its chunks, above the kill height, and not inside anything.
    private bool Fits(ZoneState zone, Vector3 feet)
    {
        if (!float.IsFinite(feet.X) || !float.IsFinite(feet.Y) || !float.IsFinite(feet.Z) || feet.Y <= zone.World.KillHeight)
        {
            return false;
        }

        var chunk = ChunkCoord.Containing(feet);
        return zone.Zone.Chunks.Any(c => c.Coord.Equals(chunk))
            && !zone.World.IsBlocked(feet, _content.Movement.BodyRadius, _content.Movement.BodyHeight);
    }

    // A look change from the Change Shape card: checked against the content, saved, and shown to everyone where the
    // player stands.
    private void ChangeLook(Player player, LookChange change, uint tick)
    {
        if (!player.Joined)
        {
            return;
        }

        if (!player.LookLimiter.TryTake(tick))
        {
            player.Connection.SendEvent(ShapeLandMessageIds.LookRejected, new LookRejected { Reason = LookRejection.TooFast });
            return;
        }

        if (!_content.Shapes.TryGet(change.Shape, out var shape))
        {
            player.Connection.SendEvent(ShapeLandMessageIds.LookRejected, new LookRejected { Reason = LookRejection.UnknownShape });
            return;
        }

        if (!_content.Colors.TryGet(change.Color, out _) || !_content.Colors.TryGet(change.EyeColor, out _))
        {
            player.Connection.SendEvent(ShapeLandMessageIds.LookRejected, new LookRejected { Reason = LookRejection.UnavailableColor });
            return;
        }

        player.Shape = shape;
        player.Character.Shape = shape.Number;
        player.Character.Color = change.Color;
        player.Character.EyeColor = change.EyeColor;
        _joins.SaveCharacter(player.Connection, player.Character);
        Respawn(player, tick);
        _logger.LogInformation("{Name} is now a {Shape}", player.Name, shape.DisplayName);
    }

    // The player renamed their account: the name is read from the database off the tick thread, then shown.
    private void ReloadName(Player player, uint tick)
    {
        if (!player.Joined || !player.RenameLimiter.TryTake(tick) || !_joins.TryGet(player.Connection, out var characterId, out _))
        {
            return;
        }

        var connection = player.Connection;
        _ = Task.Run(async () =>
        {
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(ShapeLandRules.JoinDeadlineSeconds));
                var name = await _repository.LoadNameAsync(characterId, deadline.Token);
                if (name != null)
                {
                    _inbox.Enqueue(new Input(connection, InputKind.NameLoaded) { CharacterId = characterId, Name = name });
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Couldn't reload the name of character {Character}", characterId);
            }
        });
    }

    private void Rename(Player player, long characterId, string name, uint tick)
    {
        if (!player.Joined || player.Character.Id != characterId || player.Name == name)
        {
            return;
        }

        var oldName = player.Name;
        player.Character.Name = name;
        Respawn(player, tick);
        _logger.LogInformation("{OldName} is now {Name}", oldName, name);
    }

    // The player stays where they are: a snap-back to there makes the reports still in flight, moved with the old
    // shape's speed and jump, stale. Then everyone, the player too, gets a fresh spawn with the new look and name.
    private static void Respawn(Player player, uint tick)
    {
        player.Connection.SendEvent(MessageIds.PositionCorrection, player.Validator.Correct(CorrectionReason.SnapBack, tick));
        var spawn = player.Spawn();
        foreach (var listener in player.Zone.Players)
        {
            // A state still queued would follow the spawn in the next frame (events go first).
            listener.Connection.EntityStates.Remove(player.EntityId);
            listener.Connection.SendEvent(ShapeLandMessageIds.PlayerSpawn, spawn);
        }
    }

    private static CharacterPosition PositionOf(Player player) =>
        new(player.Zone.Zone.Number, player.Validator.Position.X, player.Validator.Position.Y, player.Validator.Position.Z, player.Validator.Facing);

    private void SavePosition(Player player) => _joins.SaveStatus(player.Connection, PositionOf(player));

    /// <summary>
    /// Saves every player's spot and releases their characters, for shutdown. Call after the tick loop has stopped,
    /// since it reads the game's state.
    /// </summary>
    public Task LeaveAllAsync() =>
        _joins.LeaveAll(connection => _players.TryGetValue(connection, out var player) && player.Joined ? PositionOf(player) : null);

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
                    _logger.LogWarning(
                        "Movement violation by {Name}: {Verdict} at ({X:F1}, {Y:F1}, {Z:F1}), {Count} so far",
                        player.Name,
                        verdict,
                        report.X,
                        report.Y,
                        report.Z,
                        player.Validator.Violations
                    );
                }

                player.Connection.SendEvent(MessageIds.PositionCorrection, player.Validator.Correct(CorrectionReason.SnapBack, tick, verdict));
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
            _logger.LogInformation("Finishing the fall of {Name}, silent in the air, {Count} so far", player.Name, player.ServerFalls);
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
        _logger.LogInformation("{Name}'s spawn is now {Spawn}", player.Name, standingIn.Id);
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
        _logger.LogInformation("{Name} took {Teleporter} to {Target}", player.Name, standingIn.Id, target.Teleporter.Id);
    }

    // Moves a player to arrive in a volume, in their zone or another. Into another zone: the player learns the zone,
    // its old players see them leave, and they and its players see each other arrive.
    private void MoveTo(Player player, ZoneState zone, TriggerVolume volume, SpawnFacing facing, CorrectionReason reason, uint tick)
    {
        var (feet, yaw) = Comet.Simulation.Triggers.Arrive(volume, facing, zone.World, _content.Movement, _random);
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
        SavePosition(player);
        _logger.LogInformation("{Name} went to {Zone}", player.Name, zone.Zone.Id);
    }

    // Puts a placed player in a zone: they and the players already there see each other.
    private static void Arrive(Player player, ZoneState zone)
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
        player.Validator.PassThrough.Prune(player.Zone.World, player.Validator.Position, _content.Movement.BodyRadius, _content.Movement.BodyHeight);

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
        var message = new ChatMessage
        {
            EntityId = player.EntityId,
            Text = text,
            Name = player.Name,
        };
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
            _ = _joins.Leave(connection, null); // a join in progress lets its character go
            return;
        }

        LeaveZone(player);
        _ = _joins.Leave(connection, PositionOf(player)); // the final save, then the character is released

        _logger.LogInformation(
            "{Name} left ({Players} online); {Violations} movement violations, {Falls} falls finished by the server, {Clamped} of {Stamps} stamps clamped",
            player.Name,
            _players.Values.Count(p => p.Joined),
            player.Validator.Violations,
            player.ServerFalls,
            player.Stamp.Clamped,
            player.Stamp.Broadcasts
        );

        // For tuning the tolerances (MovementTolerances): how much of each this player needed.
        var headroom = player.Validator.Headroom;
        var bursts = string.Join(
            " ",
            MovementHeadroom.SpeedFactors.Select((factor, i) => FormattableString.Invariant($"x{factor:0.00}={headroom.BurstSecondsNeeded(i):0.000}"))
        );
        _logger.LogInformation(
            "{Name} headroom: burst seconds {Bursts}; rise over apex {Rise} m; air slack {AirSlack} s",
            player.Name,
            bursts,
            headroom.MaxRiseOverApex.ToString("0.000", CultureInfo.InvariantCulture),
            headroom.MaxAirSlack.ToString("0.000", CultureInfo.InvariantCulture)
        );
    }

    private enum InputKind
    {
        Return,
        Connected,
        Join,
        DevJoin,
        Look,
        NameChanged,
        NameLoaded,
        Report,
        Chat,
        Disconnected,
    }

    private sealed record Input(Connection Connection, InputKind Kind)
    {
        public JoinRequest Join { get; init; }

        public DevJoinRequest DevJoin { get; init; }

        public PositionReport Report { get; init; }

        public ChatSend Chat { get; init; }

        public LookChange Look { get; init; }

        /// <summary>For <see cref="InputKind.NameLoaded"/>: the character and its name, read from the database.</summary>
        public long CharacterId { get; init; }

        public string Name { get; init; } = "";

        /// <summary>The report frame's header: the client's estimate of the server tick when it sent the report.</summary>
        public uint ClientTick { get; init; }
    }
}
