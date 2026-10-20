using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MessagePack;

namespace Comet.Content
{
    /// <summary>
    /// A saved set of parts (primitives, assets and other groups), placed as one: every copy looks like the group, so
    /// editing it changes them all. Parts are placements relative to the group's pivot. The content build expands each
    /// copy into its parts (<see cref="Groups.Expand(Zone, Func{string, Group?})"/>).
    /// </summary>
    [MessagePackObject]
    public sealed class Group : IContentEntry
    {
        [Key(0)] public int Number { get; set; }

        /// <summary>The group's key (<c>group.tree</c>).</summary>
        [Key(1)] public string Id { get; set; } = "";

        /// <summary>The group's parts, placed relative to its pivot.</summary>
        [Key(2)] public Placement[] Parts { get; set; } = new Placement[0];
    }

    /// <summary>Expands group copies into the placements they stand for.</summary>
    public static class Groups
    {
        /// <summary>How deeply groups may nest; deeper (or circular) nesting places nothing past it, and validation reports it.</summary>
        public const int MaxDepth = 16;

        /// <summary>
        /// The placements a copy of a group stands for, in its zone's space, with nested groups expanded. An unknown
        /// group places nothing (validation reports it).
        /// </summary>
        public static List<Placement> Expand(Placement copy, Func<string, Group?> find)
        {
            var placements = new List<Placement>();
            Add(copy, find, placements, 0);
            return placements;
        }

        /// <summary>
        /// Replaces every group copy in <paramref name="zone"/> with its parts, each in the chunk holding its pivot
        /// (made if the zone has none there). Changes the zone in place.
        /// </summary>
        public static void Expand(Zone zone, Func<string, Group?> find)
        {
            var chunks = zone.Chunks.ToDictionary(c => c.Coord);
            var moved = new List<Placement>();
            foreach (var chunk in zone.Chunks)
            {
                if (chunk.Placements.Any(p => p.Group != null))
                {
                    moved.AddRange(chunk.Placements.Where(p => p.Group != null).SelectMany(p => Expand(p, find)));
                    chunk.Placements = chunk.Placements.Where(p => p.Group == null).ToArray();
                }
            }

            foreach (var placement in moved)
            {
                var coord = ChunkCoord.Containing(placement.Position);
                if (!chunks.TryGetValue(coord, out var chunk))
                {
                    chunk = new ZoneChunk { Coord = coord };
                    chunks.Add(coord, chunk);
                }

                chunk.Placements = chunk.Placements.Append(placement).ToArray();
            }

            zone.Chunks = chunks.Values.OrderBy(c => c.Coord.X).ThenBy(c => c.Coord.Z).ToArray();
        }

        /// <summary>
        /// <paramref name="group"/>'s parts placed by a copy's transform (turned by its rotation, scaled by its even
        /// scale, then moved to it), one level only: nested groups stay copies. The copy's colors by role recolor the
        /// parts with those roles and pass down to nested copies, over their own.
        /// </summary>
        public static List<Placement> Place(Placement copy, Group group)
        {
            var rotation = ZoneGrid.Rotation(copy.Rotation);
            var scale = copy.Scale.X;
            return group.Parts.Select(part => new Placement
            {
                Asset = part.Asset,
                Primitive = part.Primitive,
                Group = part.Group,
                Role = part.Role,
                Position = copy.Position + Vector3.Transform(part.Position * scale, rotation),
                Rotation = ZoneGrid.Euler(Quaternion.Concatenate(ZoneGrid.Rotation(part.Rotation), rotation)),
                Scale = part.Scale * scale,
                Color = part.Role is { } role && copy.Colors?.TryGetValue(role, out var color) == true ? color : part.Color,
                Colors = part.Group is null ? null : Over(copy.Colors, part.Colors),
                Blockout = part.Blockout || copy.Blockout,
            }).ToList();
        }

        /// <summary>The roles a group's parts play, its nested groups' included.</summary>
        public static SortedSet<string> Roles(string group, Func<string, Group?> find) =>
            new(Expand(new Placement { Group = group }, find).Select(p => p.Role).OfType<string>());

        // Colors by role with the outer ones winning, or null for none.
        private static Dictionary<string, uint>? Over(Dictionary<string, uint>? outer, Dictionary<string, uint>? inner)
        {
            if (outer is null || inner is null)
            {
                return outer ?? inner;
            }

            var colors = new Dictionary<string, uint>(inner);
            foreach (var (role, color) in outer)
            {
                colors[role] = color;
            }

            return colors;
        }

        private static void Add(Placement copy, Func<string, Group?> find, List<Placement> into, int depth)
        {
            if (depth >= MaxDepth || copy.Group is null || find(copy.Group) is not { } group)
            {
                return;
            }

            foreach (var placed in Place(copy, group))
            {
                if (placed.Group != null)
                {
                    Add(placed, find, into, depth + 1);
                }
                else
                {
                    into.Add(placed);
                }
            }
        }
    }
}
