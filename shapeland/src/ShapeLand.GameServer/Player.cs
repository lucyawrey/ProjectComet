using Comet.Content;
using Comet.Protocol.Messages;
using Comet.Server.Connections;
using Comet.Server.Movement;
using Comet.Server.Ticking;
using Comet.Simulation;
using ShapeLand.Data;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;

namespace ShapeLand.GameServer;

/// <summary>Tick thread only.</summary>
internal sealed class Player(Connection connection, int tickRate)
{
    public Connection Connection { get; } = connection;

    public uint EntityId => Connection.Id;

    public bool Joined { get; set; }

    public bool Joining { get; set; }

    public bool Dropped { get; set; }

    public uint CloseAtTick { get; set; }

    public ShapeLandCharacter Character { get; set; } = null!;

    public string Name => Character.Name;

    public Shape Shape { get; set; } = null!;

    public MovementValidator Validator { get; set; } = null!;

    public TickLimiter ChatLimiter { get; } = RateLimits.Chat(tickRate);

    public TickLimiter JoinLimiter { get; } = RateLimits.Join(tickRate);

    public TickLimiter LookLimiter { get; } = RateLimits.Look(tickRate);

    public TickLimiter RenameLimiter { get; } = RateLimits.Rename(tickRate);

    public TickLimiter ReturnLimiter { get; } = RateLimits.Return(tickRate);

    /// <summary>Not saved: lasts for the session only.</summary>
    public SpawnPoint SpawnPoint { get; set; } = null!;

    public ZoneState Zone { get; set; } = null!;

    public TriggerCharge SpawnCharge { get; } = new();

    public TriggerCharge TeleportCharge { get; } = new();

    public uint ConnectedTick { get; init; }

    public StateStamp Stamp { get; } = new(tickRate);

    public uint StateTick { get; set; }

    public int ServerFalls { get; set; }

    public bool Moved { get; set; }

    public void PlacedByServer(uint tick)
    {
        Stamp.StampServer(tick);
        StateTick = Stamp.Broadcast;
    }

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
