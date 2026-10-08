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

        /// <summary>How the shape is drawn.</summary>
        [JsonRequired]
        [Key(6)] public ShapeLook Look { get; set; } = new ShapeLook();
    }

    /// <summary>
    /// A shape's drawn size and eyes, in metres. Looks only: every shape collides as the one shared body in
    /// <c>MovementRules</c> (0.4 m radius, 1 m tall), so a shape drawn much bigger or smaller than it may poke
    /// into blocks or stop short of them.
    /// </summary>
    [MessagePackObject]
    public sealed class ShapeLook
    {
        /// <summary>Size from side to side.</summary>
        [JsonRequired]
        [Key(0)] public float Width { get; set; }

        /// <summary>Size from front to back.</summary>
        [JsonRequired]
        [Key(1)] public float Depth { get; set; }

        [JsonRequired]
        [Key(2)] public float Height { get; set; }

        /// <summary>How far above its feet the shape is drawn.</summary>
        [JsonRequired]
        [Key(3)] public float Hover { get; set; }

        /// <summary>Diamonds only: how far above the bottom tip the widest ring is.</summary>
        [Key(4)] public float Waist { get; set; }

        /// <summary>Each eye's width.</summary>
        [JsonRequired]
        [Key(5)] public float EyeWidth { get; set; }

        /// <summary>Each eye's height.</summary>
        [JsonRequired]
        [Key(6)] public float EyeHeight { get; set; }

        /// <summary>From one eye's centre to the other's.</summary>
        [JsonRequired]
        [Key(7)] public float EyeSpacing { get; set; }

        /// <summary>How far above the shape's bottom the eyes' centres are; they sit on the front face there, tilted with it.</summary>
        [JsonRequired]
        [Key(8)] public float EyeLevel { get; set; }
    }
}
