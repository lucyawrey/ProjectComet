using System.Text.Json.Serialization;
using Comet.Content;
using Comet.Simulation;
using MessagePack;

namespace ShapeLand.Shared.Content
{
    /// <summary>
    /// ShapeLand's game-wide tunable values (<c>content/settings/game.toml</c>, the one entry of its type): movement
    /// physics, the world's look and limits, spawn points and the color sets players pick from.
    /// </summary>
    [MessagePackObject]
    public sealed class GameSettings : IContentEntry
    {
        [JsonIgnore]
        [Key(0)] public int Number { get; set; }

        [JsonRequired]
        [Key(1)] public string Id { get; set; } = "";

        /// <summary>Movement physics, the same for every shape (each shape's speed, jump and scale are in its own file).</summary>
        [JsonRequired]
        [Key(2)] public MovementSettings Movement { get; set; } = new MovementSettings();

        [JsonRequired]
        [Key(3)] public WorldSettings World { get; set; } = new WorldSettings();

        [JsonRequired]
        [Key(4)] public SpawnSettings Spawns { get; set; } = new SpawnSettings();

        [JsonRequired]
        [Key(5)] public ColorSettings Colors { get; set; } = new ColorSettings();

        /// <summary>The movement physics as the shared motor and the server's checks take them.</summary>
        public MovementRules Rules() => new MovementRules
        {
            Gravity = Movement.Gravity,
            BodyRadius = Movement.BodyRadius,
            BodyHeight = Movement.BodyHeight,
            StepHeight = Movement.StepHeight,
            GroundSnap = Movement.GroundSnap,
            GroundAcceleration = Movement.GroundAcceleration,
            AirAcceleration = Movement.AirAcceleration,
            TurnSpeed = Movement.TurnSpeed,
        };
    }

    /// <summary>The values of Comet's <see cref="MovementRules"/>, in meters, seconds and degrees.</summary>
    [MessagePackObject]
    public sealed class MovementSettings
    {
        [JsonRequired] [Key(0)] public float Gravity { get; set; }

        /// <summary>The collision body every shape shares: a square footprint this far from the center.</summary>
        [JsonRequired] [Key(1)] public float BodyRadius { get; set; }

        [JsonRequired] [Key(2)] public float BodyHeight { get; set; }

        [JsonRequired] [Key(3)] public float StepHeight { get; set; }

        [JsonRequired] [Key(4)] public float GroundSnap { get; set; }

        [JsonRequired] [Key(5)] public float GroundAcceleration { get; set; }

        [JsonRequired] [Key(6)] public float AirAcceleration { get; set; }

        [JsonRequired] [Key(7)] public float TurnSpeed { get; set; }
    }

    [MessagePackObject]
    public sealed class WorldSettings
    {
        /// <summary>Falling below this height, in meters, is out of the world: the player goes back to their spawn.</summary>
        [JsonRequired] [Key(0)] public float KillHeight { get; set; }

        /// <summary>The color of terrain no one has painted, for pieces without their own, as 0xRRGGBB.</summary>
        [JsonRequired] [Key(1)] public uint GroundColor { get; set; }

        /// <summary>The color of terrain's unpainted undersides (the walls and bottoms where its ground ends), for pieces without their own, as 0xRRGGBB.</summary>
        [JsonRequired] [Key(2)] public uint UnderColor { get; set; }

        /// <summary>The color placed objects are drawn in when neither they nor their asset have one, as 0xRRGGBB.</summary>
        [JsonRequired] [Key(4)] public uint PlacedColor { get; set; }

        /// <summary>The zone the game plays in (<c>zone.*</c>); it has one at a time for now.</summary>
        [JsonRequired] [Key(3)] public string Zone { get; set; } = "";
    }

    [MessagePackObject]
    public sealed class SpawnSettings
    {
        /// <summary>How long a player stands inside another spawn point's radius to make it theirs, in seconds.</summary>
        [JsonRequired] [Key(0)] public float ClaimSeconds { get; set; }

        /// <summary>The asset drawn as a spawn point's marker, flattened (the editor places it with each new spawn).</summary>
        [JsonRequired] [Key(1)] public string MarkerAsset { get; set; } = "";

        /// <summary>How tall a new marker is, in meters, sunk halfway into the ground; it can be moved in the editor after.</summary>
        [JsonRequired] [Key(2)] public float MarkerHeight { get; set; }

        /// <summary>A marker's color while it isn't the player's spawn, as 0xRRGGBB: a grey used nowhere else.</summary>
        [JsonRequired] [Key(3)] public uint UnsetColor { get; set; }
    }

    [MessagePackObject]
    public sealed class ColorSettings
    {
        /// <summary>The body colors every player can pick, as 0xRRGGBB; more come from items later.</summary>
        [JsonRequired] [Key(0)] public uint[] Body { get; set; } = new uint[0];

        /// <summary>The eye colors every player can pick, as 0xRRGGBB.</summary>
        [JsonRequired] [Key(1)] public uint[] Eyes { get; set; } = new uint[0];
    }
}
