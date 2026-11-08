using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Comet.Content;
using Tomlyn.Model;

namespace Comet.ZoneFiles
{
    public static partial class ZoneFolder
    {
        /// <summary>A placement's table (a zone's <c>[[placement]]</c> or a group's <c>[[part]]</c>): one of prop, primitive or group, and its transform.</summary>
        internal static Placement ReadPlacement(ZoneToml toml, TomlTable table, string path)
        {
            toml.CheckKeys(
                table,
                path,
                "prop",
                "primitive",
                "group",
                "sides",
                "bevel",
                "steps",
                "opening",
                "thickness",
                "blockout",
                "off",
                "tag",
                "role",
                "position",
                "rotation",
                "scale",
                "color",
                "colors"
            );
            if (new[] { "prop", "primitive", "group" }.Count(table.ContainsKey) != 1)
            {
                toml.Error(path, "a placement places one of: a prop, a primitive or a group.");
            }

            var primitive = ReadPrimitive(toml, table, path);
            var group = primitive is null && table.ContainsKey("group") ? toml.String(table, path, "group") : null;
            return new Placement
            {
                Prop = primitive is null && group is null && table.ContainsKey("prop") ? toml.String(table, path, "prop") : "",
                Primitive = primitive,
                Group = group,
                Blockout = toml.Bool(table, path, "blockout", false),
                Off = toml.Bool(table, path, "off", false),
                Tag = table.ContainsKey("tag") ? toml.String(table, path, "tag") : null,
                Role = table.ContainsKey("role") ? toml.String(table, path, "role") : null,
                Position = toml.Vector(table, path, "position"),
                Rotation = toml.Vector(table, path, "rotation", Vector3.Zero),
                Scale = toml.Vector(table, path, "scale", Vector3.One),
                Color = table.TryGetValue("color", out var color) ? Color(toml, color, $"{path}.color") : null,
                Colors = table.TryGetValue("colors", out var colors) ? RoleColors(toml, colors, $"{path}.colors") : null,
            };
        }

        // A group copy's colors by role: a table of role = 0xRRGGBB.
        private static Dictionary<string, uint>? RoleColors(ZoneToml toml, object? value, string path)
        {
            if (value is not TomlTable table)
            {
                toml.Error(path, "should be a table of role = color, such as { leaves = 0x2F5D3A }.");
                return null;
            }

            var colors = new Dictionary<string, uint>();
            foreach (var (role, color) in table)
            {
                if (Color(toml, color, $"{path}.{role}") is { } read)
                {
                    colors[role] = read;
                }
            }

            return colors;
        }

        /// <summary>A placement as its table, leaving out the defaults most placements have, to keep the files short.</summary>
        internal static TomlTable PlacementTable(Placement p)
        {
            var placement = new TomlTable();
            if (p.Primitive is { } primitive)
            {
                WritePrimitive(placement, primitive);
            }
            else if (p.Group is { } group)
            {
                placement["group"] = group;
            }
            else
            {
                placement["prop"] = p.Prop;
            }

            if (p.Blockout)
            {
                placement["blockout"] = true;
            }

            if (p.Off)
            {
                placement["off"] = true;
            }

            if (p.Tag is not null)
            {
                placement["tag"] = p.Tag;
            }

            if (p.Role is not null)
            {
                placement["role"] = p.Role;
            }

            placement["position"] = ZoneToml.Array3(p.Position, LengthDecimals);
            if (!IsZero(p.Rotation, AngleDecimals))
            {
                placement["rotation"] = ZoneToml.Array3(p.Rotation, AngleDecimals);
            }

            if (!IsZero(p.Scale - Vector3.One, LengthDecimals))
            {
                placement["scale"] = ZoneToml.Array3(p.Scale, LengthDecimals);
            }

            if (p.Color is { } color)
            {
                placement["color"] = (long)color;
            }

            if (p.Colors is { Count: > 0 } colors)
            {
                var table = new TomlTable(true);
                foreach (var (role, roleColor) in colors.OrderBy(c => c.Key, StringComparer.Ordinal))
                {
                    table[role] = (long)roleColor;
                }

                placement["colors"] = table;
            }

            return placement;
        }

        // A color as 0xRRGGBB (a TOML integer, usually written in hex).
        private static uint? Color(ZoneToml toml, object? value, string path)
        {
            if (value is long number && number >= 0 && number <= 0xFFFFFF)
            {
                return (uint)number;
            }

            toml.Error(path, "should be a color, 0x000000 to 0xFFFFFF.");
            return null;
        }

        // TOML writes integers in decimal; colors read better as hex, as the game's own content writes them.
        internal static string HexColors(string text)
        {
            text = Regex.Replace(
                text,
                @"^((?:ground_|under_|charge_|text_|background_)?color = )(\d+)$",
                m => $"{m.Groups[1].Value}0x{long.Parse(m.Groups[2].Value):X6}",
                RegexOptions.Multiline
            );
            return Regex.Replace(
                text,
                @"^colors = \{.*\}$",
                line => Regex.Replace(line.Value, @"= (\d+)", m => $"= 0x{long.Parse(m.Groups[1].Value):X6}"),
                RegexOptions.Multiline
            );
        }

        // A placement's primitive and its values, or null if it places a prop or a group.
        private static Primitive? ReadPrimitive(ZoneToml toml, TomlTable table, string path)
        {
            if (!table.ContainsKey("primitive"))
            {
                foreach (var key in new[] { "sides", "bevel", "steps", "opening", "thickness" })
                {
                    if (table.ContainsKey(key))
                    {
                        toml.Error(path, $"'{key}' is for primitives; this places {(table.ContainsKey("group") ? "a group" : "a prop")}.");
                    }
                }

                return null;
            }

            var primitive = new Primitive
            {
                Kind = toml.Enum<PrimitiveKind>(table, path, "primitive"),
                Sides = table.TryGetValue("sides", out var sides) ? toml.Int(sides, $"{path}.sides") : (int?)null,
                Bevel = toml.Float(table, path, "bevel", 0),
                Steps = table.TryGetValue("steps", out var steps) ? toml.Int(steps, $"{path}.steps") : 0,
                Thickness = toml.Float(table, path, "thickness", 0),
            };
            if (table.ContainsKey("opening"))
            {
                var (width, height) = toml.Pair2(table, path, "opening");
                primitive.OpeningWidth = width;
                primitive.OpeningHeight = height;
            }

            return primitive;
        }

        private static void WritePrimitive(TomlTable placement, Primitive primitive)
        {
            placement["primitive"] = ZoneToml.SnakeCase(primitive.Kind.ToString());
            if (primitive.Sides is { } sides)
            {
                placement["sides"] = (long)sides;
            }

            if (primitive.Bevel != 0)
            {
                placement["bevel"] = ZoneToml.Round(primitive.Bevel, LengthDecimals);
            }

            if (primitive.Steps != 0)
            {
                placement["steps"] = (long)primitive.Steps;
            }

            if (primitive.OpeningWidth != 0 || primitive.OpeningHeight != 0)
            {
                placement["opening"] = new TomlArray
                {
                    ZoneToml.Round(primitive.OpeningWidth, LengthDecimals),
                    ZoneToml.Round(primitive.OpeningHeight, LengthDecimals),
                };
            }

            if (primitive.Thickness != 0)
            {
                placement["thickness"] = ZoneToml.Round(primitive.Thickness, LengthDecimals);
            }
        }
    }
}
