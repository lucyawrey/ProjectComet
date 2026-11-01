using System.Text.Json.Serialization;
using Comet.Content;
using MessagePack;

namespace ShapeLand.Shared.Content
{
    /// <summary>
    /// A color players can pick for their body and eyes: one key of <c>[colors]</c> in <c>settings/game.toml</c>, with
    /// its permanent number (registry type <c>color</c>), which messages and saved characters carry.
    /// </summary>
    [MessagePackObject]
    public sealed class PlayerColor : IContentEntry
    {
        [JsonIgnore]
        [Key(0)] public int Number { get; set; }

        /// <summary>The key, as <c>color.name</c>.</summary>
        [Key(1)] public string Id { get; set; } = "";

        /// <summary>The color as 0xRRGGBB.</summary>
        [Key(2)] public uint Rgb { get; set; }
    }
}
