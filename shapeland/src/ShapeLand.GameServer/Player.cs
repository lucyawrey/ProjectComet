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

    public bool Joined { get; set; }

    public string Name { get; set; } = "";

    public Shape Shape { get; set; } = null!;

    public uint Color { get; set; }

    public uint EyeColor { get; set; }

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

    /// <summary>
    /// The server moved the player itself (a spawn, a respawn, a fall it finished): stamped with the server's own
    /// tick, and sent to everyone else.
    /// </summary>
    public void MovedByServer(uint tick)
    {
        Stamp.StampServer(tick);
        StateTick = Stamp.Broadcast;
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
