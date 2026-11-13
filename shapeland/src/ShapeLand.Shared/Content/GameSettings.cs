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

        [JsonRequired]
        [IgnoreMember]
        public Dictionary<string, uint> Colors { get; set; } = new Dictionary<string, uint>();

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

    [MessagePackObject]
    public sealed class MovementSettings
    {
        [JsonRequired]
        [Key(0)]
        public float Gravity { get; set; }

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

        [JsonRequired]
        [Key(8)]
        public float MaxSlope { get; set; }

        [JsonRequired]
        [Key(9)]
        public float MantleHeight { get; set; }
    }

    [MessagePackObject]
    public sealed class WorldSettings
    {
        [JsonRequired]
        [Key(0)]
        public float KillHeight { get; set; }

        [JsonRequired]
        [Key(1)]
        public uint GroundColor { get; set; }

        [JsonRequired]
        [Key(2)]
        public uint UnderColor { get; set; }

        [JsonRequired]
        [Key(4)]
        public uint PlacedColor { get; set; }
    }

    [MessagePackObject]
    public sealed class SpawnSettings
    {
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
        public uint UnsetColor { get; set; }

        [JsonRequired]
        [Key(4)]
        public float VolumeHeight { get; set; }
    }

    [MessagePackObject]
    public sealed class TeleporterSettings
    {
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

        [JsonRequired]
        [Key(4)]
        public float VolumeHeight { get; set; }

        [JsonRequired]
        [Key(5)]
        public uint ChargeColor { get; set; }
    }

    [MessagePackObject]
    public sealed class TriggerSettings
    {
        [JsonRequired]
        [Key(0)]
        public float StandSeconds { get; set; }

        [JsonRequired]
        [Key(1)]
        public float VolumeHeight { get; set; }

        [JsonRequired]
        [Key(2)]
        public uint ChargeColor { get; set; }
    }
}
