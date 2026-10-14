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

        /// <summary>Top ground speed, in meters per second.</summary>
        [JsonRequired]
        [Key(4)] public float MaxSpeed { get; set; }

        /// <summary>Upward speed at the start of a jump, in meters per second.</summary>
        [JsonRequired]
        [Key(5)] public float JumpVelocity { get; set; }

        /// <summary>
        /// How big the shape is drawn, times its model's size (1 is as modeled). Looks only: every shape collides as
        /// the one shared body in <c>MovementRules</c>.
        /// </summary>
        [JsonRequired]
        [Key(6)] public float Scale { get; set; }

        /// <summary>
        /// The spawn point (<c>spawn.*</c>) a player of this shape starts at when they join, until they claim another.
        /// Changing shape later doesn't change a player's spawn.
        /// </summary>
        [JsonRequired]
        [Key(7)] public string Spawn { get; set; } = "";
    }
}
