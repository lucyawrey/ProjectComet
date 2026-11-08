using System.Collections.Generic;
using System.Text.Json.Serialization;
using Comet.Content;
using Comet.Simulation;
using MessagePack;

namespace ShapeLand.Shared.Content
{
    [MessagePackObject]
    public sealed class GameSettings : IContentEntry
    {
        [JsonIgnore]
        [Key(0)]
        public int Number { get; set; }

        [JsonRequired]
        [Key(1)]
        public string Id { get; set; } = "";

        [JsonRequired]
        [Key(2)]
        public MovementSettings Movement { get; set; } = new MovementSettings();

        [JsonRequired]
        [Key(3)]
        public WorldSettings World { get; set; } = new WorldSettings();

        [JsonRequired]
        [Key(4)]
        public SpawnSettings Spawns { get; set; } = new SpawnSettings();

        /// <summary>
        /// By key, as 0xRRGGBB, in the order they're shown. Compiled into <see cref="ShapeLandContent.Colors"/>.
        /// </summary>
        [JsonRequired]
        [IgnoreMember]
        public Dictionary<string, uint> Colors { get; set; } = new Dictionary<string, uint>();

        /// <summary>The same for all round primitives; it doesn't change players' shapes (regular solids).</summary>
        [JsonRequired]
        [Key(6)]
        public PolygonBudget Primitives { get; set; } = new PolygonBudget();

        [JsonRequired]
        [Key(7)]
        public TeleporterSettings Teleporters { get; set; } = new TeleporterSettings();

        [JsonRequired]
        [Key(8)]
        public TriggerSettings Triggers { get; set; } = new TriggerSettings();

        public MovementRules Rules() =>
            new MovementRules
            {
                Gravity = Movement.Gravity,
                BodyRadius = Movement.BodyRadius,
                BodyHeight = Movement.BodyHeight,
                StepHeight = Movement.StepHeight,
                GroundSnap = Movement.GroundSnap,
                GroundAcceleration = Movement.GroundAcceleration,
                AirAcceleration = Movement.AirAcceleration,
                TurnSpeed = Movement.TurnSpeed,
                MaxSlope = Movement.MaxSlope,
                MantleHeight = Movement.MantleHeight,
            };
    }

    /// <summary>In meters, seconds and degrees.</summary>
    [MessagePackObject]
    public sealed class MovementSettings
    {
        [JsonRequired]
        [Key(0)]
        public float Gravity { get; set; }

        /// <summary>Every shape shares this collision body, an upright cylinder.</summary>
        [JsonRequired]
        [Key(1)]
        public float BodyRadius { get; set; }

        [JsonRequired]
        [Key(2)]
        public float BodyHeight { get; set; }

        [JsonRequired]
        [Key(3)]
        public float StepHeight { get; set; }

        [JsonRequired]
        [Key(4)]
        public float GroundSnap { get; set; }

        [JsonRequired]
        [Key(5)]
        public float GroundAcceleration { get; set; }

        [JsonRequired]
        [Key(6)]
        public float AirAcceleration { get; set; }

        [JsonRequired]
        [Key(7)]
        public float TurnSpeed { get; set; }

        /// <summary>In degrees; steeper is a wall.</summary>
        [JsonRequired]
        [Key(8)]
        public float MaxSlope { get; set; }

        /// <summary>The highest ledge an airborne player is lifted onto when moving into its side.</summary>
        [JsonRequired]
        [Key(9)]
        public float MantleHeight { get; set; }
    }

    [MessagePackObject]
    public sealed class WorldSettings
    {
        /// <summary>In meters; falling below it sends the player back to their spawn.</summary>
        [JsonRequired]
        [Key(0)]
        public float KillHeight { get; set; }

        /// <summary>For pieces without their own, as 0xRRGGBB.</summary>
        [JsonRequired]
        [Key(1)]
        public uint GroundColor { get; set; }

        /// <summary>Terrain's unpainted undersides, for pieces without their own, as 0xRRGGBB.</summary>
        [JsonRequired]
        [Key(2)]
        public uint UnderColor { get; set; }

        /// <summary>For placed objects when neither they nor their prop have one, as 0xRRGGBB.</summary>
        [JsonRequired]
        [Key(4)]
        public uint PlacedColor { get; set; }
    }

    /// <summary>The editor's defaults for new ones; each keeps its own after.</summary>
    [MessagePackObject]
    public sealed class SpawnSettings
    {
        /// <summary>In seconds.</summary>
        [JsonRequired]
        [Key(0)]
        public float StandSeconds { get; set; }

        [JsonRequired]
        [Key(1)]
        public string Pad { get; set; } = "";

        /// <summary>In meters; the pad is sunk halfway into the ground.</summary>
        [JsonRequired]
        [Key(2)]
        public float PadHeight { get; set; }

        /// <summary>As 0xRRGGBB: a pale color used nowhere else.</summary>
        [JsonRequired]
        [Key(3)]
        public uint UnsetColor { get; set; }

        /// <summary>In meters, above its pad.</summary>
        [JsonRequired]
        [Key(4)]
        public float VolumeHeight { get; set; }
    }

    /// <summary>The editor's defaults for new ones; each keeps its own after.</summary>
    [MessagePackObject]
    public sealed class TeleporterSettings
    {
        /// <summary>In seconds.</summary>
        [JsonRequired]
        [Key(0)]
        public float StandSeconds { get; set; }

        [JsonRequired]
        [Key(1)]
        public string Pad { get; set; } = "";

        [JsonRequired]
        [Key(2)]
        public float PadHeight { get; set; }

        [JsonRequired]
        [Key(3)]
        public uint PadColor { get; set; }

        /// <summary>In meters, above its pad.</summary>
        [JsonRequired]
        [Key(4)]
        public float VolumeHeight { get; set; }

        /// <summary>As 0xRRGGBB; also for teleporters without their own.</summary>
        [JsonRequired]
        [Key(5)]
        public uint ChargeColor { get; set; }
    }

    /// <summary>The editor's defaults for new ones; each keeps its own after. New ones have no pad.</summary>
    [MessagePackObject]
    public sealed class TriggerSettings
    {
        /// <summary>In seconds; 0 fires at once.</summary>
        [JsonRequired]
        [Key(0)]
        public float StandSeconds { get; set; }

        /// <summary>In meters.</summary>
        [JsonRequired]
        [Key(1)]
        public float VolumeHeight { get; set; }

        /// <summary>As 0xRRGGBB; also for triggers without their own.</summary>
        [JsonRequired]
        [Key(2)]
        public uint ChargeColor { get; set; }
    }
}
