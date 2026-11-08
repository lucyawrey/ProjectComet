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

        /// <summary>
        /// Parts with the role <c>body</c> take the player's body color, <c>eyes</c> their eye color, others keep their
        /// own.
        /// </summary>
        [JsonRequired]
        [Key(3)]
        public string Look { get; set; } = "";

        /// <summary>In meters per second.</summary>
        [JsonRequired]
        [Key(4)]
        public float MaxSpeed { get; set; }

        /// <summary>In meters per second.</summary>
        [JsonRequired]
        [Key(5)]
        public float JumpVelocity { get; set; }

        /// <summary>Looks only: every shape collides as the one shared body in <c>MovementRules</c>.</summary>
        [JsonRequired]
        [Key(6)]
        public float Scale { get; set; }

        /// <summary>Changing shape later doesn't change a player's spawn.</summary>
        [JsonRequired]
        [Key(7)]
        public string Spawn { get; set; } = "";

        /// <summary>In meters, before <see cref="Scale"/>; 0 to stand on the ground.</summary>
        [Key(8)]
        public float Hover { get; set; }
    }
}
