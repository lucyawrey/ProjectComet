using System.Numerics;
using System.Text.Json.Serialization;
using MessagePack;

namespace Comet.Content
{
    /// <summary>
    /// Something placed in zones (a kit piece, a prop, a primitive), authored as a TOML entry naming its model. The
    /// content build reads the model and bakes its collision triangles and bounds into the content file, so server
    /// and client collide against the same triangles.
    /// </summary>
    [MessagePackObject]
    public sealed class Asset : IContentEntry
    {
        /// <summary>Marks the objects in a model that make up its collision mesh (<c>Cube-col</c>).</summary>
        public const string CollisionSuffix = "-col";

        [JsonIgnore]
        [Key(0)] public int Number { get; set; }

        [JsonRequired]
        [Key(1)] public string Id { get; set; } = "";

        /// <summary>The <c>.glb</c> file, relative to the game's model folder (in ShapeLand, the Unity project's <c>Assets</c>).</summary>
        [JsonRequired]
        [Key(2)] public string Model { get; set; } = "";

        /// <summary>Whether placements may only scale it the same on every axis (for detailed props).</summary>
        [Key(3)] public bool UniformScale { get; set; }

        /// <summary>
        /// The collision mesh in the asset's own space, in meters, three corners per triangle, wound clockwise seen
        /// from outside (Unity's convention). Taken from the model's objects named with <see cref="CollisionSuffix"/>,
        /// or from all its meshes if it has none.
        /// </summary>
        [JsonIgnore]
        [Key(4)] public Vector3[] Collision { get; set; } = new Vector3[0];

        /// <summary>The low corner of the box around all the model's meshes, in its own space.</summary>
        [JsonIgnore]
        [Key(5)] public Vector3 BoundsMin { get; set; }

        /// <summary>The high corner of that box.</summary>
        [JsonIgnore]
        [Key(6)] public Vector3 BoundsMax { get; set; }

        /// <summary>The color placements of it are drawn in as 0xRRGGBB, unless they have their own; null for the game's placed-object color.</summary>
        [Key(7)] public uint? Color { get; set; }
    }
}
