using System.Text.Json.Serialization;
using Comet.Content;
using MessagePack;

namespace ShapeLand.Shared.Content
{
    /// <summary>The number is permanent: messages and saved characters carry it.</summary>
    [MessagePackObject]
    public sealed class PlayerColor : IContentEntry
    {
        [JsonIgnore]
        [Key(0)]
        public int Number { get; set; }

        [Key(1)]
        public string Id { get; set; } = "";

        [Key(2)]
        public uint Rgb { get; set; }
    }
}
