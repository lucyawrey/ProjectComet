using System.Text.Json.Serialization;
using Comet.Content;
using MessagePack;

namespace ShapeLand.Shared.Content
{
    /// <summary>The meshes a player's shape can use.</summary>
    public enum MeshKind
    {
        Cube,
        Diamond,
        Pyramid,
    }

    /// <summary>A playable shape: how it looks and moves. Read by both client and server, so movement validation uses content.</summary>
    [MessagePackObject]
    public sealed class Shape : IContentEntry
    {
        [JsonIgnore]
        [Key(0)] public int Number { get; set; }

        [JsonRequired]
        [Key(1)] public string Id { get; set; } = "";

        [JsonRequired]
        [Key(2)] public string DisplayName { get; set; } = "";

        [JsonRequired]
        [Key(3)] public MeshKind Mesh { get; set; }

        /// <summary>Top ground speed, in metres per second.</summary>
        [JsonRequired]
        [Key(4)] public float MaxSpeed { get; set; }

        /// <summary>Upward speed at the start of a jump, in metres per second.</summary>
        [JsonRequired]
        [Key(5)] public float JumpVelocity { get; set; }
    }
}
