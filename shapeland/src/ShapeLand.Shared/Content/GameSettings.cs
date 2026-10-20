using System.Text.Json.Serialization;
using Comet.Content;
using Comet.Simulation;
using MessagePack;

namespace ShapeLand.Shared.Content
{
    /// <summary>
    /// ShapeLand's game-wide tunable values (<c>content/settings/game.toml</c>, the one entry of its type): movement
    /// physics, the world's look and limits, spawn points, teleporters and triggers, and the color sets players pick from.
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

        /// <summary>How finely round primitives are cut, the same for all of them (players' shapes are regular solids, so it doesn't change them).</summary>
        [JsonRequired]
        [Key(6)] public PolygonBudget Primitives { get; set; } = new PolygonBudget();

        [JsonRequired]
        [Key(7)] public TeleporterSettings Teleporters { get; set; } = new TeleporterSettings();

        [JsonRequired]
        [Key(8)] public TriggerSettings Triggers { get; set; } = new TriggerSettings();

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

        /// <summary>The color placed objects are drawn in when neither they nor their prop have one, as 0xRRGGBB.</summary>
        [JsonRequired] [Key(4)] public uint PlacedColor { get; set; }
    }

    /// <summary>Spawn points: the editor's defaults for new ones (each keeps its own after) and how pads are drawn.</summary>
    [MessagePackObject]
    public sealed class SpawnSettings
    {
        /// <summary>How long a player stands in a new spawn point to make it theirs, in seconds.</summary>
        [JsonRequired] [Key(0)] public float StandSeconds { get; set; }

        /// <summary>The primitive kind of a new spawn point's pad, flattened under it.</summary>
        [JsonRequired] [Key(1)] public string Pad { get; set; } = "";

        /// <summary>How tall a new pad is, in meters, sunk halfway into the ground; it can be moved in the editor after.</summary>
        [JsonRequired] [Key(2)] public float PadHeight { get; set; }

        /// <summary>A pad's color while it isn't the player's spawn, as 0xRRGGBB: a pale color used nowhere else.</summary>
        [JsonRequired] [Key(3)] public uint UnsetColor { get; set; }

        /// <summary>How tall a new spawn point's volume is, in meters, above its pad.</summary>
        [JsonRequired] [Key(4)] public float VolumeHeight { get; set; }
    }

    /// <summary>Teleporters: the editor's defaults for new ones (each keeps its own after).</summary>
    [MessagePackObject]
    public sealed class TeleporterSettings
    {
        /// <summary>How long a player stands in a new teleporter before it sends them, in seconds.</summary>
        [JsonRequired] [Key(0)] public float StandSeconds { get; set; }

        /// <summary>The primitive kind of a new teleporter's pad, flattened under it.</summary>
        [JsonRequired] [Key(1)] public string Pad { get; set; } = "";

        [JsonRequired] [Key(2)] public float PadHeight { get; set; }

        /// <summary>A new pad's color, as 0xRRGGBB.</summary>
        [JsonRequired] [Key(3)] public uint PadColor { get; set; }

        /// <summary>How tall a new teleporter's volume is, in meters, above its pad.</summary>
        [JsonRequired] [Key(4)] public float VolumeHeight { get; set; }
    }

    /// <summary>Triggers with toggle effects: the editor's defaults for new ones (each keeps its own after; new ones have no pad).</summary>
    [MessagePackObject]
    public sealed class TriggerSettings
    {
        /// <summary>How long a player stands in a new trigger before it fires, in seconds (0 fires at once).</summary>
        [JsonRequired] [Key(0)] public float StandSeconds { get; set; }

        /// <summary>How tall a new trigger's volume is, in meters.</summary>
        [JsonRequired] [Key(1)] public float VolumeHeight { get; set; }
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
