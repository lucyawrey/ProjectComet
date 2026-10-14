using Comet.Content;
using Comet.Protocol.Messages;
using Comet.Server.Connections;
using Comet.Server.Movement;
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

    public string Name { get; set; } = "";

    public Shape Shape { get; set; } = null!;

    /// <summary>The body's color, as 0xRRGGBB.</summary>
    public uint Color { get; set; }

    /// <summary>The eyes' color, as 0xRRGGBB.</summary>
    public uint EyeColor { get; set; }

    /// <summary>Checks the player's position reports; made on joining.</summary>
    public MovementValidator Validator { get; set; } = null!;

    public TickLimiter ChatLimiter { get; } = TickLimiter.Chat(tickRate);

    public TickLimiter JoinLimiter { get; } = TickLimiter.Join(tickRate);

    public TickLimiter ReturnLimiter { get; } = TickLimiter.Return(tickRate);

    /// <summary>Where the player goes on falling out of the world or returning; for the session only.</summary>
    public SpawnPoint SpawnPoint { get; set; } = null!;

    /// <summary>Another spawn point the player is standing in, claiming it, and the tick they stepped in; null if none.</summary>
    public (SpawnPoint Spawn, uint Since)? Claiming { get; set; }

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
        Color = Color,
        EyeColor = EyeColor,
        X = Validator.Position.X,
        Y = Validator.Position.Y,
        Z = Validator.Position.Z,
        Facing = Validator.Facing,
    };
}
