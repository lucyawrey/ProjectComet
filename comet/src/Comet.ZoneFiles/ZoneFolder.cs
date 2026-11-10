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
    /// <summary>A folder per chunk in <c>chunks/</c>, named by position (<c>0_-1</c>), holding <c>chunk.toml</c> and its terrain tiles.</summary>
    public static partial class ZoneFolder
    {
        public const string ZoneFile = "zone.toml";
        public const string ChunksFolder = "chunks";
        public const string ChunkFileName = "chunk.toml";

        /// <summary>Positions, sizes and scales are written to the millimeter.</summary>
        private const int LengthDecimals = 3;

        /// <summary>Angles are written to hundredths of a degree.</summary>
        private const int AngleDecimals = 2;

        /// <summary>Times are written to the millisecond.</summary>
        private const int TimeDecimals = 3;

        /// <summary>Sign background opacities are written to hundredths.</summary>
        private const int OpacityDecimals = 2;

        /// <summary>Sign font sizes are written to tenths of a pixel.</summary>
        private const int FontSizeDecimals = 1;

        private static readonly Regex ChunkName = new Regex(@"^(-?\d+)_(-?\d+)$");

        public static string ChunkFile(ChunkCoord coord) => $"{ChunksFolder}/{coord}/{ChunkFileName}";

        /// <summary><paramref name="kind"/> is <c>height</c>, <c>under.height</c>, <c>color</c> or <c>under.color</c>.</summary>
        public static string TileFile(ChunkCoord coord, string piece, string kind) => $"{ChunksFolder}/{coord}/{piece}.{kind}";

        /// <summary>Returns the zone even with errors, minus failed parts; checks only form: <see cref="ZoneValidation"/> checks meaning.</summary>
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

        /// <summary>Removes chunk and tile files the zone no longer has; unchanged files are left alone, so a save shows only real changes.</summary>
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
