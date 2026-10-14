using Tomlyn;
using Tomlyn.Model;

namespace Comet.ZoneFiles
{
    /// <summary>Reads and writes zone TOML through Tomlyn's table model, which needs no reflection or source generator.</summary>
    internal static class ZoneToml
    {
        public static TomlTable Parse(string text) => TomlSerializer.Deserialize<TomlTable>(text, TomlSerializerOptions.Default)!;

        public static string Write(TomlTable table) => TomlSerializer.Serialize(table, TomlSerializerOptions.Default);
    }
}
