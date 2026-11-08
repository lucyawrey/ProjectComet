using Comet.Content;
using Tomlyn.Model;

namespace Comet.ZoneFiles
{
    public static partial class ZoneFolder
    {
        // The keys every trigger kind's table has: its volume, stand time, re-arm and pad.
        private static readonly string[] VolumeKeys = { "volume", "position", "yaw", "size", "stand_seconds", "rearm", "pad", "sign" };

        private static TriggerVolume ReadVolume(ZoneToml toml, TomlTable table, string path)
        {
            var volume = new TriggerVolume
            {
                Kind = toml.Enum<PrimitiveKind>(table, path, "volume"),
                Position = toml.Vector(table, path, "position"),
                Yaw = toml.Float(table, path, "yaw", 0),
                Size = toml.Vector(table, path, "size"),
                StandSeconds = toml.Float(table, path, "stand_seconds", 0),
                Rearm = toml.Enum<Rearm>(table, path, "rearm", Rearm.StepOff),
            };
            if (table.TryGetValue("pad", out var pad))
            {
                if (pad is TomlTable padTable)
                {
                    volume.Pad = ReadPlacement(toml, padTable, $"{path}.pad");
                }
                else
                {
                    toml.Error($"{path}.pad", "should be a table, such as [spawn.pad].");
                }
            }

            if (table.TryGetValue("sign", out var sign))
            {
                if (sign is TomlTable signTable)
                {
                    volume.Sign = ReadSign(toml, signTable, $"{path}.sign", "offset");
                }
                else
                {
                    toml.Error($"{path}.sign", "should be a table, such as [teleporter.sign].");
                }
            }

            return volume;
        }

        private static void WriteVolume(TomlTable table, TriggerVolume volume)
        {
            table["volume"] = ZoneToml.SnakeCase(volume.Kind.ToString());
            table["position"] = ZoneToml.Array3(volume.Position, LengthDecimals);
            if (ZoneToml.Round(volume.Yaw, AngleDecimals) != 0)
            {
                table["yaw"] = ZoneToml.Round(volume.Yaw, AngleDecimals);
            }

            table["size"] = ZoneToml.Array3(volume.Size, LengthDecimals);
            if (volume.StandSeconds != 0)
            {
                table["stand_seconds"] = ZoneToml.Round(volume.StandSeconds, TimeDecimals);
            }

            if (volume.Rearm != Rearm.StepOff)
            {
                table["rearm"] = ZoneToml.SnakeCase(volume.Rearm.ToString());
            }
        }

        // The pad and sign go last, as TOML writes a subtable after its parent's values.
        private static TomlTable WriteSubtables(TomlTable table, TriggerVolume volume)
        {
            if (volume.Pad is { } pad)
            {
                table["pad"] = PlacementTable(pad);
            }

            if (volume.Sign is { } sign)
            {
                table["sign"] = SignTable(sign, "offset");
            }

            return table;
        }
    }
}
