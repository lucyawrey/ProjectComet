using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Comet.Content;

namespace Comet.ZoneFiles
{
    /// <summary>
    /// Checks what a zone's files mean, after <see cref="ZoneFolder.Read"/> has checked their form: ids and names,
    /// tiles that name a known piece and fit their chunk, terrain that matches where a piece's tiles meet, objects
    /// whose pivots are in their own chunk, spawn, teleporter and trigger ids that name their zone, signs' text and
    /// ranges, and toggle effects that name placements in it. Run by the content build, and by the editor before it
    /// saves.
    /// </summary>
    public static partial class ZoneValidation
    {
        private static readonly Regex NamePattern = new Regex("^[a-z][a-z0-9_]*$");
        private static readonly Regex PlacementTag = new Regex("^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)*$");

        /// <summary>A name in ids and in the zone: a lowercase letter, then lowercase letters, digits and underscores.</summary>
        public static bool IsName(string name) => NamePattern.IsMatch(name);

        /// <summary>Whether <paramref name="id"/> is <c>{kind}.zone.name</c>, as spawn, teleporter and trigger ids are.</summary>
        public static bool IsZonedId(string id, string kind) =>
            id.StartsWith(kind + ".", StringComparison.Ordinal)
            && id.Substring(kind.Length + 1).Split('.') is { Length: 2 } parts
            && IsName(parts[0])
            && IsName(parts[1]);

        /// <summary>The zone named in a <c>{kind}.zone.name</c> id, as a zone id (<c>zone.zone</c>).</summary>
        public static string ZoneOf(string id) => "zone." + id.Split('.')[1];

        /// <summary>The kinds a trigger volume can be.</summary>
        public static readonly PrimitiveKind[] VolumeKinds = { PrimitiveKind.Box, PrimitiveKind.Cylinder, PrimitiveKind.Sphere };

        /// <summary>Whether <paramref name="id"/> is <c>{kind}.name</c>.</summary>
        public static bool IsId(string id, string kind) =>
            id.StartsWith(kind + ".", StringComparison.Ordinal) && IsName(id.Substring(kind.Length + 1));

        /// <summary>The problems in <paramref name="zone"/>, each against the file it's in.</summary>
        public static List<ZoneFileError> Check(Zone zone)
        {
            var errors = new List<ZoneFileError>();
            void Error(string file, string message) => errors.Add(new ZoneFileError(file, message));
            const string zoneFile = ZoneFolder.ZoneFile;

            if (!IsId(zone.Id, "zone"))
            {
                Error(zoneFile, $"id: '{zone.Id}' isn't a valid zone id: use 'zone.' then lowercase letters, digits and underscores.");
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

                if (!IsId(border.Zone, "zone") || border.Zone == zone.Id)
                {
                    Error(zoneFile, $"border: '{border.Zone}' isn't another zone's id.");
                }
            }

            foreach (var entrance in zone.Entrances)
            {
                CheckBox(entrance.Size, $"entrance {entrance.Name}", zoneFile, Error);
                if (!IsId(entrance.ToZone, "zone"))
                {
                    Error(zoneFile, $"entrance {entrance.Name}: to_zone '{entrance.ToZone}' isn't a zone id.");
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

            // Tags may be shared: a toggle effect flips every placement with its tag.
            var placementTags = new HashSet<string>();
            foreach (var chunk in zone.Chunks)
            {
                foreach (var placement in chunk.Placements.Where(p => p.Tag is not null))
                {
                    if (!PlacementTag.IsMatch(placement.Tag!))
                    {
                        Error(
                            ZoneFolder.ChunkFile(chunk.Coord),
                            $"placement of {Placed(placement)}: '{placement.Tag}' isn't a valid tag: use lowercase letters, digits, underscores and dots, starting with a letter."
                        );
                    }

                    placementTags.Add(placement.Tag!);
                }
            }

            var pieces = new HashSet<string>(zone.Pieces.Select(p => p.Name));
            var idFiles = new Dictionary<string, string>();
            var zoneName = zone.Id.StartsWith("zone.", StringComparison.Ordinal) ? zone.Id.Substring("zone.".Length) : zone.Id;
            foreach (var chunk in zone.Chunks)
            {
                var file = ZoneFolder.ChunkFile(chunk.Coord);
                CheckTiles(chunk, zone.HeightBase, pieces, file, Error);
                foreach (var placement in chunk.Placements)
                {
                    var what = $"placement of {Placed(placement)}";
                    CheckPlaced(placement, $"{what} at {Text(placement.Position)}", inGroup: false, file, Error);
                    CheckPivot(chunk.Coord, placement.Position, what, file, Error);
                }

                foreach (var spawn in chunk.Spawns)
                {
                    CheckTrigger(spawn.Id, "spawn", spawn.Volume, chunk.Coord, zoneName, idFiles, file, Error);
                }

                foreach (var teleporter in chunk.Teleporters)
                {
                    CheckTrigger(teleporter.Id, "teleporter", teleporter.Volume, chunk.Coord, zoneName, idFiles, file, Error);
                    // An empty target is a teleporter not linked yet, which the content build warns about.
                    if (teleporter.Target != "" && !IsZonedId(teleporter.Target, "teleporter"))
                    {
                        Error(
                            file,
                            $"{teleporter.Id}: target '{teleporter.Target}' isn't a teleporter id: use 'teleporter.', the zone's name, '.' and a name."
                        );
                    }
                    else if (teleporter.Target == teleporter.Id)
                    {
                        Error(file, $"{teleporter.Id}: a teleporter can't target itself.");
                    }
                }

                foreach (var trigger in chunk.Triggers)
                {
                    CheckTrigger(trigger.Id, "trigger", trigger.Volume, chunk.Coord, zoneName, idFiles, file, Error);
                    foreach (var effect in trigger.Effects)
                    {
                        if (!placementTags.Contains(effect.Tag))
                        {
                            Error(file, $"{trigger.Id}: no placement in the zone is tagged '{effect.Tag}'.");
                        }

                        if (effect.RevertSeconds < 0)
                        {
                            Error(file, $"{trigger.Id}: revert_seconds can't be negative.");
                        }
                    }
                }

                foreach (var sign in chunk.Signs)
                {
                    CheckPivot(chunk.Coord, sign.Position, "sign", file, Error);
                    CheckSign(sign, $"sign at {Text(sign.Position)}", file, Error);
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

        private static void CheckPivot(ChunkCoord chunk, Vector3 position, string what, string file, Action<string, string> error)
        {
            if (!ChunkCoord.Containing(position).Equals(chunk))
            {
                error(
                    file,
                    $"{what} at {Text(position)} is in chunk {ChunkCoord.Containing(position)}: objects belong to the chunk holding their pivot."
                );
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
