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
    public sealed class ZoneTerrainEditing
    {
        private static readonly MessagePackSerializerOptions Serializer = ContentSerializer.CreateOptions(
            MessagePack.Resolvers.StandardResolver.Instance
        );
        private static readonly Dictionary<ZoneEditing, ZoneTerrainEditing> Open = new Dictionary<ZoneEditing, ZoneTerrainEditing>();

        private ZoneTerrainEditing(ZoneEditing editing)
        {
            Editing = editing;
            Zone = MessagePackSerializer.Deserialize<Zone>(editing.ZoneData, Serializer);
            foreach (var chunk in Zone.Chunks)
            {
                chunk.Tiles = chunk.Tiles.Select(Expand).ToArray();
            }

            Terrain = new ZoneTerrain(Zone);
        }

        public ZoneEditing Editing { get; }

        public Zone Zone { get; }

        public ZoneTerrain Terrain { get; private set; }

        public static ZoneTerrainEditing For(ZoneEditing editing)
        {
            if (!Open.TryGetValue(editing, out var terrain) || terrain.Editing == null)
            {
                terrain = new ZoneTerrainEditing(editing);
                Open[editing] = terrain;
            }

            return terrain;
        }

        public static void Forget() => Open.Clear();

        public void Commit(string undoName, CometSettings settings)
        {
            Undo.RecordObject(Editing, undoName);
            Editing.ZoneData = MessagePackSerializer.Serialize(Zone, Serializer);
            EditorUtility.SetDirty(Editing);
            Terrain = new ZoneTerrain(Zone);
            ZoneEditor.RebuildTerrain(Editing, Zone, settings);
        }

        public void Reindex() => Terrain = new ZoneTerrain(Zone);

        public bool TryGetHeight(string piece, int x, int z, out float height) => Terrain[piece].TryGetHeight(x, z, out height);

        /// <summary>A height in meters, or null for a hole; changes every tile holding the sample (edges repeat), making one if none does.</summary>
        public void SetHeight(string piece, int x, int z, float? height) =>
            ForEachTile(
                piece,
                x,
                z,
                create: height != null,
                (tile, tx, tz) => tile.Heights.Samples[tz * tile.Heights.SizeX + tx] = Sample(tile.Heights, height)
            );

        public void SetUnderside(string piece, int x, int z, float height) =>
            ForEachTile(
                piece,
                x,
                z,
                create: false,
                (tile, tx, tz) =>
                {
                    tile.Underside ??= Filled(tile.Heights, Terrain[piece].BottomHeight);
                    tile.Underside.Samples[tz * tile.Underside.SizeX + tx] = Sample(tile.Underside, height);
                }
            );

        public float UndersideAt(string piece, int x, int z) =>
            Terrain[piece].TryGetUnderside(x, z, out var under) ? under : Terrain[piece].BottomHeight;

        public void SetColor(string piece, int x, int z, uint color, uint ground) =>
            ForEachTile(
                piece,
                x,
                z,
                create: false,
                (tile, tx, tz) =>
                {
                    tile.Colors ??= NewColors(tile.Heights.SizeX, tile.Heights.SizeZ, ground);
                    tile.Colors.Colors[tz * tile.Colors.SizeX + tx] = color;
                }
            );

        public void ResetColors(string piece, int x, int z, uint ground, uint skirt) =>
            ForEachTile(
                piece,
                x,
                z,
                create: false,
                (tile, tx, tz) =>
                {
                    if (tile.Colors is { } colors)
                    {
                        colors.Colors[tz * colors.SizeX + tx] = ground;
                    }

                    if (tile.UndersideColors is { } underColors)
                    {
                        underColors.Colors[tz * underColors.SizeX + tx] = skirt;
                    }
                }
            );

        public void SetUndersideColor(string piece, int x, int z, uint color, uint skirt) =>
            ForEachTile(
                piece,
                x,
                z,
                create: false,
                (tile, tx, tz) =>
                {
                    if (tile.Underside is null)
                    {
                        return;
                    }

                    tile.UndersideColors ??= NewColors(tile.Heights.SizeX, tile.Heights.SizeZ, skirt);
                    tile.UndersideColors.Colors[tz * tile.UndersideColors.SizeX + tx] = color;
                }
            );

        public void ReplaceDefaultColors(string piece, uint oldGround, uint newGround, uint oldUnder, uint newUnder)
        {
            static void Replace(ColorMap? map, uint from, uint to)
            {
                if (map != null && from != to)
                {
                    for (var i = 0; i < map.Colors.Length; i++)
                    {
                        if (map.Colors[i] == from)
                        {
                            map.Colors[i] = to;
                        }
                    }
                }
            }

            foreach (var tile in Zone.Chunks.SelectMany(c => c.Tiles).Where(t => t.Piece == piece))
            {
                Replace(tile.Colors, oldGround, newGround);
                Replace(tile.UndersideColors, oldUnder, newUnder);
            }
        }

        public bool HasUnderside(string piece) => Zone.Chunks.SelectMany(c => c.Tiles).Any(t => t.Piece == piece && t.Underside != null);

        public void ClearUnderside(string piece)
        {
            foreach (var tile in Zone.Chunks.SelectMany(c => c.Tiles).Where(t => t.Piece == piece))
            {
                tile.Underside = null;
                tile.UndersideColors = null;
            }

            Reindex();
        }

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

        /// <summary>Moves a piece by whole samples sideways and <paramref name="dy"/> meters up, with its underside and colors.</summary>
        public void MovePiece(string name, int dx, int dz, float dy, uint ground, uint skirt)
        {
            var samples = new Dictionary<(int X, int Z), (float Height, float? Under, uint? Color, uint? UnderColor)>();
            foreach (var chunk in Zone.Chunks)
            {
                foreach (var tile in chunk.Tiles.Where(t => t.Piece == name))
                {
                    var map = tile.Heights;
                    for (var z = 0; z < map.SizeZ; z++)
                    {
                        for (var x = 0; x < map.SizeX; x++)
                        {
                            if (map.IsHole(x, z))
                            {
                                continue;
                            }

                            var i = z * map.SizeX + x;
                            var key = (chunk.Coord.X * ZoneGrid.ChunkSize + tile.OriginX + x, chunk.Coord.Z * ZoneGrid.ChunkSize + tile.OriginZ + z);
                            samples.TryGetValue(key, out var seen);
                            samples[key] = (
                                map.Offset + map.Samples[i] * Heightmap.Scale,
                                tile.Underside is { } under ? under.Offset + under.Samples[i] * Heightmap.Scale : seen.Under,
                                tile.Colors?.Colors[i] ?? seen.Color,
                                (tile.Underside != null ? tile.UndersideColors?.Colors[i] : null) ?? seen.UnderColor
                            );
                        }
                    }
                }
            }

            foreach (var chunk in Zone.Chunks)
            {
                chunk.Tiles = chunk.Tiles.Where(t => t.Piece != name).ToArray();
            }

            Reindex();
            foreach (var ((x, z), sample) in samples)
            {
                SetHeight(name, x + dx, z + dz, sample.Height + dy);
            }

            Reindex();
            foreach (var ((x, z), sample) in samples)
            {
                var (nx, nz) = (x + dx, z + dz);
                if (sample.Under is { } under)
                {
                    SetUnderside(name, nx, nz, under + dy);
                }

                if (sample.Color is { } color)
                {
                    SetColor(name, nx, nz, color, ground);
                }

                if (sample.UnderColor is { } underColor)
                {
                    SetUndersideColor(name, nx, nz, underColor, skirt);
                }
            }

            Reindex();
        }

        public void RemovePiece(string name)
        {
            Zone.Pieces = Zone.Pieces.Where(p => p.Name != name).ToArray();
            foreach (var chunk in Zone.Chunks)
            {
                chunk.Tiles = chunk.Tiles.Where(t => t.Piece != name).ToArray();
            }

            Reindex();
        }

        /// <summary>Samples where two pieces' ground is closer than <paramref name="gap"/> meters, so a body may not fit; worth fixing.</summary>
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

        public static void Crop(Zone zone)
        {
            foreach (var chunk in zone.Chunks)
            {
                var kept = new List<TerrainTile>();
                foreach (var tile in chunk.Tiles)
                {
                    var map = tile.Heights;
                    int minX = int.MaxValue,
                        minZ = int.MaxValue,
                        maxX = -1,
                        maxZ = -1;
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

                    if (maxX - minX < 1 || maxZ - minZ < 1)
                    {
                        continue;
                    }

                    if (minX == 0 && minZ == 0 && maxX == map.SizeX - 1 && maxZ == map.SizeZ - 1)
                    {
                        kept.Add(tile);
                        continue;
                    }

                    kept.Add(
                        new TerrainTile
                        {
                            Piece = tile.Piece,
                            OriginX = tile.OriginX + minX,
                            OriginZ = tile.OriginZ + minZ,
                            Heights = Cut(map, minX, minZ, maxX, maxZ),
                            Underside = tile.Underside is { } under ? Cut(under, minX, minZ, maxX, maxZ) : null,
                            Colors = tile.Colors is { } colors ? Cut(colors, minX, minZ, maxX, maxZ) : null,
                            UndersideColors = tile.UndersideColors is { } underColors ? Cut(underColors, minX, minZ, maxX, maxZ) : null,
                        }
                    );
                }

                chunk.Tiles = kept.ToArray();
            }
        }

        private static TerrainTile Expand(TerrainTile tile)
        {
            const int size = ZoneGrid.ChunkSamples;
            var heights = NewHeights(size, size, tile.Heights.Offset);
            var under = tile.Underside is { } u ? NewHeights(size, size, u.Offset) : null;
            var colors = tile.Colors != null ? NewColors(size, size, 0) : null;
            var underColors = tile.UndersideColors != null ? NewColors(size, size, 0) : null;
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

                    if (underColors != null)
                    {
                        underColors.Colors[to] = tile.UndersideColors![x, z];
                    }
                }
            }

            return new TerrainTile
            {
                Piece = tile.Piece,
                Heights = heights,
                Underside = under,
                Colors = colors,
                UndersideColors = underColors,
            };
        }

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

                const int size = ZoneGrid.ChunkSamples;
                var tile = new TerrainTile { Piece = piece, Heights = NewHeights(size, size, Zone.HeightBase) };
                var whole = Terrain[piece];
                for (var i = 0; i < size; i++)
                {
                    foreach (var (tx, tz) in new[] { (i, 0), (i, size - 1), (0, i), (size - 1, i) })
                    {
                        var (wx, wz) = (coord.X * ZoneGrid.ChunkSize + tx, coord.Z * ZoneGrid.ChunkSize + tz);
                        if (whole.TryGetHeight(wx, wz, out var edge))
                        {
                            tile.Heights.Samples[tz * size + tx] = Sample(tile.Heights, edge);
                        }
                    }
                }

                chunk.Tiles = chunk.Tiles.Append(tile).ToArray();
                Reindex();
                change(tile, x - coord.X * ZoneGrid.ChunkSize, z - coord.Z * ZoneGrid.ChunkSize);
            }
        }

        private static ushort Sample(Heightmap map, float? height) => height is { } h ? map.ToSample(h) : Heightmap.Hole;

        private static Heightmap Filled(Heightmap like, float height)
        {
            var map = NewHeights(like.SizeX, like.SizeZ, like.Offset);
            Array.Fill(map.Samples, Sample(map, height));
            return map;
        }

        private static Heightmap NewHeights(int sizeX, int sizeZ, float offset) =>
            new Heightmap
            {
                SizeX = sizeX,
                SizeZ = sizeZ,
                Offset = offset,
                Samples = new ushort[sizeX * sizeZ],
            };

        private static ColorMap NewColors(int sizeX, int sizeZ, uint fill)
        {
            var map = new ColorMap
            {
                SizeX = sizeX,
                SizeZ = sizeZ,
                Colors = new uint[sizeX * sizeZ],
            };
            Array.Fill(map.Colors, fill);
            return map;
        }

        private static Heightmap Cut(Heightmap map, int minX, int minZ, int maxX, int maxZ)
        {
            var cut = new Heightmap
            {
                SizeX = maxX - minX + 1,
                SizeZ = maxZ - minZ + 1,
                Offset = map.Offset,
            };
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
