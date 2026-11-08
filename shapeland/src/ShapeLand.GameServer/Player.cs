using Comet.Content;
using Comet.Protocol.Messages;
using Comet.Server.Connections;
using Comet.Server.Movement;
using Comet.Server.Ticking;
using Comet.Simulation;
using ShapeLand.Data;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;

namespace ShapeLand.GameServer;

/// <summary>A connection in ShapeLandGame: a player once joined, with their look, movement checks and rate limits. Tick thread only.</summary>
internal sealed class Player(Connection connection, int tickRate)
{
    public Connection Connection { get; } = connection;

    public uint EntityId => Connection.Id;

    /// <summary>True once the player has joined and spawned; before that only a join request is accepted.</summary>
    public bool Joined { get; set; }

    /// <summary>A join request came in (its ticket is being checked, or its character loaded).</summary>
    public bool Joining { get; set; }

    /// <summary>Dropped by the server (replaced or removed): ignored until the connection closes on <see cref="CloseAtTick"/>.</summary>
    public bool Dropped { get; set; }

    /// <summary>When a dropped player's connection is closed, a tick after its last frame went out.</summary>
    public uint CloseAtTick { get; set; }

    /// <summary>The saved character, with its look; the name is the account's.</summary>
    public ShapeLandCharacter Character { get; set; } = null!;

    public string Name => Character.Name;

    public Shape Shape { get; set; } = null!;

    /// <summary>Checks the player's position reports; made on joining.</summary>
    public MovementValidator Validator { get; set; } = null!;

    public TickLimiter ChatLimiter { get; } = RateLimits.Chat(tickRate);

    public TickLimiter JoinLimiter { get; } = RateLimits.Join(tickRate);

    public TickLimiter LookLimiter { get; } = RateLimits.Look(tickRate);

    public TickLimiter RenameLimiter { get; } = RateLimits.Rename(tickRate);

    public TickLimiter ReturnLimiter { get; } = RateLimits.Return(tickRate);

    /// <summary>Where the player goes on falling out of the world or returning; for the session only.</summary>
    public SpawnPoint SpawnPoint { get; set; } = null!;

    /// <summary>The zone the player is in, once joined.</summary>
    public ZoneState Zone { get; set; } = null!;

    /// <summary>Times the player standing in spawn points, to claim them.</summary>
    public TriggerCharge SpawnCharge { get; } = new();

    /// <summary>Times the player standing in teleporters.</summary>
    public TriggerCharge TeleportCharge { get; } = new();

    /// <summary>The tick the connection arrived; it must join within <see cref="ShapeLandRules.JoinDeadlineSeconds"/>.</summary>
    public uint ConnectedTick { get; init; }

    /// <summary>Stamps the player's states for others.</summary>
    public StateStamp Stamp { get; } = new(tickRate);

    /// <summary>The tick the current position is from (see <see cref="StateStamp"/>).</summary>
    public uint StateTick { get; set; }

    /// <summary>How often the server has taken over this player's fall because they went silent in the air.</summary>
    public int ServerFalls { get; set; }

    /// <summary>Accepted a new position this tick, to send to everyone else.</summary>
    public bool Moved { get; set; }

    /// <summary>The server placed the player itself (a spawn): stamped with the server's own tick.</summary>
    public void PlacedByServer(uint tick)
    {
        Stamp.StampServer(tick);
        StateTick = Stamp.Broadcast;
    }

    /// <summary>The server moved the player itself (a respawn, a fall it finished): stamped, and sent to everyone else.</summary>
    public void MovedByServer(uint tick)
    {
        PlacedByServer(tick);
        Moved = true;
    }

    public EntityState State() =>
        new()
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

    public PlayerSpawn Spawn() =>
        new()
        {
            EntityId = EntityId,
            Name = Name,
            Shape = Shape.Number,
            Color = Character.Color,
            EyeColor = Character.EyeColor,
            X = Validator.Position.X,
            Y = Validator.Position.Y,
            Z = Validator.Position.Z,
            Facing = Validator.Facing,
        };
}
