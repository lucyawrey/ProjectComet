using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Comet.Content;

namespace Comet.ZoneFiles
{
    /// <summary>
    /// Checks what a zone's files mean, after <see cref="ZoneFolder.Read"/> has checked their form: keys and names,
    /// tiles that name a known piece and fit their chunk, terrain that matches where a piece's tiles meet, objects
    /// whose pivots are in their own chunk, and one default spawn. Run by the content build, and by the editor
    /// before it saves.
    /// </summary>
    public static class ZoneValidation
    {
        private static readonly Regex NamePattern = new Regex("^[a-z][a-z0-9_]*$");
        private static readonly Regex PlacementName = new Regex("^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)*$");

        /// <summary>A name in keys and in the zone: a lowercase letter, then lowercase letters, digits and underscores.</summary>
        public static bool IsName(string name) => NamePattern.IsMatch(name);

        /// <summary>Whether <paramref name="key"/> is <c>{kind}.name</c>.</summary>
        public static bool IsKey(string key, string kind) =>
            key.StartsWith(kind + ".", StringComparison.Ordinal) && IsName(key.Substring(kind.Length + 1));

        /// <summary>The problems in <paramref name="zone"/>, each against the file it's in.</summary>
        public static List<ZoneFileError> Check(Zone zone)
        {
            var errors = new List<ZoneFileError>();
            void Error(string file, string message) => errors.Add(new ZoneFileError(file, message));
            const string zoneFile = ZoneFolder.ZoneFile;

            if (!IsKey(zone.Id, "zone"))
            {
                Error(zoneFile, $"id: '{zone.Id}' isn't a valid zone key: use 'zone.' then lowercase letters, digits and underscores.");
            }

            CheckNames(zone.Pieces.Select(p => p.Name), "piece", zoneFile, Error);
            CheckNames(zone.Entrances.Select(e => e.Name), "entrance", zoneFile, Error);
            CheckNames(zone.TransitionRooms.Select(r => r.Name), "transition_room", zoneFile, Error);

            var chunks = new HashSet<ChunkCoord>(zone.Chunks.Select(c => c.Coord));
            foreach (var border in zone.Borders)
            {
                if (!chunks.Contains(border.Chunk))
                {
                    Error(zoneFile, $"border: chunk {border.Chunk} isn't one of the zone's chunks.");
                }

                if (!IsKey(border.Zone, "zone") || border.Zone == zone.Id)
                {
                    Error(zoneFile, $"border: '{border.Zone}' isn't another zone's key.");
                }
            }

            foreach (var entrance in zone.Entrances)
            {
                CheckBox(entrance.Size, $"entrance {entrance.Name}", zoneFile, Error);
                if (!IsKey(entrance.ToZone, "zone"))
                {
                    Error(zoneFile, $"entrance {entrance.Name}: to_zone '{entrance.ToZone}' isn't a zone key.");
                }
            }

            var entranceNames = new HashSet<string>(zone.Entrances.Select(e => e.Name));
            foreach (var room in zone.TransitionRooms)
            {
                CheckBox(room.Size, $"transition_room {room.Name}", zoneFile, Error);
                if (!entranceNames.Contains(room.Entrance))
                {
                    Error(zoneFile, $"transition_room {room.Name}: there's no entrance '{room.Entrance}'.");
                }
            }

            var placementNames = new Dictionary<string, string>();
            foreach (var chunk in zone.Chunks)
            {
                foreach (var placement in chunk.Placements.Where(p => p.Name is not null))
                {
                    var file = ZoneFolder.ChunkFile(chunk.Coord);
                    if (!PlacementName.IsMatch(placement.Name!))
                    {
                        Error(file, $"placement of {placement.Asset}: '{placement.Name}' isn't a valid name: use lowercase letters, digits, underscores and dots, starting with a letter.");
                    }
                    else if (placementNames.TryGetValue(placement.Name!, out var first))
                    {
                        Error(file, $"placement of {placement.Asset}: the name '{placement.Name}' is already used in {first}.");
                    }
                    else
                    {
                        placementNames.Add(placement.Name!, file);
                    }
                }
            }

            var pieces = new HashSet<string>(zone.Pieces.Select(p => p.Name));
            var spawnFiles = new Dictionary<string, string>();
            var defaults = new List<string>();
            foreach (var chunk in zone.Chunks)
            {
                var file = ZoneFolder.ChunkFile(chunk.Coord);
                CheckTiles(chunk, pieces, file, Error);
                foreach (var placement in chunk.Placements)
                {
                    var what = $"placement of {placement.Asset}";
                    if (!IsKey(placement.Asset, "asset"))
                    {
                        Error(file, $"placement: '{placement.Asset}' isn't an asset key.");
                    }

                    CheckPivot(chunk.Coord, placement.Position, what, file, Error);
                    if (placement.Scale.X <= 0 || placement.Scale.Y <= 0 || placement.Scale.Z <= 0)
                    {
                        Error(file, $"{what} at {Text(placement.Position)}: scale must be above 0 on every axis.");
                    }
                }

                foreach (var spawn in chunk.Spawns)
                {
                    if (!IsKey(spawn.Id, "spawn"))
                    {
                        Error(file, $"spawn: '{spawn.Id}' isn't a valid spawn key: use 'spawn.' then lowercase letters, digits and underscores.");
                    }
                    else if (spawnFiles.TryGetValue(spawn.Id, out var first))
                    {
                        Error(file, $"spawn: '{spawn.Id}' is already used in {first}.");
                    }
                    else
                    {
                        spawnFiles.Add(spawn.Id, file);
                    }

                    CheckPivot(chunk.Coord, spawn.Position, $"spawn {spawn.Id}", file, Error);
                    if (spawn.Radius < 0)
                    {
                        Error(file, $"spawn {spawn.Id}: radius can't be negative.");
                    }

                    if (spawn.IsDefault)
                    {
                        defaults.Add(spawn.Id);
                    }
                }

                foreach (var volume in chunk.Volumes)
                {
                    CheckPivot(chunk.Coord, volume.Position, $"{ZoneToml.SnakeCase(volume.Kind.ToString())} volume", file, Error);
                    CheckBox(volume.Size, $"{ZoneToml.SnakeCase(volume.Kind.ToString())} volume at {Text(volume.Position)}", file, Error);
                }
            }

            if (spawnFiles.Count > 0 && defaults.Count != 1)
            {
                Error(zoneFile, defaults.Count == 0
                    ? "No default spawn: mark one of the zone's spawns with default = true."
                    : $"{defaults.Count} default spawns ({string.Join(", ", defaults)}): a zone has one.");
            }

            CheckSharedEdges(zone, Error);
            return errors;
        }

        /// <summary>
        /// The problems with <paramref name="zone"/>'s placements given its assets (found by key through
        /// <paramref name="findAsset"/>, null for an unknown key): unknown assets, uneven scale on an asset that only
        /// scales evenly, and objects reaching past the ring of chunks around their pivot's chunk.
        /// </summary>
        public static List<ZoneFileError> CheckPlacements(Zone zone, Func<string, Asset?> findAsset)
        {
            var errors = new List<ZoneFileError>();
            foreach (var chunk in zone.Chunks)
            {
                var file = ZoneFolder.ChunkFile(chunk.Coord);
                foreach (var placement in chunk.Placements)
                {
                    var what = $"placement of {placement.Asset} at {Text(placement.Position)}";
                    var asset = findAsset(placement.Asset);
                    if (asset is null)
                    {
                        errors.Add(new ZoneFileError(file, $"{what}: there's no asset '{placement.Asset}'."));
                        continue;
                    }

                    var scale = placement.Scale;
                    if (asset.UniformScale && (Math.Abs(scale.X - scale.Y) > 1e-4f || Math.Abs(scale.X - scale.Z) > 1e-4f))
                    {
                        errors.Add(new ZoneFileError(file, $"{what}: {asset.Id} only scales evenly, the same on every axis."));
                    }

                    var (min, max) = Bounds(asset, placement.Matrix());
                    var reachMin = new Vector3((chunk.Coord.X - 1) * ZoneGrid.ChunkSize, float.MinValue, (chunk.Coord.Z - 1) * ZoneGrid.ChunkSize);
                    var reachMax = new Vector3((chunk.Coord.X + 2) * ZoneGrid.ChunkSize, float.MaxValue, (chunk.Coord.Z + 2) * ZoneGrid.ChunkSize);
                    if (min.X < reachMin.X || min.Z < reachMin.Z || max.X > reachMax.X || max.Z > reachMax.Z)
                    {
                        errors.Add(new ZoneFileError(file, $"{what}: it reaches past the chunks around its own; objects can reach at most one chunk ({ZoneGrid.ChunkSize} m) beyond the chunk holding their pivot."));
                    }
                }
            }

            return errors;
        }

        // The world box around an asset's bounds once placed.
        private static (Vector3 Min, Vector3 Max) Bounds(Asset asset, Matrix4x4 matrix)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (var corner = 0; corner < 8; corner++)
            {
                var point = Vector3.Transform(new Vector3(
                    (corner & 1) == 0 ? asset.BoundsMin.X : asset.BoundsMax.X,
                    (corner & 2) == 0 ? asset.BoundsMin.Y : asset.BoundsMax.Y,
                    (corner & 4) == 0 ? asset.BoundsMin.Z : asset.BoundsMax.Z), matrix);
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
            }

            return (min, max);
        }

        private static void CheckNames(IEnumerable<string> names, string what, string file, Action<string, string> error)
        {
            var seen = new HashSet<string>();
            foreach (var name in names)
            {
                if (!IsName(name))
                {
                    error(file, $"{what}: '{name}' isn't a valid name: use lowercase letters, digits and underscores, starting with a letter.");
                }
                else if (!seen.Add(name))
                {
                    error(file, $"{what}: '{name}' is used twice.");
                }
            }
        }

        private static void CheckTiles(ZoneChunk chunk, HashSet<string> pieces, string file, Action<string, string> error)
        {
            var seen = new HashSet<string>();
            foreach (var tile in chunk.Tiles)
            {
                if (!pieces.Contains(tile.Piece))
                {
                    error(file, $"tile: there's no piece '{tile.Piece}' in {ZoneFolder.ZoneFile}.");
                    continue;
                }

                if (!seen.Add(tile.Piece))
                {
                    error(file, $"tile: piece '{tile.Piece}' has two tiles in this chunk.");
                }

                var heights = tile.Heights;
                if (tile.OriginX < 0 || tile.OriginZ < 0
                    || tile.OriginX + heights.SizeX > ZoneGrid.ChunkSamples || tile.OriginZ + heights.SizeZ > ZoneGrid.ChunkSamples)
                {
                    error(file, $"tile {tile.Piece}: {heights.SizeX}×{heights.SizeZ} samples from [{tile.OriginX}, {tile.OriginZ}] reach past the chunk's {ZoneGrid.ChunkSamples}×{ZoneGrid.ChunkSamples}.");
                }

                if (tile.Underside is { } under && (under.SizeX != heights.SizeX || under.SizeZ != heights.SizeZ))
                {
                    error(file, $"tile {tile.Piece}: the underside is {under.SizeX}×{under.SizeZ} samples; the heights are {heights.SizeX}×{heights.SizeZ}.");
                }

                if (tile.Colors is { } colors && (colors.SizeX != heights.SizeX || colors.SizeZ != heights.SizeZ))
                {
                    error(file, $"tile {tile.Piece}: the colors are {colors.SizeX}×{colors.SizeZ} samples; the heights are {heights.SizeX}×{heights.SizeZ}.");
                }
            }
        }

        // Neighboring chunks repeat the samples on their shared edges (and corners), so where two tiles of one piece
        // both cover a sample, they must agree on whether it's ground and, within half a height step, how high.
        private static void CheckSharedEdges(Zone zone, Action<string, string> error)
        {
            var tiles = zone.Chunks
                .SelectMany(c => c.Tiles.Select(t => (Chunk: c.Coord, Tile: t)))
                .GroupBy(t => t.Tile.Piece);
            foreach (var piece in tiles)
            {
                var list = piece.ToList();
                for (var i = 0; i < list.Count; i++)
                {
                    for (var j = i + 1; j < list.Count; j++)
                    {
                        var (a, b) = (list[i], list[j]);
                        if (a.Chunk.Equals(b.Chunk) || Math.Abs(a.Chunk.X - b.Chunk.X) > 1 || Math.Abs(a.Chunk.Z - b.Chunk.Z) > 1)
                        {
                            continue;
                        }

                        var mismatch = FirstMismatch(a.Chunk, a.Tile, b.Chunk, b.Tile);
                        if (mismatch is { } at)
                        {
                            var (first, second) = string.CompareOrdinal(a.Chunk.ToString(), b.Chunk.ToString()) < 0 ? (a.Chunk, b.Chunk) : (b.Chunk, a.Chunk);
                            error(ZoneFolder.ChunkFile(first), $"tile {piece.Key}: the terrain doesn't match chunk {second}'s where they meet (world sample [{at.X}, {at.Z}]).");
                        }
                    }
                }
            }
        }

        private static (int X, int Z)? FirstMismatch(ChunkCoord chunkA, TerrainTile a, ChunkCoord chunkB, TerrainTile b)
        {
            // Both tiles in world sample coordinates (a chunk's first sample is at chunk × ChunkSize).
            int ax = chunkA.X * ZoneGrid.ChunkSize + a.OriginX, az = chunkA.Z * ZoneGrid.ChunkSize + a.OriginZ;
            int bx = chunkB.X * ZoneGrid.ChunkSize + b.OriginX, bz = chunkB.Z * ZoneGrid.ChunkSize + b.OriginZ;
            int minX = Math.Max(ax, bx), maxX = Math.Min(ax + a.Heights.SizeX, bx + b.Heights.SizeX) - 1;
            int minZ = Math.Max(az, bz), maxZ = Math.Min(az + a.Heights.SizeZ, bz + b.Heights.SizeZ) - 1;
            var tolerance = Math.Min(a.Heights.Scale, b.Heights.Scale) / 2;
            for (var z = minZ; z <= maxZ; z++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var groundA = a.Heights.TryGetHeight(x - ax, z - az, out var heightA);
                    var groundB = b.Heights.TryGetHeight(x - bx, z - bz, out var heightB);
                    if (groundA != groundB || (groundA && Math.Abs(heightA - heightB) > tolerance))
                    {
                        return (x, z);
                    }
                }
            }

            return null;
        }

        private static void CheckPivot(ChunkCoord chunk, Vector3 position, string what, string file, Action<string, string> error)
        {
            if (!ChunkCoord.Containing(position).Equals(chunk))
            {
                error(file, $"{what} at {Text(position)} is in chunk {ChunkCoord.Containing(position)}: objects belong to the chunk holding their pivot.");
            }
        }

        private static void CheckBox(Vector3 size, string what, string file, Action<string, string> error)
        {
            if (size.X <= 0 || size.Y <= 0 || size.Z <= 0)
            {
                error(file, $"{what}: size must be above 0 on every axis.");
            }
        }

        private static string Text(Vector3 v) => FormattableString.Invariant($"[{v.X}, {v.Y}, {v.Z}]");
    }
}
