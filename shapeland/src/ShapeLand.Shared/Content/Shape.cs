using System.Text.Json.Serialization;
using Comet.Content;
using MessagePack;

namespace ShapeLand.Shared.Content
{
    [MessagePackObject]
    public sealed class Shape : IContentEntry
    {
        [JsonIgnore]
        [Key(0)]
        public int Number { get; set; }

        [JsonRequired]
        [Key(1)]
        public string Id { get; set; } = "";

        [JsonRequired]
        [Key(2)]
        public string DisplayName { get; set; } = "";

        [JsonRequired]
        [Key(3)]
        public string Look { get; set; } = "";

        [JsonRequired]
        [Key(4)]
        public float MaxSpeed { get; set; }

        [JsonRequired]
        [Key(5)]
        public float JumpVelocity { get; set; }

        [JsonRequired]
        [Key(6)]
        public float Scale { get; set; }

        [JsonRequired]
        [Key(7)]
        public string Spawn { get; set; } = "";

        [Key(8)]
        public float Hover { get; set; }
    }
}
