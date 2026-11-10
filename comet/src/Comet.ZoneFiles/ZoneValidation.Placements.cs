using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Comet.Content;
using Comet.Simulation;
using Group = Comet.Content.Group;

namespace Comet.ZoneFiles
{
    public static partial class ZoneValidation
    {
        /// <summary>
        /// <paramref name="findProp"/> and <paramref name="findGroup"/> return null for an unknown id. A group copy's
        /// parts are checked where they land, each against its own chunk, as the content build places them.
        /// </summary>
        public static List<ZoneFileError> CheckPlacements(Zone zone, Func<string, Prop?> findProp, Func<string, Group?>? findGroup = null)
        {
            var errors = new List<ZoneFileError>();
            foreach (var chunk in zone.Chunks)
            {
                var file = ZoneFolder.ChunkFile(chunk.Coord);
                foreach (var copy in chunk.Placements)
                {
                    if (copy.Group is not { } id)
                    {
                        CheckReach(copy, chunk.Coord, $"placement of {Placed(copy)} at {Text(copy.Position)}", file, findProp, errors);
                        continue;
                    }

                    if (findGroup?.Invoke(id) is null)
                    {
                        errors.Add(new ZoneFileError(file, $"placement of {id} at {Text(copy.Position)}: there's no group '{id}'."));
                        continue;
                    }

                    CheckRoleColors(
                        copy,
                        $"placement of {id} at {Text(copy.Position)}",
                        file,
                        findGroup,
                        (f, m) => errors.Add(new ZoneFileError(f, m))
                    );
                    foreach (var part in Groups.Expand(copy, findGroup))
                    {
                        CheckReach(
                            part,
                            ChunkCoord.Containing(part.Position),
                            $"placement of {id} at {Text(copy.Position)}, its part {Placed(part)} at {Text(part.Position)}",
                            file,
                            findProp,
                            errors
                        );
                    }
                }
            }

            return errors;
        }

        public static List<ZoneFileError> CheckGroups(IReadOnlyList<(Group Group, string File)> groups, Func<string, Prop?> findProp)
        {
            var errors = new List<ZoneFileError>();
            void Error(string file, string message) => errors.Add(new ZoneFileError(file, message));
            var byId = new Dictionary<string, (Group Group, string File)>();
            foreach (var (group, file) in groups)
            {
                if (!IsId(group.Id, "group"))
                {
                    Error(file, $"id: '{group.Id}' isn't a valid group id: use 'group.' then lowercase letters, digits and underscores.");
                }
                else if (byId.TryGetValue(group.Id, out var first))
                {
                    Error(file, $"id: '{group.Id}' is already used by {first.File}.");
                }
                else
                {
                    byId.Add(group.Id, (group, file));
                }

                if (group.Parts.Length == 0)
                {
                    Error(file, "a group needs at least one part ([[part]]).");
                }

                foreach (var (part, i) in group.Parts.Select((p, i) => (p, i)))
                {
                    var what = $"part {i + 1} ({Placed(part)})";
                    CheckPlaced(part, what, inGroup: true, file, Error);
                    if (part.Group is { } inner && IsId(inner, "group") && !groups.Any(g => g.Group.Id == inner))
                    {
                        Error(file, $"{what}: there's no group '{inner}'.");
                    }
                    else if (
                        part.Primitive is null
                        && part.Group is null
                        && IsId(part.Prop, "prop")
                        && findProp(part.Prop) is { } prop
                        && prop.UniformScale
                        && !Even(part.Scale)
                    )
                    {
                        Error(file, $"{what}: {prop.Id} only scales evenly, the same on every axis.");
                    }
                    else if (part.Primitive is null && part.Group is null && IsId(part.Prop, "prop") && findProp(part.Prop) is null)
                    {
                        Error(file, $"{what}: there's no prop '{part.Prop}'.");
                    }
                }
            }

            var visiting = new List<string>();
            var done = new HashSet<string>();
            void Visit(string id)
            {
                if (done.Contains(id) || !byId.TryGetValue(id, out var found))
                {
                    return;
                }

                var at = visiting.IndexOf(id);
                if (at >= 0)
                {
                    Error(found.File, $"{id} contains itself: {string.Join(" > ", visiting.Skip(at))} > {id}.");
                    return;
                }

                visiting.Add(id);
                foreach (var inner in found.Group.Parts.Select(p => p.Group).OfType<string>())
                {
                    Visit(inner);
                }

                visiting.RemoveAt(visiting.Count - 1);
                done.Add(id);
            }

            foreach (var id in byId.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                Visit(id);
            }

            Group? Find(string id) => byId.TryGetValue(id, out var found) ? found.Group : null;
            foreach (var (group, file) in groups)
            {
                foreach (var (part, i) in group.Parts.Select((p, i) => (p, i)))
                {
                    CheckRoleColors(part, $"part {i + 1} ({Placed(part)})", file, Find, Error);
                }
            }

            return errors;
        }

        public static List<ZoneFileError> Blockout(Zone zone, Func<string, Group?> findGroup)
        {
            var found = new List<ZoneFileError>();
            foreach (var chunk in zone.Chunks)
            {
                foreach (var placement in chunk.Placements)
                {
                    if (placement.Blockout)
                    {
                        found.Add(
                            new ZoneFileError(
                                ZoneFolder.ChunkFile(chunk.Coord),
                                $"{Placed(placement)} at {Text(placement.Position)} is blockout; replace or finish it before a release."
                            )
                        );
                    }
                    else if (placement.Group is { } id && Groups.Expand(placement, findGroup).Count(p => p.Blockout) is var parts and > 0)
                    {
                        found.Add(
                            new ZoneFileError(
                                ZoneFolder.ChunkFile(chunk.Coord),
                                $"{id} at {Text(placement.Position)} has {parts} blockout {(parts == 1 ? "part" : "parts")}; replace or finish them before a release."
                            )
                        );
                    }
                }
            }

            return found;
        }

        private static void CheckReach(
            Placement placement,
            ChunkCoord chunk,
            string what,
            string file,
            Func<string, Prop?> findProp,
            List<ZoneFileError> errors
        )
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
                        errors.Add(
                            new ZoneFileError(
                                file,
                                $"{what}: it reaches past the chunks around its own; objects can reach at most one chunk ({ZoneGrid.ChunkSize} m) beyond the chunk holding their pivot."
                            )
                        );
                    }
                }
            }
        }

        private static void CheckRoleColors(Placement copy, string what, string file, Func<string, Group?> findGroup, Action<string, string> error)
        {
            if (copy.Colors is not { } colors || copy.Group is not { } group)
            {
                return;
            }

            var roles = Groups.Roles(group, findGroup);
            foreach (var role in colors.Keys.Where(r => !roles.Contains(r)))
            {
                error(
                    file,
                    roles.Count == 0
                        ? $"{what}: colors has '{role}', but {group}'s parts play no roles."
                        : $"{what}: colors has '{role}', which none of {group}'s parts plays ({string.Join(", ", roles)})."
                );
            }
        }

        private static string Placed(Placement placement) =>
            placement.Primitive is { } primitive ? $"a {ZoneToml.SnakeCase(primitive.Kind.ToString())}" : placement.Group ?? placement.Prop;

        private static bool Even(Vector3 scale) => Math.Abs(scale.X - scale.Y) <= 1e-4f && Math.Abs(scale.X - scale.Z) <= 1e-4f;

        private static void CheckPlaced(Placement placement, string what, bool inGroup, string file, Action<string, string> error)
        {
            if (placement.Primitive is { } primitive)
            {
                CheckPrimitive(primitive, what, file, error);
            }
            else if (placement.Group is { } group)
            {
                if (!IsId(group, "group"))
                {
                    error(file, $"{what}: '{group}' isn't a group id.");
                }

                if (!Even(placement.Scale))
                {
                    error(file, $"{what}: a group scales evenly, the same on every axis.");
                }

                if (placement.Color != null)
                {
                    error(
                        file,
                        $"{what}: a group's copies have no color of their own; set colors by role (colors = {{ role = 0x… }}) or change the group's parts."
                    );
                }
            }
            else if (!IsId(placement.Prop, "prop"))
            {
                error(file, $"{what}: '{placement.Prop}' isn't a prop id.");
            }

            if (placement.Scale.X <= 0 || placement.Scale.Y <= 0 || placement.Scale.Z <= 0)
            {
                error(file, $"{what}: scale must be above 0 on every axis.");
            }

            if (inGroup && placement.Tag != null)
            {
                error(file, $"{what}: a group's parts have no tag; give them a role for game code to find them by, or tag the copy.");
            }

            if (inGroup && placement.Off)
            {
                error(file, $"{what}: a group's parts don't start off; set it on the copy.");
            }

            if (placement.Role is { } role && (!inGroup || !IsName(role)))
            {
                error(
                    file,
                    inGroup
                        ? $"{what}: '{role}' isn't a valid role: use lowercase letters, digits and underscores."
                        : $"{what}: only a group's parts have a role."
                );
            }

            if (placement.Colors is { } colors && placement.Group is null)
            {
                error(file, $"{what}: only a group's copies have colors by role; give it a color instead.");
            }
        }

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

            if (
                (primitive.OpeningWidth != 0 || primitive.OpeningHeight != 0)
                && (kind != PrimitiveKind.Doorway || primitive.OpeningWidth < 0 || primitive.OpeningHeight < 0)
            )
            {
                error(file, $"{what}: only doorways have an opening, and it can't be negative.");
            }

            if (primitive.Thickness != 0 && (kind != PrimitiveKind.Arch || primitive.Thickness < 0))
            {
                error(file, $"{what}: only arches have a thickness, and it can't be negative.");
            }
        }

        private static (Vector3 Min, Vector3 Max) Bounds(Vector3 boundsMin, Vector3 boundsMax, Matrix4x4 matrix)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (var corner = 0; corner < 8; corner++)
            {
                var point = Vector3.Transform(
                    new Vector3(
                        (corner & 1) == 0 ? boundsMin.X : boundsMax.X,
                        (corner & 2) == 0 ? boundsMin.Y : boundsMax.Y,
                        (corner & 4) == 0 ? boundsMin.Z : boundsMax.Z
                    ),
                    matrix
                );
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
            }

            return (min, max);
        }
    }
}
