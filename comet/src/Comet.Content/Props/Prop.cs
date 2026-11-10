using System;
using System.Numerics;
using System.Text.Json.Serialization;
using MessagePack;

namespace Comet.Content
{
    [MessagePackObject]
    public sealed class Prop : IContentEntry
    {
        public const string CollisionSuffix = "-col";

        [JsonIgnore]
        [Key(0)]
        public int Number { get; set; }

        [JsonRequired]
        [Key(1)]
        public string Id { get; set; } = "";

        /// <summary>The <c>.glb</c> file, relative to the game's model folder.</summary>
        [JsonRequired]
        [Key(2)]
        public string Model { get; set; } = "";

        [Key(3)]
        public bool UniformScale { get; set; }

        /// <summary>Three corners per triangle, in meters, clockwise from outside; from <see cref="CollisionSuffix"/> objects, else all.</summary>
        [JsonIgnore]
        [Key(4)]
        public Vector3[] Collision { get; set; } = Array.Empty<Vector3>();

        [JsonIgnore]
        [Key(5)]
        public Vector3 BoundsMin { get; set; }

        [JsonIgnore]
        [Key(6)]
        public Vector3 BoundsMax { get; set; }

        /// <summary>Placements' 0xRRGGBB color unless they have their own; null for the game's placed-object color.</summary>
        [Key(7)]
        public uint? Color { get; set; }

        /// <summary>The name the zone editor shows; null shows its id without the <c>prop.</c> prefix.</summary>
        [Key(8)]
        public string? DisplayName { get; set; }
    }
}
