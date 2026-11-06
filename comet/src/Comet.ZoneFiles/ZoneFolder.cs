using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Comet.Content;
using Tomlyn.Model;

namespace Comet.ZoneFiles
{
    /// <summary>
    /// A zone's folder: <c>zone.toml</c> (the zone's id, terrain pieces, borders, entrances and transition rooms),
    /// then a folder per chunk in <c>chunks/</c>, named by its grid position (<c>chunks/0_-1/</c>), holding the chunk's
    /// <c>chunk.toml</c> and its terrain tiles: <c>&lt;piece&gt;.height</c>, and optionally <c>&lt;piece&gt;.under.height</c>,
    /// <c>&lt;piece&gt;.color</c> and <c>&lt;piece&gt;.under.color</c> (the underside's colors, only with an underside).
    /// The zone is its set of chunk folders.
    /// </summary>
    public static class ZoneFolder
    {
        public const string ZoneFile = "zone.toml";
        public const string ChunksFolder = "chunks";
        public const string ChunkFileName = "chunk.toml";

        /// <summary>Decimal places written for positions, sizes and scales: millimeters.</summary>
        private const int LengthDecimals = 3;

        /// <summary>Decimal places written for angles: hundredths of a degree.</summary>
        private const int AngleDecimals = 2;

        /// <summary>Decimal places written for times in seconds: milliseconds.</summary>
        private const int TimeDecimals = 3;

        /// <summary>Decimal places written for sign background opacities: hundredths.</summary>
        private const int OpacityDecimals = 2;

        /// <summary>Decimal places written for sign font sizes: tenths of a pixel.</summary>
        private const int FontSizeDecimals = 1;

        private static readonly Regex ChunkName = new Regex(@"^(-?\d+)_(-?\d+)$");

        /// <summary>The chunk file's path in the zone folder.</summary>
        public static string ChunkFile(ChunkCoord coord) => $"{ChunksFolder}/{coord}/{ChunkFileName}";

        /// <summary>The path in the zone folder of a tile file: <paramref name="kind"/> is <c>height</c>, <c>under.height</c>, <c>color</c> or <c>under.color</c>.</summary>
        public static string TileFile(ChunkCoord coord, string piece, string kind) => $"{ChunksFolder}/{coord}/{piece}.{kind}";

        /// <summary>
        /// Reads the zone in <paramref name="folder"/>, adding any problems to <paramref name="errors"/>; the zone is
        /// returned either way, without the parts that failed. Checks the files' form only: <see cref="ZoneValidation"/>
        /// checks what they mean.
        /// </summary>
        public static Zone Read(string folder, List<ZoneFileError> errors)
        {
            var zone = new Zone();
            var zoneToml = new ZoneToml(ZoneFile, errors);
            var zonePath = Path.Combine(folder, ZoneFile);
            if (!File.Exists(zonePath))
            {
                zoneToml.Error("No zone file: each zone folder needs one.");
                return zone;
            }

            if (zoneToml.Parse(File.ReadAllText(zonePath)) is { } table)
            {
                ReadZone(zone, table, zoneToml);
            }

            var chunks = new List<ZoneChunk>();
            var chunksPath = Path.Combine(folder, ChunksFolder);
            if (Directory.Exists(chunksPath))
            {
                // Files directly in chunks/ and folders without a chunk file are left alone, unless the folder holds
                // terrain tiles, which would otherwise be dropped without a word.
                foreach (var path in Directory.EnumerateDirectories(chunksPath).OrderBy(p => p, StringComparer.Ordinal))
                {
                    var name = Path.GetFileName(path);
                    var file = $"{ChunksFolder}/{name}/{ChunkFileName}";
                    if (!File.Exists(Path.Combine(path, ChunkFileName)))
                    {
                        if (Directory.EnumerateFiles(path).Any(IsTileFile))
                        {
                            errors.Add(new ZoneFileError($"{ChunksFolder}/{name}", $"This folder has terrain tiles but no {ChunkFileName}."));
                        }

                        continue;
                    }

                    var match = ChunkName.Match(name);
                    if (!match.Success || !int.TryParse(match.Groups[1].Value, out var x) || !int.TryParse(match.Groups[2].Value, out var z))
                    {
                        errors.Add(new ZoneFileError(file, "Chunk folders are named by their grid position, x_z (such as 0_-1)."));
                        continue;
                    }

                    var chunk = ReadChunk(folder, new ChunkCoord(x, z), file, zone.HeightBase, errors);
                    if (chunk is not null)
                    {
                        chunks.Add(chunk);
                    }
                }
            }

            zone.Chunks = chunks.ToArray();
            return zone;
        }

        /// <summary>
        /// Writes <paramref name="zone"/> into <paramref name="folder"/>, removing chunk and tile files it no longer
        /// has. Files whose content hasn't changed are left untouched, so a save shows only real changes.
        /// </summary>
        public static void Write(string folder, Zone zone)
        {
            var written = new HashSet<string>(StringComparer.Ordinal);
            WriteIfChanged(folder, ZoneFile, Encoding.UTF8.GetBytes(HexColors(ZoneToml.Write(ZoneTable(zone)))), written);
            foreach (var chunk in zone.Chunks)
            {
                WriteIfChanged(folder, ChunkFile(chunk.Coord), Encoding.UTF8.GetBytes(HexColors(ZoneToml.Write(ChunkTable(chunk)))), written);
                foreach (var tile in chunk.Tiles)
                {
                    WriteIfChanged(folder, TileFile(chunk.Coord, tile.Piece, "height"), Bytes(s => HeightmapFormat.Write(s, tile.Heights)), written);
                    if (tile.Underside is not null)
                    {
                        WriteIfChanged(
                            folder,
                            TileFile(chunk.Coord, tile.Piece, "under.height"),
                            Bytes(s => HeightmapFormat.Write(s, tile.Underside)),
                            written
                        );
                    }

                    if (tile.Colors is not null)
                    {
                        WriteIfChanged(folder, TileFile(chunk.Coord, tile.Piece, "color"), Bytes(s => ColorMapFormat.Write(s, tile.Colors)), written);
                    }

                    if (tile.UndersideColors is not null)
                    {
                        WriteIfChanged(
                            folder,
                            TileFile(chunk.Coord, tile.Piece, "under.color"),
                            Bytes(s => ColorMapFormat.Write(s, tile.UndersideColors)),
                            written
                        );
                    }
                }
            }

            RemoveStale(folder, written);
        }

        private static void ReadZone(Zone zone, TomlTable table, ZoneToml toml)
        {
            toml.CheckKeys(table, "", "id", "world_anchor", "kill_height", "height_base", "piece", "border", "entrance", "transition_room");
            zone.Id = toml.String(table, "", "id");
            if (table.ContainsKey("world_anchor"))
            {
                var (x, z) = toml.Pair(table, "", "world_anchor");
                zone.WorldAnchor = new ChunkCoord(x, z);
            }

            if (table.ContainsKey("kill_height"))
            {
                zone.KillHeight = toml.Float(table, "", "kill_height");
            }

            zone.HeightBase = toml.Float(table, "", "height_base", Zone.DefaultHeightBase);

            zone.Pieces = toml.Tables(table, "piece")
                .Select(t =>
                {
                    toml.CheckKeys(t.Table, t.Path, "name", "skirt_depth", "rim_skirts", "hole_skirts", "ground_color", "under_color");
                    return new TerrainPiece
                    {
                        Name = toml.String(t.Table, t.Path, "name"),
                        SkirtDepth = toml.Float(t.Table, t.Path, "skirt_depth"),
                        RimSkirts = toml.Bool(t.Table, t.Path, "rim_skirts", true),
                        HoleSkirts = toml.Bool(t.Table, t.Path, "hole_skirts", true),
                        GroundColor = t.Table.TryGetValue("ground_color", out var ground) ? Color(toml, ground, $"{t.Path}.ground_color") : null,
                        UnderColor = t.Table.TryGetValue("under_color", out var under) ? Color(toml, under, $"{t.Path}.under_color") : null,
                    };
                })
                .ToArray();
            zone.Borders = toml.Tables(table, "border")
                .Select(t =>
                {
                    toml.CheckKeys(t.Table, t.Path, "chunk", "side", "zone");
                    var (x, z) = toml.Pair(t.Table, t.Path, "chunk");
                    return new ZoneBorder
                    {
                        Chunk = new ChunkCoord(x, z),
                        Side = toml.Enum<ChunkSide>(t.Table, t.Path, "side"),
                        Zone = toml.String(t.Table, t.Path, "zone"),
                    };
                })
                .ToArray();
            zone.Entrances = toml.Tables(table, "entrance")
                .Select(t =>
                {
                    toml.CheckKeys(t.Table, t.Path, "name", "position", "yaw", "size", "to_zone", "to_entrance");
                    return new ZoneEntrance
                    {
                        Name = toml.String(t.Table, t.Path, "name"),
                        Position = toml.Vector(t.Table, t.Path, "position"),
                        Yaw = toml.Float(t.Table, t.Path, "yaw", 0),
                        Size = toml.Vector(t.Table, t.Path, "size"),
                        ToZone = toml.String(t.Table, t.Path, "to_zone"),
                        ToEntrance = toml.String(t.Table, t.Path, "to_entrance"),
                    };
                })
                .ToArray();
            zone.TransitionRooms = toml.Tables(table, "transition_room")
                .Select(t =>
                {
                    toml.CheckKeys(t.Table, t.Path, "name", "position", "yaw", "size", "entrance");
                    return new TransitionRoom
                    {
                        Name = toml.String(t.Table, t.Path, "name"),
                        Position = toml.Vector(t.Table, t.Path, "position"),
                        Yaw = toml.Float(t.Table, t.Path, "yaw", 0),
                        Size = toml.Vector(t.Table, t.Path, "size"),
                        Entrance = toml.String(t.Table, t.Path, "entrance"),
                    };
                })
                .ToArray();
        }

        private static ZoneChunk? ReadChunk(string folder, ChunkCoord coord, string file, float heightBase, List<ZoneFileError> errors)
        {
            var toml = new ZoneToml(file, errors);
            var table = toml.Parse(File.ReadAllText(Path.Combine(folder, file)));
            if (table is null)
            {
                return null;
            }

            toml.CheckKeys(table, "", "tile", "placement", "spawn", "teleporter", "trigger", "volume", "sign");
            var chunk = new ZoneChunk { Coord = coord };
            var tiles = new List<TerrainTile>();
            var tileFiles = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (t, path) in toml.Tables(table, "tile"))
            {
                toml.CheckKeys(t, path, "piece", "origin");
                var tile = new TerrainTile { Piece = toml.String(t, path, "piece") };
                (tile.OriginX, tile.OriginZ) = toml.Pair(t, path, "origin");
                if (tile.Piece.Length == 0 || !ZoneValidation.IsName(tile.Piece))
                {
                    // Without a usable name there's no file to read; validation reports the name.
                    continue;
                }

                var heights = ReadTileFile(folder, TileFile(coord, tile.Piece, "height"), HeightmapFormat.Read, required: true, errors, tileFiles);
                if (heights is null)
                {
                    continue;
                }

                heights.Offset = heightBase;
                tile.Heights = heights;
                tile.Underside = ReadTileFile(
                    folder,
                    TileFile(coord, tile.Piece, "under.height"),
                    HeightmapFormat.Read,
                    required: false,
                    errors,
                    tileFiles
                );
                if (tile.Underside is not null)
                {
                    tile.Underside.Offset = heightBase;
                }
                tile.Colors = ReadTileFile(folder, TileFile(coord, tile.Piece, "color"), ColorMapFormat.Read, required: false, errors, tileFiles);
                tile.UndersideColors = ReadTileFile(
                    folder,
                    TileFile(coord, tile.Piece, "under.color"),
                    ColorMapFormat.Read,
                    required: false,
                    errors,
                    tileFiles
                );
                tiles.Add(tile);
            }

            chunk.Tiles = tiles.ToArray();
            chunk.Placements = toml.Tables(table, "placement").Select(p => ReadPlacement(toml, p.Table, p.Path)).ToArray();
            chunk.Spawns = toml.Tables(table, "spawn")
                .Select(s =>
                {
                    toml.CheckKeys(s.Table, s.Path, VolumeKeys.Concat(new[] { "id", "facing" }).ToArray());
                    return new SpawnPoint
                    {
                        Id = toml.String(s.Table, s.Path, "id"),
                        Volume = ReadVolume(toml, s.Table, s.Path),
                        Facing = toml.Enum<SpawnFacing>(s.Table, s.Path, "facing", SpawnFacing.Direction),
                    };
                })
                .ToArray();
            chunk.Teleporters = toml.Tables(table, "teleporter")
                .Select(t =>
                {
                    toml.CheckKeys(t.Table, t.Path, VolumeKeys.Concat(new[] { "id", "facing", "target", "charge_color" }).ToArray());
                    return new Teleporter
                    {
                        Id = toml.String(t.Table, t.Path, "id"),
                        Volume = ReadVolume(toml, t.Table, t.Path),
                        Facing = toml.Enum<SpawnFacing>(t.Table, t.Path, "facing", SpawnFacing.Direction),
                        Target = toml.String(t.Table, t.Path, "target"),
                        ChargeColor = t.Table.TryGetValue("charge_color", out var color) ? Color(toml, color, $"{t.Path}.charge_color") : null,
                    };
                })
                .ToArray();
            chunk.Triggers = toml.Tables(table, "trigger")
                .Select(t =>
                {
                    toml.CheckKeys(t.Table, t.Path, VolumeKeys.Concat(new[] { "id", "charge_color", "effect" }).ToArray());
                    return new Trigger
                    {
                        Id = toml.String(t.Table, t.Path, "id"),
                        Volume = ReadVolume(toml, t.Table, t.Path),
                        ChargeColor = t.Table.TryGetValue("charge_color", out var color) ? Color(toml, color, $"{t.Path}.charge_color") : null,
                        Effects = toml.Tables(t.Table, "effect")
                            .Select(e =>
                            {
                                var path = $"{t.Path}.{e.Path}";
                                toml.CheckKeys(e.Table, path, "toggle_tag", "revert_seconds");
                                return new ToggleEffect
                                {
                                    Tag = toml.String(e.Table, path, "toggle_tag"),
                                    RevertSeconds = toml.Float(e.Table, path, "revert_seconds", 0),
                                };
                            })
                            .ToArray(),
                    };
                })
                .ToArray();
            chunk.Signs = toml.Tables(table, "sign").Select(t => ReadSign(toml, t.Table, t.Path, "position")).ToArray();
            chunk.Volumes = toml.Tables(table, "volume")
                .Select(v =>
                {
                    toml.CheckKeys(v.Table, v.Path, "kind", "position", "rotation", "size");
                    return new CollisionVolume
                    {
                        Kind = toml.Enum<VolumeKind>(v.Table, v.Path, "kind"),
                        Position = toml.Vector(v.Table, v.Path, "position"),
                        Rotation = toml.Vector(v.Table, v.Path, "rotation", Vector3.Zero),
                        Size = toml.Vector(v.Table, v.Path, "size"),
                    };
                })
                .ToArray();

            // Tile files in the chunk folder that no tile reads are probably a piece renamed by hand: say so rather than
            // silently ignoring them. Other files there aren't the zone's.
            var tileFolder = Path.Combine(folder, ChunksFolder, coord.ToString());
            if (Directory.Exists(tileFolder))
            {
                foreach (var path in Directory.EnumerateFiles(tileFolder).OrderBy(p => p, StringComparer.Ordinal))
                {
                    var tileFile = $"{ChunksFolder}/{coord}/{Path.GetFileName(path)}";
                    if (IsTileFile(path) && !tileFiles.Contains(tileFile))
                    {
                        errors.Add(new ZoneFileError(tileFile, $"No tile in {file} uses this file."));
                    }
                }
            }

            return chunk;
        }

        private static T? ReadTileFile<T>(
            string folder,
            string file,
            Func<Stream, T> read,
            bool required,
            List<ZoneFileError> errors,
            HashSet<string> seen
        )
            where T : class
        {
            var path = Path.Combine(folder, file);
            if (!File.Exists(path))
            {
                if (required)
                {
                    errors.Add(new ZoneFileError(file, "Missing: the chunk lists a tile for this piece."));
                }

                return null;
            }

            seen.Add(file);
            try
            {
                using var stream = File.OpenRead(path);
                return read(stream);
            }
            catch (ContentException e)
            {
                errors.Add(new ZoneFileError(file, e.Message));
                return null;
            }
        }

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

        private static TomlTable ZoneTable(Zone zone)
        {
            var table = new TomlTable { ["id"] = zone.Id };
            if (zone.WorldAnchor is { } anchor)
            {
                table["world_anchor"] = new TomlArray { (long)anchor.X, (long)anchor.Z };
            }

            if (zone.KillHeight is { } killHeight)
            {
                table["kill_height"] = ZoneToml.Round(killHeight, LengthDecimals);
            }

            if (zone.HeightBase != Zone.DefaultHeightBase)
            {
                table["height_base"] = ZoneToml.Round(zone.HeightBase, LengthDecimals);
            }

            AddTables(
                table,
                "piece",
                zone.Pieces,
                p =>
                {
                    var piece = new TomlTable
                    {
                        ["name"] = p.Name,
                        ["skirt_depth"] = ZoneToml.Round(p.SkirtDepth, LengthDecimals),
                        ["rim_skirts"] = p.RimSkirts,
                        ["hole_skirts"] = p.HoleSkirts,
                    };
                    if (p.GroundColor is { } ground)
                    {
                        piece["ground_color"] = (long)ground;
                    }

                    if (p.UnderColor is { } under)
                    {
                        piece["under_color"] = (long)under;
                    }

                    return piece;
                }
            );
            AddTables(
                table,
                "border",
                zone.Borders,
                b => new TomlTable
                {
                    ["chunk"] = new TomlArray { (long)b.Chunk.X, (long)b.Chunk.Z },
                    ["side"] = ZoneToml.SnakeCase(b.Side.ToString()),
                    ["zone"] = b.Zone,
                }
            );
            AddTables(
                table,
                "entrance",
                zone.Entrances,
                e => new TomlTable
                {
                    ["name"] = e.Name,
                    ["position"] = ZoneToml.Array3(e.Position, LengthDecimals),
                    ["yaw"] = ZoneToml.Round(e.Yaw, AngleDecimals),
                    ["size"] = ZoneToml.Array3(e.Size, LengthDecimals),
                    ["to_zone"] = e.ToZone,
                    ["to_entrance"] = e.ToEntrance,
                }
            );
            AddTables(
                table,
                "transition_room",
                zone.TransitionRooms,
                r => new TomlTable
                {
                    ["name"] = r.Name,
                    ["position"] = ZoneToml.Array3(r.Position, LengthDecimals),
                    ["yaw"] = ZoneToml.Round(r.Yaw, AngleDecimals),
                    ["size"] = ZoneToml.Array3(r.Size, LengthDecimals),
                    ["entrance"] = r.Entrance,
                }
            );
            return table;
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

        private static TomlTable ChunkTable(ZoneChunk chunk)
        {
            var table = new TomlTable();
            AddTables(
                table,
                "tile",
                chunk.Tiles,
                t => new TomlTable
                {
                    ["piece"] = t.Piece,
                    ["origin"] = new TomlArray { (long)t.OriginX, (long)t.OriginZ },
                }
            );
            AddTables(table, "placement", chunk.Placements, PlacementTable);
            AddTables(
                table,
                "spawn",
                chunk.Spawns,
                s =>
                {
                    var spawn = new TomlTable { ["id"] = s.Id };
                    WriteVolume(spawn, s.Volume);
                    spawn["facing"] = ZoneToml.SnakeCase(s.Facing.ToString());
                    return WriteSubtables(spawn, s.Volume);
                }
            );
            AddTables(
                table,
                "teleporter",
                chunk.Teleporters,
                t =>
                {
                    var teleporter = new TomlTable { ["id"] = t.Id };
                    WriteVolume(teleporter, t.Volume);
                    teleporter["facing"] = ZoneToml.SnakeCase(t.Facing.ToString());
                    teleporter["target"] = t.Target;
                    if (t.ChargeColor is { } color)
                    {
                        teleporter["charge_color"] = (long)color;
                    }

                    return WriteSubtables(teleporter, t.Volume);
                }
            );
            AddTables(table, "sign", chunk.Signs, t => SignTable(t, "position"));
            AddTables(
                table,
                "trigger",
                chunk.Triggers,
                t =>
                {
                    var trigger = new TomlTable { ["id"] = t.Id };
                    WriteVolume(trigger, t.Volume);
                    if (t.ChargeColor is { } color)
                    {
                        trigger["charge_color"] = (long)color;
                    }

                    WriteSubtables(trigger, t.Volume);
                    AddTables(
                        trigger,
                        "effect",
                        t.Effects,
                        e =>
                        {
                            var effect = new TomlTable { ["toggle_tag"] = e.Tag };
                            if (e.RevertSeconds != 0)
                            {
                                effect["revert_seconds"] = ZoneToml.Round(e.RevertSeconds, TimeDecimals);
                            }

                            return effect;
                        }
                    );
                    return trigger;
                }
            );
            AddTables(
                table,
                "volume",
                chunk.Volumes,
                v =>
                {
                    var volume = new TomlTable
                    {
                        ["kind"] = ZoneToml.SnakeCase(v.Kind.ToString()),
                        ["position"] = ZoneToml.Array3(v.Position, LengthDecimals),
                    };
                    if (!IsZero(v.Rotation, AngleDecimals))
                    {
                        volume["rotation"] = ZoneToml.Array3(v.Rotation, AngleDecimals);
                    }

                    volume["size"] = ZoneToml.Array3(v.Size, LengthDecimals);
                    return volume;
                }
            );
            return table;
        }

        // The keys every trigger kind's table has: its volume, stand time, re-arm and pad.
        private static readonly string[] VolumeKeys = { "volume", "position", "yaw", "size", "stand_seconds", "rearm", "pad", "sign" };

        // A sign's table: standalone ones have a position; a trigger's has an offset from its volume's bottom center.
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

        // Settings not in use (the background's with no panel, the other mode's size) are kept when set, so turning
        // them back on in the editor brings them back.
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

        internal static void AddTables<T>(TomlTable table, string key, IEnumerable<T> items, Func<T, TomlTable> toTable)
        {
            var array = new TomlTableArray();
            foreach (var item in items)
            {
                array.Add(toTable(item));
            }

            if (array.Count > 0)
            {
                table[key] = array;
            }
        }

        private static bool IsZero(Vector3 value, int decimals) =>
            ZoneToml.Round(value.X, decimals) == 0 && ZoneToml.Round(value.Y, decimals) == 0 && ZoneToml.Round(value.Z, decimals) == 0;

        private static byte[] Bytes(Action<Stream> write)
        {
            using var stream = new MemoryStream();
            write(stream);
            return stream.ToArray();
        }

        private static void WriteIfChanged(string folder, string file, byte[] bytes, HashSet<string> written)
        {
            written.Add(file);
            var path = Path.Combine(folder, file);
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        private static bool IsTileFile(string path) =>
            path.EndsWith(".height", StringComparison.Ordinal) || path.EndsWith(".color", StringComparison.Ordinal);

        // Removes the chunk and tile files a save didn't write (deleted chunks and tiles) from the chunk folders (x_z,
        // the files directly in them), and chunk folders left empty. Anything else in chunks/ isn't the zone's, so it's
        // left alone.
        private static void RemoveStale(string folder, HashSet<string> written)
        {
            var chunksPath = Path.Combine(folder, ChunksFolder);
            if (!Directory.Exists(chunksPath))
            {
                return;
            }

            var chunkFolders = Directory.EnumerateDirectories(chunksPath).Where(d => ChunkName.IsMatch(Path.GetFileName(d))).ToList();
            foreach (var path in chunkFolders.SelectMany(d => Directory.EnumerateFiles(d)).ToList())
            {
                var file = Path.GetRelativePath(folder, path).Replace('\\', '/');
                if ((Path.GetFileName(path) == ChunkFileName || IsTileFile(path)) && !written.Contains(file))
                {
                    File.Delete(path);
                }
            }

            foreach (var directory in chunkFolders)
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
        }
    }
}
