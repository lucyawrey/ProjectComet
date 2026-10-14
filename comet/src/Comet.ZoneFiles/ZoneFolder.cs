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
    /// A zone's folder: <c>zone.toml</c> (the zone's key, terrain pieces, borders, entrances and transition rooms),
    /// then a TOML file per chunk in <c>chunks/</c>, named by its grid position (<c>chunks/0_-1.toml</c>), with the
    /// chunk's terrain tiles beside it in a folder of the same name: <c>&lt;piece&gt;.height</c>, and optionally
    /// <c>&lt;piece&gt;.under.height</c>, <c>&lt;piece&gt;.color</c> and <c>&lt;piece&gt;.under.color</c> (the underside's
    /// colors, only with an underside). The zone is its set of chunk files.
    /// </summary>
    public static class ZoneFolder
    {
        public const string ZoneFile = "zone.toml";
        public const string ChunksFolder = "chunks";

        /// <summary>Decimal places written for positions, sizes and scales: millimeters.</summary>
        private const int LengthDecimals = 3;
        /// <summary>Decimal places written for angles: hundredths of a degree.</summary>
        private const int AngleDecimals = 2;

        private static readonly Regex ChunkName = new Regex(@"^(-?\d+)_(-?\d+)$");

        /// <summary>The chunk file's path in the zone folder.</summary>
        public static string ChunkFile(ChunkCoord coord) => $"{ChunksFolder}/{coord}.toml";

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
                foreach (var path in Directory.EnumerateFiles(chunksPath, "*.toml").OrderBy(p => p, StringComparer.Ordinal))
                {
                    var name = Path.GetFileNameWithoutExtension(path);
                    var match = ChunkName.Match(name);
                    var file = $"{ChunksFolder}/{Path.GetFileName(path)}";
                    if (!match.Success || !int.TryParse(match.Groups[1].Value, out var x) || !int.TryParse(match.Groups[2].Value, out var z))
                    {
                        errors.Add(new ZoneFileError(file, "Chunk files are named by their grid position, x_z (such as 0_-1.toml)."));
                        continue;
                    }

                    var chunk = ReadChunk(folder, new ChunkCoord(x, z), file, errors);
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
                WriteIfChanged(folder, ChunkFile(chunk.Coord), Encoding.UTF8.GetBytes(ZoneToml.Write(ChunkTable(chunk))), written);
                foreach (var tile in chunk.Tiles)
                {
                    WriteIfChanged(folder, TileFile(chunk.Coord, tile.Piece, "height"), Bytes(s => HeightmapFormat.Write(s, tile.Heights)), written);
                    if (tile.Underside is not null)
                    {
                        WriteIfChanged(folder, TileFile(chunk.Coord, tile.Piece, "under.height"), Bytes(s => HeightmapFormat.Write(s, tile.Underside)), written);
                    }

                    if (tile.Colors is not null)
                    {
                        WriteIfChanged(folder, TileFile(chunk.Coord, tile.Piece, "color"), Bytes(s => ColorMapFormat.Write(s, tile.Colors)), written);
                    }

                    if (tile.UndersideColors is not null)
                    {
                        WriteIfChanged(folder, TileFile(chunk.Coord, tile.Piece, "under.color"), Bytes(s => ColorMapFormat.Write(s, tile.UndersideColors)), written);
                    }
                }
            }

            RemoveStale(folder, written);
        }

        private static void ReadZone(Zone zone, TomlTable table, ZoneToml toml)
        {
            toml.CheckKeys(table, "", "id", "world_anchor", "piece", "border", "entrance", "transition_room");
            zone.Id = toml.String(table, "", "id");
            if (table.ContainsKey("world_anchor"))
            {
                var (x, z) = toml.Pair(table, "", "world_anchor");
                zone.WorldAnchor = new ChunkCoord(x, z);
            }

            zone.Pieces = toml.Tables(table, "piece").Select(t =>
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
            }).ToArray();
            zone.Borders = toml.Tables(table, "border").Select(t =>
            {
                toml.CheckKeys(t.Table, t.Path, "chunk", "side", "zone");
                var (x, z) = toml.Pair(t.Table, t.Path, "chunk");
                return new ZoneBorder
                {
                    Chunk = new ChunkCoord(x, z),
                    Side = toml.Enum<ChunkSide>(t.Table, t.Path, "side"),
                    Zone = toml.String(t.Table, t.Path, "zone"),
                };
            }).ToArray();
            zone.Entrances = toml.Tables(table, "entrance").Select(t =>
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
            }).ToArray();
            zone.TransitionRooms = toml.Tables(table, "transition_room").Select(t =>
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
            }).ToArray();
        }

        private static ZoneChunk? ReadChunk(string folder, ChunkCoord coord, string file, List<ZoneFileError> errors)
        {
            var toml = new ZoneToml(file, errors);
            var table = toml.Parse(File.ReadAllText(Path.Combine(folder, file)));
            if (table is null)
            {
                return null;
            }

            toml.CheckKeys(table, "", "tile", "placement", "spawn", "volume");
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

                tile.Heights = heights;
                tile.Underside = ReadTileFile(folder, TileFile(coord, tile.Piece, "under.height"), HeightmapFormat.Read, required: false, errors, tileFiles);
                tile.Colors = ReadTileFile(folder, TileFile(coord, tile.Piece, "color"), ColorMapFormat.Read, required: false, errors, tileFiles);
                tile.UndersideColors = ReadTileFile(folder, TileFile(coord, tile.Piece, "under.color"), ColorMapFormat.Read, required: false, errors, tileFiles);
                tiles.Add(tile);
            }

            chunk.Tiles = tiles.ToArray();
            chunk.Placements = toml.Tables(table, "placement").Select(p =>
            {
                toml.CheckKeys(p.Table, p.Path, "asset", "name", "position", "rotation", "scale");
                return new Placement
                {
                    Asset = toml.String(p.Table, p.Path, "asset"),
                    Name = p.Table.ContainsKey("name") ? toml.String(p.Table, p.Path, "name") : null,
                    Position = toml.Vector(p.Table, p.Path, "position"),
                    Rotation = toml.Vector(p.Table, p.Path, "rotation", Vector3.Zero),
                    Scale = toml.Vector(p.Table, p.Path, "scale", Vector3.One),
                };
            }).ToArray();
            chunk.Spawns = toml.Tables(table, "spawn").Select(s =>
            {
                toml.CheckKeys(s.Table, s.Path, "id", "position", "radius", "facing", "yaw", "default");
                return new SpawnPoint
                {
                    Id = toml.String(s.Table, s.Path, "id"),
                    Position = toml.Vector(s.Table, s.Path, "position"),
                    Radius = toml.Float(s.Table, s.Path, "radius"),
                    Facing = toml.Enum<SpawnFacing>(s.Table, s.Path, "facing", SpawnFacing.Direction),
                    Yaw = toml.Float(s.Table, s.Path, "yaw", 0),
                    IsDefault = toml.Bool(s.Table, s.Path, "default", false),
                };
            }).ToArray();
            chunk.Volumes = toml.Tables(table, "volume").Select(v =>
            {
                toml.CheckKeys(v.Table, v.Path, "kind", "position", "rotation", "size");
                return new CollisionVolume
                {
                    Kind = toml.Enum<VolumeKind>(v.Table, v.Path, "kind"),
                    Position = toml.Vector(v.Table, v.Path, "position"),
                    Rotation = toml.Vector(v.Table, v.Path, "rotation", Vector3.Zero),
                    Size = toml.Vector(v.Table, v.Path, "size"),
                };
            }).ToArray();

            // Files in the tile folder that no tile reads are probably a piece renamed by hand: say so rather than
            // silently ignoring them.
            var tileFolder = Path.Combine(folder, ChunksFolder, coord.ToString());
            if (Directory.Exists(tileFolder))
            {
                foreach (var path in Directory.EnumerateFiles(tileFolder).OrderBy(p => p, StringComparer.Ordinal))
                {
                    var tileFile = $"{ChunksFolder}/{coord}/{Path.GetFileName(path)}";
                    if (!tileFiles.Contains(tileFile))
                    {
                        errors.Add(new ZoneFileError(tileFile, $"No tile in {file} uses this file."));
                    }
                }
            }

            return chunk;
        }

        private static T? ReadTileFile<T>(string folder, string file, Func<Stream, T> read, bool required, List<ZoneFileError> errors, HashSet<string> seen)
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
        private static string HexColors(string text) =>
            Regex.Replace(text, @"^((?:ground|under)_color = )(\d+)$", m => $"{m.Groups[1].Value}0x{long.Parse(m.Groups[2].Value):X6}", RegexOptions.Multiline);

        private static TomlTable ZoneTable(Zone zone)
        {
            var table = new TomlTable { ["id"] = zone.Id };
            if (zone.WorldAnchor is { } anchor)
            {
                table["world_anchor"] = new TomlArray { (long)anchor.X, (long)anchor.Z };
            }

            AddTables(table, "piece", zone.Pieces, p =>
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
            });
            AddTables(table, "border", zone.Borders, b => new TomlTable
            {
                ["chunk"] = new TomlArray { (long)b.Chunk.X, (long)b.Chunk.Z },
                ["side"] = ZoneToml.SnakeCase(b.Side.ToString()),
                ["zone"] = b.Zone,
            });
            AddTables(table, "entrance", zone.Entrances, e => new TomlTable
            {
                ["name"] = e.Name,
                ["position"] = ZoneToml.Array3(e.Position, LengthDecimals),
                ["yaw"] = ZoneToml.Round(e.Yaw, AngleDecimals),
                ["size"] = ZoneToml.Array3(e.Size, LengthDecimals),
                ["to_zone"] = e.ToZone,
                ["to_entrance"] = e.ToEntrance,
            });
            AddTables(table, "transition_room", zone.TransitionRooms, r => new TomlTable
            {
                ["name"] = r.Name,
                ["position"] = ZoneToml.Array3(r.Position, LengthDecimals),
                ["yaw"] = ZoneToml.Round(r.Yaw, AngleDecimals),
                ["size"] = ZoneToml.Array3(r.Size, LengthDecimals),
                ["entrance"] = r.Entrance,
            });
            return table;
        }

        private static TomlTable ChunkTable(ZoneChunk chunk)
        {
            var table = new TomlTable();
            AddTables(table, "tile", chunk.Tiles, t => new TomlTable
            {
                ["piece"] = t.Piece,
                ["origin"] = new TomlArray { (long)t.OriginX, (long)t.OriginZ },
            });
            AddTables(table, "placement", chunk.Placements, p =>
            {
                var placement = new TomlTable { ["asset"] = p.Asset };
                if (p.Name is not null)
                {
                    placement["name"] = p.Name;
                }

                placement["position"] = ZoneToml.Array3(p.Position, LengthDecimals);
                // Leave out the defaults, which most placements have, to keep the files short.
                if (!IsZero(p.Rotation, AngleDecimals))
                {
                    placement["rotation"] = ZoneToml.Array3(p.Rotation, AngleDecimals);
                }

                if (!IsZero(p.Scale - Vector3.One, LengthDecimals))
                {
                    placement["scale"] = ZoneToml.Array3(p.Scale, LengthDecimals);
                }

                return placement;
            });
            AddTables(table, "spawn", chunk.Spawns, s =>
            {
                var spawn = new TomlTable
                {
                    ["id"] = s.Id,
                    ["position"] = ZoneToml.Array3(s.Position, LengthDecimals),
                    ["radius"] = ZoneToml.Round(s.Radius, LengthDecimals),
                    ["facing"] = ZoneToml.SnakeCase(s.Facing.ToString()),
                };
                if (s.Facing == SpawnFacing.Direction)
                {
                    spawn["yaw"] = ZoneToml.Round(s.Yaw, AngleDecimals);
                }

                if (s.IsDefault)
                {
                    spawn["default"] = true;
                }

                return spawn;
            });
            AddTables(table, "volume", chunk.Volumes, v =>
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
            });
            return table;
        }

        private static void AddTables<T>(TomlTable table, string key, IEnumerable<T> items, Func<T, TomlTable> toTable)
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

        // Removes the chunk and tile files a save didn't write (deleted chunks and tiles), and tile folders left empty.
        private static void RemoveStale(string folder, HashSet<string> written)
        {
            var chunksPath = Path.Combine(folder, ChunksFolder);
            if (!Directory.Exists(chunksPath))
            {
                return;
            }

            foreach (var path in Directory.EnumerateFiles(chunksPath, "*", SearchOption.AllDirectories).ToList())
            {
                var file = Path.GetRelativePath(folder, path).Replace('\\', '/');
                var ours = file.EndsWith(".toml", StringComparison.Ordinal) || file.EndsWith(".height", StringComparison.Ordinal) || file.EndsWith(".color", StringComparison.Ordinal);
                if (ours && !written.Contains(file))
                {
                    File.Delete(path);
                }
            }

            foreach (var directory in Directory.EnumerateDirectories(chunksPath))
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
        }
    }
}
