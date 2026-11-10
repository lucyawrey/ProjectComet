using Comet.Content;
using Tomlyn.Model;

namespace Comet.ZoneFiles
{
    public static partial class ZoneFolder
    {
        // Standalone signs have a position; a trigger's has an offset from its volume's bottom center.
        private static Sign ReadSign(ZoneToml toml, TomlTable table, string path, string positionKey)
        {
            toml.CheckKeys(
                table,
                path,
                positionKey,
                "text",
                "text_color",
                "background",
                "background_color",
                "background_opacity",
                "show_distance",
                "screen_sized",
                "font_size",
                "text_height",
                "arrow",
                "off"
            );
            return new Sign
            {
                Position = toml.Vector(table, path, positionKey),
                Text = toml.String(table, path, "text"),
                TextColor = table.TryGetValue("text_color", out var text) ? Color(toml, text, $"{path}.text_color") ?? 0xFFFFFF : 0xFFFFFF,
                Background = toml.Bool(table, path, "background", false),
                BackgroundColor = table.TryGetValue("background_color", out var back) ? Color(toml, back, $"{path}.background_color") ?? 0 : 0,
                BackgroundOpacity = toml.Float(table, path, "background_opacity", 1),
                ShowDistance = toml.Float(table, path, "show_distance", 45),
                ScreenSized = toml.Bool(table, path, "screen_sized", false),
                FontSize = toml.Float(table, path, "font_size", 13),
                TextHeight = toml.Float(table, path, "text_height", 0.3f),
                Arrow = toml.Bool(table, path, "arrow", false),
                Off = toml.Bool(table, path, "off", false),
            };
        }

        private static TomlTable SignTable(Sign t, string positionKey)
        {
            var sign = new TomlTable
            {
                [positionKey] = ZoneToml.Array3(t.Position, LengthDecimals),
                ["text"] = t.Text,
                ["text_color"] = (long)t.TextColor,
            };
            if (t.Background)
            {
                sign["background"] = true;
            }

            if (t.BackgroundColor != 0)
            {
                sign["background_color"] = (long)t.BackgroundColor;
            }

            if (t.BackgroundOpacity != 1)
            {
                sign["background_opacity"] = ZoneToml.Round(t.BackgroundOpacity, OpacityDecimals);
            }

            sign["show_distance"] = ZoneToml.Round(t.ShowDistance, LengthDecimals);
            if (t.ScreenSized)
            {
                sign["screen_sized"] = true;
            }

            if (t.ScreenSized || t.FontSize != new Sign().FontSize)
            {
                sign["font_size"] = ZoneToml.Round(t.FontSize, FontSizeDecimals);
            }

            if (!t.ScreenSized || t.TextHeight != new Sign().TextHeight)
            {
                sign["text_height"] = ZoneToml.Round(t.TextHeight, LengthDecimals);
            }

            if (t.Arrow)
            {
                sign["arrow"] = true;
            }

            if (t.Off)
            {
                sign["off"] = true;
            }

            return sign;
        }
    }
}
