using System;
using System.Numerics;
using System.Text.Json.Serialization;
using MessagePack;

namespace Comet.Content
{
    /// <summary>
    /// A model placed in zones. The content build bakes its collision triangles and bounds into the content file, so server and client collide alike.
    /// </summary>
    [MessagePackObject]
    public sealed class Prop : IContentEntry
    {
        /// <summary>Marks the objects in a model that make up its collision mesh (<c>Cube-col</c>).</summary>
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

        /// <summary>Whether placements may only scale it evenly on every axis (for detailed models that look wrong stretched).</summary>
        [Key(3)]
        public bool UniformScale { get; set; }

        /// <summary>
        /// The collision mesh in the prop's own space, in meters, three corners per triangle, wound clockwise from outside.
        /// Taken from objects named with <see cref="CollisionSuffix"/>, else all meshes.
        /// </summary>
        [JsonIgnore]
        [Key(4)]
        public Vector3[] Collision { get; set; } = Array.Empty<Vector3>();

        /// <summary>The low corner of the box around all its meshes, in its own space.</summary>
        [JsonIgnore]
        [Key(5)]
        public Vector3 BoundsMin { get; set; }

        /// <summary>The high corner of that box.</summary>
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
