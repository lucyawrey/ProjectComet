using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Comet.Content;
using Group = Comet.Content.Group;
using Comet.Simulation;

namespace Comet.ZoneFiles
{
    /// <summary>
    /// Checks what a zone's files mean, after <see cref="ZoneFolder.Read"/> has checked their form: keys and names,
    /// tiles that name a known piece and fit their chunk, terrain that matches where a piece's tiles meet, objects
    /// whose pivots are in their own chunk, spawn, teleporter and trigger keys that name their zone, and toggle effects
    /// that name placements in it. Run by the content build, and by the editor before it saves.
    /// </summary>
    public static class ZoneValidation
    {
        private static readonly Regex NamePattern = new Regex("^[a-z][a-z0-9_]*$");
        private static readonly Regex PlacementName = new Regex("^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)*$");

        /// <summary>A name in keys and in the zone: a lowercase letter, then lowercase letters, digits and underscores.</summary>
        public static bool IsName(string name) => NamePattern.IsMatch(name);

        /// <summary>Whether <paramref name="key"/> is <c>{kind}.zone.name</c>, as spawn, teleporter and trigger keys are.</summary>
        public static bool IsZonedKey(string key, string kind) =>
            key.StartsWith(kind + ".", StringComparison.Ordinal) && key.Substring(kind.Length + 1).Split('.') is { Length: 2 } parts && IsName(parts[0]) && IsName(parts[1]);

        /// <summary>The zone named in a <c>{kind}.zone.name</c> key, as a zone key (<c>zone.zone</c>).</summary>
        public static string ZoneOf(string key) => "zone." + key.Split('.')[1];

        /// <summary>The kinds a trigger volume can be.</summary>
        public static readonly PrimitiveKind[] VolumeKinds = { PrimitiveKind.Box, PrimitiveKind.Cylinder, PrimitiveKind.Sphere };

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
            foreach (var piece in zone.Pieces.Where(p => p.SkirtDepth < 0))
            {
                Error(zoneFile, $"piece {piece.Name}: skirt_depth can't be negative.");
            }

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

            // Names may be shared: a toggle effect flips every placement with its name.
            var placementNames = new HashSet<string>();
            foreach (var chunk in zone.Chunks)
            {
                foreach (var placement in chunk.Placements.Where(p => p.Name is not null))
                {
                    if (!PlacementName.IsMatch(placement.Name!))
                    {
                        Error(ZoneFolder.ChunkFile(chunk.Coord), $"placement of {Placed(placement)}: '{placement.Name}' isn't a valid name: use lowercase letters, digits, underscores and dots, starting with a letter.");
                    }

                    placementNames.Add(placement.Name!);
                }
            }

            var pieces = new HashSet<string>(zone.Pieces.Select(p => p.Name));
            var keyFiles = new Dictionary<string, string>();
            var zoneName = zone.Id.StartsWith("zone.", StringComparison.Ordinal) ? zone.Id.Substring("zone.".Length) : zone.Id;
            foreach (var chunk in zone.Chunks)
            {
                var file = ZoneFolder.ChunkFile(chunk.Coord);
                CheckTiles(chunk, pieces, file, Error);
                foreach (var placement in chunk.Placements)
                {
                    var what = $"placement of {Placed(placement)}";
                    CheckPlaced(placement, $"{what} at {Text(placement.Position)}", inGroup: false, file, Error);
                    CheckPivot(chunk.Coord, placement.Position, what, file, Error);
                }

                foreach (var spawn in chunk.Spawns)
                {
                    CheckTrigger(spawn.Id, "spawn", spawn.Volume, chunk.Coord, zoneName, keyFiles, file, Error);
                }

                foreach (var teleporter in chunk.Teleporters)
                {
                    CheckTrigger(teleporter.Id, "teleporter", teleporter.Volume, chunk.Coord, zoneName, keyFiles, file, Error);
                    if (!IsZonedKey(teleporter.Target, "teleporter"))
                    {
                        Error(file, $"{teleporter.Id}: target '{teleporter.Target}' isn't a teleporter key: use 'teleporter.', the zone's name, '.' and a name.");
                    }
                    else if (teleporter.Target == teleporter.Id)
                    {
                        Error(file, $"{teleporter.Id}: a teleporter can't target itself.");
                    }
                }

                foreach (var trigger in chunk.Triggers)
                {
                    CheckTrigger(trigger.Id, "trigger", trigger.Volume, chunk.Coord, zoneName, keyFiles, file, Error);
                    foreach (var effect in trigger.Effects)
                    {
                        if (!placementNames.Contains(effect.Name))
                        {
                            Error(file, $"{trigger.Id}: no placement in the zone is named '{effect.Name}'.");
                        }

                        if (effect.RevertSeconds < 0)
                        {
                            Error(file, $"{trigger.Id}: revert_seconds can't be negative.");
                        }
                    }
                }

                foreach (var volume in chunk.Volumes)
                {
                    CheckPivot(chunk.Coord, volume.Position, $"{ZoneToml.SnakeCase(volume.Kind.ToString())} volume", file, Error);
                    CheckBox(volume.Size, $"{ZoneToml.SnakeCase(volume.Kind.ToString())} volume at {Text(volume.Position)}", file, Error);
                }
            }

            CheckSharedEdges(zone, Error);
            return errors;
        }

        /// <summary>
        /// The problems with <paramref name="zone"/>'s placements given its props and groups (found by key through
        /// <paramref name="findProp"/> and <paramref name="findGroup"/>, null for an unknown key): unknown props and
        /// groups, uneven scale on a prop that only scales evenly, and objects reaching past the ring of chunks around
        /// their pivot's chunk. A group copy's parts are checked where they land, each against its own chunk, as the
        /// content build places them (<see cref="Groups.Expand(Zone, Func{string, Group?})"/>).
        /// </summary>
        public static List<ZoneFileError> CheckPlacements(Zone zone, Func<string, Prop?> findProp, Func<string, Group?>? findGroup = null)
        {
            var errors = new List<ZoneFileError>();
            foreach (var chunk in zone.Chunks)
            {
                var file = ZoneFolder.ChunkFile(chunk.Coord);
                foreach (var copy in chunk.Placements)
                {
                    if (copy.Group is not { } key)
                    {
                        CheckReach(copy, chunk.Coord, $"placement of {Placed(copy)} at {Text(copy.Position)}", file, findProp, errors);
                        continue;
                    }

                    if (findGroup?.Invoke(key) is null)
                    {
                        errors.Add(new ZoneFileError(file, $"placement of {key} at {Text(copy.Position)}: there's no group '{key}'."));
                        continue;
                    }

                    CheckRoleColors(copy, $"placement of {key} at {Text(copy.Position)}", file, findGroup, (f, m) => errors.Add(new ZoneFileError(f, m)));
                    foreach (var part in Groups.Expand(copy, findGroup))
                    {
                        CheckReach(part, ChunkCoord.Containing(part.Position), $"placement of {key} at {Text(copy.Position)}, its part {Placed(part)} at {Text(part.Position)}", file, findProp, errors);
                    }
                }
            }

            return errors;
        }

        /// <summary>
        /// The problems with a game's groups, each against its file: keys, parts that fit a group (no names; roles are
        /// names), unknown props and groups, and groups that contain themselves.
        /// </summary>
        public static List<ZoneFileError> CheckGroups(IReadOnlyList<(Group Group, string File)> groups, Func<string, Prop?> findProp)
        {
            var errors = new List<ZoneFileError>();
            void Error(string file, string message) => errors.Add(new ZoneFileError(file, message));
            var byKey = new Dictionary<string, (Group Group, string File)>();
            foreach (var (group, file) in groups)
            {
                if (!IsKey(group.Id, "group"))
                {
                    Error(file, $"id: '{group.Id}' isn't a valid group key: use 'group.' then lowercase letters, digits and underscores.");
                }
                else if (byKey.TryGetValue(group.Id, out var first))
                {
                    Error(file, $"id: '{group.Id}' is already used by {first.File}.");
                }
                else
                {
                    byKey.Add(group.Id, (group, file));
                }

                if (group.Parts.Length == 0)
                {
                    Error(file, "a group needs at least one part ([[part]]).");
                }

                foreach (var (part, i) in group.Parts.Select((p, i) => (p, i)))
                {
                    var what = $"part {i + 1} ({Placed(part)})";
                    CheckPlaced(part, what, inGroup: true, file, Error);
                    if (part.Group is { } inner && IsKey(inner, "group") && !groups.Any(g => g.Group.Id == inner))
                    {
                        Error(file, $"{what}: there's no group '{inner}'.");
                    }
                    else if (part.Primitive is null && part.Group is null && IsKey(part.Prop, "prop") && findProp(part.Prop) is { } prop
                        && prop.UniformScale && !Even(part.Scale))
                    {
                        Error(file, $"{what}: {prop.Id} only scales evenly, the same on every axis.");
                    }
                    else if (part.Primitive is null && part.Group is null && IsKey(part.Prop, "prop") && findProp(part.Prop) is null)
                    {
                        Error(file, $"{what}: there's no prop '{part.Prop}'.");
                    }
                }
            }

            // A group may not contain itself, however deep.
            var visiting = new List<string>();
            var done = new HashSet<string>();
            void Visit(string key)
            {
                if (done.Contains(key) || !byKey.TryGetValue(key, out var found))
                {
                    return;
                }

                var at = visiting.IndexOf(key);
                if (at >= 0)
                {
                    Error(found.File, $"{key} contains itself: {string.Join(" > ", visiting.Skip(at))} > {key}.");
                    return;
                }

                visiting.Add(key);
                foreach (var inner in found.Group.Parts.Select(p => p.Group).OfType<string>())
                {
                    Visit(inner);
                }

                visiting.RemoveAt(visiting.Count - 1);
                done.Add(key);
            }

            foreach (var key in byKey.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                Visit(key);
            }

            Group? Find(string key) => byKey.TryGetValue(key, out var found) ? found.Group : null;
            foreach (var (group, file) in groups)
            {
                foreach (var (part, i) in group.Parts.Select((p, i) => (p, i)))
                {
                    CheckRoleColors(part, $"part {i + 1} ({Placed(part)})", file, Find, Error);
                }
            }

            return errors;
        }

        /// <summary>
        /// The zone's blockout placements, each against its chunk file: stand-ins to finish before a release. A group
        /// copy counts if it or any part it expands to is blockout.
        /// </summary>
        public static List<ZoneFileError> Blockout(Zone zone, Func<string, Group?> findGroup)
        {
            var found = new List<ZoneFileError>();
            foreach (var chunk in zone.Chunks)
            {
                foreach (var placement in chunk.Placements)
                {
                    if (placement.Blockout)
                    {
                        found.Add(new ZoneFileError(ZoneFolder.ChunkFile(chunk.Coord), $"{Placed(placement)} at {Text(placement.Position)} is blockout; replace or finish it before a release."));
                    }
                    else if (placement.Group is { } key && Groups.Expand(placement, findGroup).Count(p => p.Blockout) is var parts and > 0)
                    {
                        found.Add(new ZoneFileError(ZoneFolder.ChunkFile(chunk.Coord), $"{key} at {Text(placement.Position)} has {parts} blockout {(parts == 1 ? "part" : "parts")}; replace or finish them before a release."));
                    }
                }
            }

            return found;
        }

        // A placement's reach: its bounds once placed stay within the ring of chunks around `chunk`.
        private static void CheckReach(Placement placement, ChunkCoord chunk, string what, string file, Func<string, Prop?> findProp, List<ZoneFileError> errors)
        {
            {
                {
                    (Vector3 Min, Vector3 Max) local;
                    if (placement.Primitive is { } primitive)
                    {
                        local = PrimitiveMesher.Bounds(primitive, placement.Scale);
                    }
                    else
                    {
                        var prop = findProp(placement.Prop);
                        if (prop is null)
                        {
                            errors.Add(new ZoneFileError(file, $"{what}: there's no prop '{placement.Prop}'."));
                            return;
                        }

                        var scale = placement.Scale;
                        if (prop.UniformScale && (Math.Abs(scale.X - scale.Y) > 1e-4f || Math.Abs(scale.X - scale.Z) > 1e-4f))
                        {
                            errors.Add(new ZoneFileError(file, $"{what}: {prop.Id} only scales evenly, the same on every axis."));
                        }

                        local = (prop.BoundsMin, prop.BoundsMax);
                    }

                    var (min, max) = Bounds(local.Min, local.Max, placement.Matrix());
                    var reachMin = new Vector3((chunk.X - 1) * ZoneGrid.ChunkSize, float.MinValue, (chunk.Z - 1) * ZoneGrid.ChunkSize);
                    var reachMax = new Vector3((chunk.X + 2) * ZoneGrid.ChunkSize, float.MaxValue, (chunk.Z + 2) * ZoneGrid.ChunkSize);
                    if (min.X < reachMin.X || min.Z < reachMin.Z || max.X > reachMax.X || max.Z > reachMax.Z)
                    {
                        errors.Add(new ZoneFileError(file, $"{what}: it reaches past the chunks around its own; objects can reach at most one chunk ({ZoneGrid.ChunkSize} m) beyond the chunk holding their pivot."));
                    }
                }
            }
        }

        // A group copy's colors by role: each names a role its group's parts play.
        private static void CheckRoleColors(Placement copy, string what, string file, Func<string, Group?> findGroup, Action<string, string> error)
        {
            if (copy.Colors is not { } colors || copy.Group is not { } group)
            {
                return;
            }

            var roles = Groups.Roles(group, findGroup);
            foreach (var role in colors.Keys.Where(r => !roles.Contains(r)))
            {
                error(file, roles.Count == 0
                    ? $"{what}: colors has '{role}', but {group}'s parts play no roles."
                    : $"{what}: colors has '{role}', which none of {group}'s parts plays ({string.Join(", ", roles)}).");
            }
        }

        // What a placement places, for messages: its prop's or group's key, or its primitive's kind.
        private static string Placed(Placement placement) =>
            placement.Primitive is { } primitive ? $"a {ZoneToml.SnakeCase(primitive.Kind.ToString())}" : placement.Group ?? placement.Prop;

        private static bool Even(Vector3 scale) => Math.Abs(scale.X - scale.Y) <= 1e-4f && Math.Abs(scale.X - scale.Z) <= 1e-4f;

        // What a placement places and how, wherever it is: a prop key, a primitive's values or a group key; a scale
        // above 0 (even for a group); a role only on a group's parts, a name and starting off only in a zone, and no
        // color of a group copy's own.
        private static void CheckPlaced(Placement placement, string what, bool inGroup, string file, Action<string, string> error)
        {
            if (placement.Primitive is { } primitive)
            {
                CheckPrimitive(primitive, what, file, error);
            }
            else if (placement.Group is { } group)
            {
                if (!IsKey(group, "group"))
                {
                    error(file, $"{what}: '{group}' isn't a group key.");
                }

                if (!Even(placement.Scale))
                {
                    error(file, $"{what}: a group scales evenly, the same on every axis.");
                }

                if (placement.Color != null)
                {
                    error(file, $"{what}: a group's copies have no color of their own; set colors by role (colors = {{ role = 0x… }}) or change the group's parts.");
                }
            }
            else if (!IsKey(placement.Prop, "prop"))
            {
                error(file, $"{what}: '{placement.Prop}' isn't a prop key.");
            }

            if (placement.Scale.X <= 0 || placement.Scale.Y <= 0 || placement.Scale.Z <= 0)
            {
                error(file, $"{what}: scale must be above 0 on every axis.");
            }

            if (inGroup && placement.Name != null)
            {
                error(file, $"{what}: a group's parts have no name; give them a role for game code to find them by, or name the copy.");
            }

            if (inGroup && placement.Off)
            {
                error(file, $"{what}: a group's parts don't start off; set it on the copy.");
            }

            if (placement.Role is { } role && (!inGroup || !IsName(role)))
            {
                error(file, inGroup ? $"{what}: '{role}' isn't a valid role: use lowercase letters, digits and underscores." : $"{what}: only a group's parts have a role.");
            }

            if (placement.Colors is { } colors && placement.Group is null)
            {
                error(file, $"{what}: only a group's copies have colors by role; give it a color instead.");
            }
        }

        // A primitive's values: in range, and only those its kind uses.
        private static void CheckPrimitive(Primitive primitive, string what, string file, Action<string, string> error)
        {
            var kind = primitive.Kind;
            if (primitive.Sides is { } sides)
            {
                if (kind is not (PrimitiveKind.Cylinder or PrimitiveKind.Cone or PrimitiveKind.Sphere or PrimitiveKind.Arch))
                {
                    error(file, $"{what}: only cylinders, cones, spheres and arches have sides.");
                }
                else if (sides < 3 || sides > 256)
                {
                    error(file, $"{what}: sides must be from 3 to 256.");
                }
            }

            if (primitive.Bevel != 0 && (kind != PrimitiveKind.Box || primitive.Bevel < 0))
            {
                error(file, $"{what}: only boxes have a bevel, and it can't be negative.");
            }

            if (primitive.Steps != 0 && (kind != PrimitiveKind.Stairs || primitive.Steps < 0 || primitive.Steps > Primitive.MaxSteps))
            {
                error(file, $"{what}: only stairs have steps, from 1 to {Primitive.MaxSteps}.");
            }

            if ((primitive.OpeningWidth != 0 || primitive.OpeningHeight != 0) &&
                (kind != PrimitiveKind.Doorway || primitive.OpeningWidth < 0 || primitive.OpeningHeight < 0))
            {
                error(file, $"{what}: only doorways have an opening, and it can't be negative.");
            }

            if (primitive.Thickness != 0 && (kind != PrimitiveKind.Arch || primitive.Thickness < 0))
            {
                error(file, $"{what}: only arches have a thickness, and it can't be negative.");
            }
        }

        // The world box around a mesh's bounds once placed.
        private static (Vector3 Min, Vector3 Max) Bounds(Vector3 boundsMin, Vector3 boundsMax, Matrix4x4 matrix)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (var corner = 0; corner < 8; corner++)
            {
                var point = Vector3.Transform(new Vector3(
                    (corner & 1) == 0 ? boundsMin.X : boundsMax.X,
                    (corner & 2) == 0 ? boundsMin.Y : boundsMax.Y,
                    (corner & 4) == 0 ? boundsMin.Z : boundsMax.Z), matrix);
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

                if (tile.UndersideColors is { } underColors)
                {
                    if (tile.Underside is null)
                    {
                        error(file, $"tile {tile.Piece}: has underside colors but no underside (delete its .under.color file).");
                    }
                    else if (underColors.SizeX != heights.SizeX || underColors.SizeZ != heights.SizeZ)
                    {
                        error(file, $"tile {tile.Piece}: the underside colors are {underColors.SizeX}×{underColors.SizeZ} samples; the heights are {heights.SizeX}×{heights.SizeZ}.");
                    }
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

        // A spawn point's, teleporter's or trigger's key, volume and pad.
        private static void CheckTrigger(string key, string kind, TriggerVolume volume, ChunkCoord chunk, string zoneName, Dictionary<string, string> keyFiles, string file, Action<string, string> error)
        {
            if (!IsZonedKey(key, kind) || key.Split('.')[1] != zoneName)
            {
                error(file, $"{kind}: '{key}' isn't a valid {kind} key here: use '{kind}.{zoneName}.' then lowercase letters, digits and underscores.");
            }
            else if (keyFiles.TryGetValue(key, out var first))
            {
                error(file, $"{kind}: '{key}' is already used in {first}.");
            }
            else
            {
                keyFiles.Add(key, file);
            }

            CheckPivot(chunk, volume.Position, key, file, error);
            if (Array.IndexOf(VolumeKinds, volume.Kind) < 0)
            {
                error(file, $"{key}: a volume is a box, a cylinder or a sphere, not a {ZoneToml.SnakeCase(volume.Kind.ToString())}.");
            }

            CheckBox(volume.Size, key, file, error);
            if (volume.StandSeconds < 0)
            {
                error(file, $"{key}: stand_seconds can't be negative.");
            }

            if (volume.Pad is { } pad)
            {
                if (pad.Primitive is null)
                {
                    error(file, $"{key}: a pad is a primitive.");
                }
                else
                {
                    CheckPlaced(pad, $"{key}'s pad", inGroup: false, file, error);
                }

                if (pad.Name is not null || pad.Off)
                {
                    error(file, $"{key}: a pad can't be named or start off; pads aren't toggled.");
                }

                CheckPivot(chunk, pad.Position, $"{key}'s pad", file, error);
            }
        }

        private static string Text(Vector3 v) => FormattableString.Invariant($"[{v.X}, {v.Y}, {v.Z}]");
    }
}
