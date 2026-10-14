using System;
using System.Collections.Generic;
using System.Linq;
using Comet.Content;
using Comet.Simulation;
using MessagePack;
using UnityEditor;
using UnityEngine;

namespace Comet.Unity.Editor
{
    /// <summary>
    /// A zone's terrain while it's being edited: each piece's tiles grown to whole chunks, so brushes can reach
    /// anywhere in a chunk the piece touches, and written back to the scene's copy of the zone after each stroke
    /// (one undo step). Saving crops the tiles back to their ground (<see cref="Crop"/>).
    /// </summary>
    public sealed class ZoneTerrainEditing
    {
        // Height range of new tiles: about 4 mm steps from -64 m to 192 m, as the test terrain uses.
        private const float NewScale = 1f / 256;
        private const float NewOffset = -64f;

        private static readonly MessagePackSerializerOptions Serializer = ContentSerializer.CreateOptions(MessagePack.Resolvers.StandardResolver.Instance);
        private static readonly Dictionary<int, ZoneTerrainEditing> Open = new Dictionary<int, ZoneTerrainEditing>();

        private ZoneTerrainEditing(ZoneEditing editing)
        {
            Editing = editing;
            Zone = MessagePackSerializer.Deserialize<Zone>(editing.zoneData, Serializer);
            foreach (var chunk in Zone.Chunks)
            {
                chunk.Tiles = chunk.Tiles.Select(Expand).ToArray();
            }

            Terrain = new ZoneTerrain(Zone);
        }

        public ZoneEditing Editing { get; }

        /// <summary>The zone with expanded tiles. Changes go through <see cref="SetHeight"/> and the like, then <see cref="Commit"/>.</summary>
        public Zone Zone { get; }

        public ZoneTerrain Terrain { get; private set; }

        /// <summary>The terrain being edited for a zone opened in the editor, made on first use.</summary>
        public static ZoneTerrainEditing For(ZoneEditing editing)
        {
            if (!Open.TryGetValue(editing.GetInstanceID(), out var terrain) || terrain.Editing == null)
            {
                terrain = new ZoneTerrainEditing(editing);
                Open[editing.GetInstanceID()] = terrain;
            }

            return terrain;
        }

        /// <summary>Forgets the edited copies, so they're read again from the scene (after an undo, say).</summary>
        public static void Forget() => Open.Clear();

        /// <summary>Writes the edited terrain into the scene's copy of the zone as one undo step, and redraws it.</summary>
        public void Commit(string undoName, ZoneEditorSettings settings)
        {
            Undo.RecordObject(Editing, undoName);
            Editing.zoneData = MessagePackSerializer.Serialize(Zone, Serializer);
            EditorUtility.SetDirty(Editing);
            Terrain = new ZoneTerrain(Zone);
            ZoneEditor.RebuildTerrain(Editing, Zone, settings);
        }

        /// <summary>Rebuilds the piece index after pieces or tiles were added or removed.</summary>
        public void Reindex() => Terrain = new ZoneTerrain(Zone);

        /// <summary>The ground height at a world sample of <paramref name="piece"/>, if it has ground there.</summary>
        public bool TryGetHeight(string piece, int x, int z, out float height) => Terrain[piece].TryGetHeight(x, z, out height);

        /// <summary>
        /// Sets a world sample of <paramref name="piece"/>: a height in meters, or null for a hole. Every tile
        /// holding the sample is changed (chunks repeat their shared edges); one is made if none holds it.
        /// </summary>
        public void SetHeight(string piece, int x, int z, float? height) =>
            ForEachTile(piece, x, z, create: height != null, (tile, tx, tz) => tile.Heights.Samples[tz * tile.Heights.SizeX + tx] = Sample(tile.Heights, height));

        /// <summary>Sets the underside height at a world sample, starting an underside map at the piece's bottom where there's none.</summary>
        public void SetUnderside(string piece, int x, int z, float height) => ForEachTile(piece, x, z, create: false, (tile, tx, tz) =>
        {
            tile.Underside ??= Filled(tile.Heights.SizeX, tile.Heights.SizeZ, Terrain[piece].Settings.BottomHeight);
            tile.Underside.Samples[tz * tile.Underside.SizeX + tx] = Sample(tile.Underside, height);
        });

        /// <summary>The underside height at a world sample: its underside map, else the piece's bottom.</summary>
        public float UndersideAt(string piece, int x, int z) =>
            Terrain[piece].TryGetUnderside(x, z, out var under) ? under : Terrain[piece].Settings.BottomHeight;

        /// <summary>Paints a world sample's color (0xRRGGBB), starting a color map of <paramref name="ground"/> where there's none.</summary>
        public void SetColor(string piece, int x, int z, uint color, uint ground) => ForEachTile(piece, x, z, create: false, (tile, tx, tz) =>
        {
            tile.Colors ??= new ColorMap { SizeX = tile.Heights.SizeX, SizeZ = tile.Heights.SizeZ, Colors = Enumerable.Repeat(ground, tile.Heights.SizeX * tile.Heights.SizeZ).ToArray() };
            tile.Colors.Colors[tz * tile.Colors.SizeX + tx] = color;
        });

        /// <summary>Adds a piece, flat at <paramref name="height"/> over a square of <paramref name="size"/> samples around <paramref name="center"/>.</summary>
        public void AddPiece(TerrainPiece settings, (int X, int Z) center, int size, float height)
        {
            Zone.Pieces = Zone.Pieces.Append(settings).ToArray();
            Reindex();
            for (var z = center.Z - size / 2; z <= center.Z + size / 2; z++)
            {
                for (var x = center.X - size / 2; x <= center.X + size / 2; x++)
                {
                    SetHeight(settings.Name, x, z, height);
                }
            }

            Reindex();
        }

        /// <summary>Removes a piece and all its tiles.</summary>
        public void RemovePiece(string name)
        {
            Zone.Pieces = Zone.Pieces.Where(p => p.Name != name).ToArray();
            foreach (var chunk in Zone.Chunks)
            {
                chunk.Tiles = chunk.Tiles.Where(t => t.Piece != name).ToArray();
            }

            Reindex();
        }

        /// <summary>
        /// World samples where two pieces both have ground closer together than <paramref name="gap"/> meters, so a
        /// body may not fit between them: allowed, but worth fixing.
        /// </summary>
        public List<Vector3> Overlaps(float gap)
        {
            var found = new List<Vector3>();
            var pieces = Terrain.Pieces;
            for (var i = 0; i < pieces.Count; i++)
            {
                for (var j = i + 1; j < pieces.Count; j++)
                {
                    var (a, b) = (pieces[i], pieces[j]);
                    var (aMinX, aMinZ, aMaxX, aMaxZ) = a.Extent;
                    var (bMinX, bMinZ, bMaxX, bMaxZ) = b.Extent;
                    for (var z = Math.Max(aMinZ, bMinZ); z <= Math.Min(aMaxZ, bMaxZ); z++)
                    {
                        for (var x = Math.Max(aMinX, bMinX); x <= Math.Min(aMaxX, bMaxX); x++)
                        {
                            if (a.TryGetHeight(x, z, out var ha) && b.TryGetHeight(x, z, out var hb) && Math.Abs(ha - hb) < gap)
                            {
                                found.Add(new Vector3(x * ZoneGrid.SampleSpacing, Math.Max(ha, hb), z * ZoneGrid.SampleSpacing));
                            }
                        }
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// Crops each tile to the samples with ground (and drops tiles with none), as the zone's files keep them.
        /// Underside and color maps are cropped the same way.
        /// </summary>
        public static void Crop(Zone zone)
        {
            foreach (var chunk in zone.Chunks)
            {
                var kept = new List<TerrainTile>();
                foreach (var tile in chunk.Tiles)
                {
                    var map = tile.Heights;
                    int minX = int.MaxValue, minZ = int.MaxValue, maxX = -1, maxZ = -1;
                    for (var z = 0; z < map.SizeZ; z++)
                    {
                        for (var x = 0; x < map.SizeX; x++)
                        {
                            if (!map.IsHole(x, z))
                            {
                                minX = Math.Min(minX, x);
                                minZ = Math.Min(minZ, z);
                                maxX = Math.Max(maxX, x);
                                maxZ = Math.Max(maxZ, z);
                            }
                        }
                    }

                    // A tile needs a whole cell of ground to draw anything; a lone row or column is dropped too.
                    if (maxX - minX < 1 || maxZ - minZ < 1)
                    {
                        continue;
                    }

                    if (minX == 0 && minZ == 0 && maxX == map.SizeX - 1 && maxZ == map.SizeZ - 1)
                    {
                        kept.Add(tile);
                        continue;
                    }

                    kept.Add(new TerrainTile
                    {
                        Piece = tile.Piece,
                        OriginX = tile.OriginX + minX,
                        OriginZ = tile.OriginZ + minZ,
                        Heights = Cut(map, minX, minZ, maxX, maxZ),
                        Underside = tile.Underside is { } under ? Cut(under, minX, minZ, maxX, maxZ) : null,
                        Colors = tile.Colors is { } colors ? Cut(colors, minX, minZ, maxX, maxZ) : null,
                    });
                }

                chunk.Tiles = kept.ToArray();
            }
        }

        // A tile grown to its whole chunk, with holes where it had no samples.
        private static TerrainTile Expand(TerrainTile tile)
        {
            const int size = ZoneGrid.ChunkSamples;
            var heights = new Heightmap { SizeX = size, SizeZ = size, Scale = tile.Heights.Scale, Offset = tile.Heights.Offset, Samples = new ushort[size * size] };
            var under = tile.Underside is { } u ? new Heightmap { SizeX = size, SizeZ = size, Scale = u.Scale, Offset = u.Offset, Samples = new ushort[size * size] } : null;
            var colors = tile.Colors != null ? new ColorMap { SizeX = size, SizeZ = size, Colors = new uint[size * size] } : null;
            for (var z = 0; z < tile.Heights.SizeZ; z++)
            {
                for (var x = 0; x < tile.Heights.SizeX; x++)
                {
                    var to = (tile.OriginZ + z) * size + tile.OriginX + x;
                    heights.Samples[to] = tile.Heights.Samples[z * tile.Heights.SizeX + x];
                    if (under != null)
                    {
                        under.Samples[to] = tile.Underside!.Samples[z * tile.Underside.SizeX + x];
                    }

                    if (colors != null)
                    {
                        colors.Colors[to] = tile.Colors![x, z];
                    }
                }
            }

            return new TerrainTile { Piece = tile.Piece, Heights = heights, Underside = under, Colors = colors };
        }

        // Runs `change` on every tile of the piece holding world sample (x, z), making one for its own chunk if
        // none does and `create` is set.
        private void ForEachTile(string piece, int x, int z, bool create, Action<TerrainTile, int, int> change)
        {
            var found = false;
            var chunkX = FloorDiv(x, ZoneGrid.ChunkSize);
            var chunkZ = FloorDiv(z, ZoneGrid.ChunkSize);
            for (var dz = 0; dz >= -1; dz--)
            {
                for (var dx = 0; dx >= -1; dx--)
                {
                    var coord = new ChunkCoord(chunkX + dx, chunkZ + dz);
                    var tx = x - coord.X * ZoneGrid.ChunkSize;
                    var tz = z - coord.Z * ZoneGrid.ChunkSize;
                    if (tx > ZoneGrid.ChunkSize || tz > ZoneGrid.ChunkSize)
                    {
                        continue;
                    }

                    var tile = Zone.Chunks.FirstOrDefault(c => c.Coord.Equals(coord))?.Tiles.FirstOrDefault(t => t.Piece == piece);
                    if (tile != null)
                    {
                        change(tile, tx, tz);
                        found = true;
                    }
                }
            }

            if (!found && create)
            {
                var coord = new ChunkCoord(chunkX, chunkZ);
                var chunk = Zone.Chunks.FirstOrDefault(c => c.Coord.Equals(coord));
                if (chunk == null)
                {
                    chunk = new ZoneChunk { Coord = coord };
                    Zone.Chunks = Zone.Chunks.Append(chunk).ToArray();
                }

                var like = Zone.Chunks.SelectMany(c => c.Tiles).FirstOrDefault(t => t.Piece == piece)?.Heights;
                const int size = ZoneGrid.ChunkSamples;
                var tile = new TerrainTile
                {
                    Piece = piece,
                    Heights = new Heightmap { SizeX = size, SizeZ = size, Scale = like?.Scale ?? NewScale, Offset = like?.Offset ?? NewOffset, Samples = new ushort[size * size] },
                };
                chunk.Tiles = chunk.Tiles.Append(tile).ToArray();
                Reindex();
                change(tile, x - coord.X * ZoneGrid.ChunkSize, z - coord.Z * ZoneGrid.ChunkSize);
            }
        }

        // A height in meters as one of the map's samples, kept off the hole value and within range.
        private static ushort Sample(Heightmap map, float? height) => height is { } h
            ? (ushort)Mathf.Clamp(Mathf.Round((h - map.Offset) / map.Scale), 1, ushort.MaxValue)
            : Heightmap.Hole;

        private static Heightmap Filled(int sizeX, int sizeZ, float height)
        {
            var map = new Heightmap { SizeX = sizeX, SizeZ = sizeZ, Scale = NewScale, Offset = NewOffset, Samples = new ushort[sizeX * sizeZ] };
            Array.Fill(map.Samples, Sample(map, height));
            return map;
        }

        private static Heightmap Cut(Heightmap map, int minX, int minZ, int maxX, int maxZ)
        {
            var cut = new Heightmap { SizeX = maxX - minX + 1, SizeZ = maxZ - minZ + 1, Scale = map.Scale, Offset = map.Offset };
            cut.Samples = new ushort[cut.SizeX * cut.SizeZ];
            for (var z = 0; z < cut.SizeZ; z++)
            {
                Array.Copy(map.Samples, (minZ + z) * map.SizeX + minX, cut.Samples, z * cut.SizeX, cut.SizeX);
            }

            return cut;
        }

        private static ColorMap Cut(ColorMap map, int minX, int minZ, int maxX, int maxZ)
        {
            var cut = new ColorMap { SizeX = maxX - minX + 1, SizeZ = maxZ - minZ + 1 };
            cut.Colors = new uint[cut.SizeX * cut.SizeZ];
            for (var z = 0; z < cut.SizeZ; z++)
            {
                Array.Copy(map.Colors, (minZ + z) * map.SizeX + minX, cut.Colors, z * cut.SizeX, cut.SizeX);
            }

            return cut;
        }

        private static int FloorDiv(int a, int b) => a >= 0 ? a / b : (a - b + 1) / b;
    }
}
