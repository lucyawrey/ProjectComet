using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Comet.Content;

namespace Comet.Simulation
{
    /// <summary>
    /// Builds primitives' meshes, the same everywhere they're needed (server and client collision, the client's and
    /// the editor's drawing). A mesh is flat-shaded triangles, three corners each, wound clockwise seen from outside
    /// (Unity's convention, as <see cref="CollisionMesh"/>), built at the placed size around the pivot (the bottom
    /// center), so bevels stay true to size and round primitives get sides from the polygon budget at their real size.
    /// The same triangles are drawn and collided with.
    /// </summary>
    public static class PrimitiveMesher
    {
        /// <summary>About how high each step of a stairway is when its step count isn't set, in meters.</summary>
        public const float DefaultStepHeight = 0.25f;

        /// <summary>The triangles of <paramref name="primitive"/> at <paramref name="size"/> meters.</summary>
        public static Vector3[] Build(Primitive primitive, Vector3 size, PolygonBudget budget)
        {
            size = Vector3.Abs(size);
            var mesh = new Builder();
            switch (primitive.Kind)
            {
                case PrimitiveKind.Box when primitive.Bevel > 0:
                    BeveledBox(mesh, size, primitive.Bevel);
                    break;
                case PrimitiveKind.Box:
                    mesh.Prism(Rectangle(size), size.Z);
                    break;
                case PrimitiveKind.Wedge:
                    mesh.Prism(new[] { new Vector2(-size.X / 2, 0), new Vector2(size.X / 2, 0), new Vector2(size.X / 2, size.Y) }, size.Z);
                    break;
                case PrimitiveKind.Cylinder:
                case PrimitiveKind.Cone:
                    Round(mesh, size, primitive.Sides ?? budget.Sides(Ellipse(size.X, size.Z)), primitive.Kind == PrimitiveKind.Cone);
                    break;
                case PrimitiveKind.Sphere:
                    Sphere(mesh, size, primitive.Sides, budget);
                    break;
                case PrimitiveKind.Stairs:
                    mesh.Prism(Stairs(size, primitive.Steps), size.Z);
                    break;
                case PrimitiveKind.Wall:
                    mesh.Prism(Wall(size, primitive.OpeningWidth, primitive.OpeningHeight), size.Z);
                    break;
                case PrimitiveKind.Arch:
                    mesh.Prism(Arch(size, primitive.OpeningWidth, primitive.OpeningHeight, primitive.Sides, budget), size.Z);
                    break;
                default:
                    Solid(mesh, primitive.Kind, size);
                    break;
            }

            return mesh.Triangles.ToArray();
        }

        /// <summary>
        /// The box <paramref name="primitive"/> fills at <paramref name="size"/>, around its pivot: what zone checks use
        /// for its reach. Most fill a box of their size; a regular solid is as wide as being regular makes it.
        /// </summary>
        public static (Vector3 Min, Vector3 Max) Bounds(Primitive primitive, Vector3 size)
        {
            size = Vector3.Abs(size);
            if (UnitSolids.IsSolid(primitive.Kind))
            {
                var (points, _) = UnitSolids.Get(primitive.Kind);
                return (points.Aggregate(Vector3.Min) * size, points.Aggregate(Vector3.Max) * size);
            }

            return (new Vector3(-size.X / 2, 0, -size.Z / 2), new Vector3(size.X / 2, size.Y, size.Z / 2));
        }

        // About the circumference of an ellipse with these diameters (Ramanujan's first approximation).
        private static float Ellipse(float width, float depth)
        {
            float a = width / 2, b = depth / 2;
            return MathF.PI * (3 * (a + b) - MathF.Sqrt((3 * a + b) * (a + 3 * b)));
        }

        private static Vector2[] Rectangle(Vector3 size) => new[]
        {
            new Vector2(-size.X / 2, 0), new Vector2(size.X / 2, 0), new Vector2(size.X / 2, size.Y), new Vector2(-size.X / 2, size.Y),
        };

        // Steps climbing toward +X, as a profile in X and Y (counterclockwise): step k's top is k step heights up.
        private static Vector2[] Stairs(Vector3 size, int steps)
        {
            var count = steps > 0 ? steps : Math.Max(1, (int)MathF.Round(size.Y / DefaultStepHeight));
            float run = size.X / count, rise = size.Y / count, left = -size.X / 2;
            var profile = new List<Vector2> { new Vector2(left, 0), new Vector2(size.X / 2, 0), new Vector2(size.X / 2, size.Y) };
            for (var k = count; k >= 1; k--)
            {
                profile.Add(new Vector2(left + (k - 1) * run, k * rise));
                if (k > 1)
                {
                    profile.Add(new Vector2(left + (k - 1) * run, (k - 1) * rise));
                }
            }

            return profile.ToArray();
        }

        // A wall's face (in X and Y, counterclockwise) with a door-shaped opening at the bottom center, kept inside the
        // wall; no opening if either side of it is 0.
        private static Vector2[] Wall(Vector3 size, float width, float height)
        {
            var (half, top) = Opening(size, width, height);
            if (half <= 0 || top <= 0)
            {
                return Rectangle(size);
            }

            return new[]
            {
                new Vector2(-size.X / 2, 0), new Vector2(-half, 0), new Vector2(-half, top), new Vector2(half, top),
                new Vector2(half, 0), new Vector2(size.X / 2, 0), new Vector2(size.X / 2, size.Y), new Vector2(-size.X / 2, size.Y),
            };
        }

        // An arch's face: a wall whose opening's top is a half circle as wide as the opening.
        private static Vector2[] Arch(Vector3 size, float width, float height, int? sides, PolygonBudget budget)
        {
            var (half, top) = Opening(size, width, height);
            if (half <= 0 || top <= 0)
            {
                return Rectangle(size);
            }

            var radius = Math.Min(half, top);
            var springs = top - radius; // where the curve starts, above the straight sides
            var segments = Math.Max(2, (sides ?? budget.Sides(2 * MathF.PI * radius)) / 2);
            var profile = new List<Vector2> { new Vector2(-size.X / 2, 0), new Vector2(-half, 0) };
            for (var i = 0; i <= segments; i++)
            {
                var angle = MathF.PI * (1 - (float)i / segments);
                var point = new Vector2(half * MathF.Cos(angle), springs + radius * MathF.Sin(angle));
                if (Vector2.DistanceSquared(point, profile[profile.Count - 1]) > 1e-8f)
                {
                    profile.Add(point);
                }
            }

            profile.Add(new Vector2(half, 0));
            profile.Add(new Vector2(size.X / 2, 0));
            profile.Add(new Vector2(size.X / 2, size.Y));
            profile.Add(new Vector2(-size.X / 2, size.Y));
            return profile.ToArray();
        }

        // An opening's half width and height, kept at least 5 cm inside the wall's sides and top.
        private static (float Half, float Top) Opening(Vector3 size, float width, float height)
        {
            const float rim = 0.05f;
            return (Math.Min(width / 2, size.X / 2 - rim), Math.Min(height, size.Y - rim));
        }

        // A box whose edges are cut back by the bevel: each main face inset, a strip along each edge, a triangle at each corner.
        private static void BeveledBox(Builder mesh, Vector3 size, float bevel)
        {
            var e = new Vector3(size.X / 2, size.Y / 2, size.Z / 2);
            var center = new Vector3(0, e.Y, 0);
            bevel = Math.Min(bevel, Math.Min(e.X, Math.Min(e.Y, e.Z)) * 0.999f);

            // The corner (sx, sy, sz) cut back across axis a: on that face, inset along the other two.
            Vector3 Point(int sx, int sy, int sz, int axis)
            {
                var x = sx * (axis == 0 ? e.X : e.X - bevel);
                var y = sy * (axis == 1 ? e.Y : e.Y - bevel);
                var z = sz * (axis == 2 ? e.Z : e.Z - bevel);
                return center + new Vector3(x, y, z);
            }

            int[] signs = { -1, 1 };
            foreach (var s in signs)
            {
                mesh.Convex(center, Point(s, -1, -1, 0), Point(s, 1, -1, 0), Point(s, 1, 1, 0), Point(s, -1, 1, 0));
                mesh.Convex(center, Point(-1, s, -1, 1), Point(1, s, -1, 1), Point(1, s, 1, 1), Point(-1, s, 1, 1));
                mesh.Convex(center, Point(-1, -1, s, 2), Point(1, -1, s, 2), Point(1, 1, s, 2), Point(-1, 1, s, 2));
            }

            foreach (var a in signs)
            {
                foreach (var b in signs)
                {
                    // The strips along X, Y and Z between the two faces they join.
                    mesh.Convex(center, Point(-1, a, b, 1), Point(1, a, b, 1), Point(1, a, b, 2), Point(-1, a, b, 2));
                    mesh.Convex(center, Point(a, -1, b, 0), Point(a, 1, b, 0), Point(a, 1, b, 2), Point(a, -1, b, 2));
                    mesh.Convex(center, Point(a, b, -1, 0), Point(a, b, 1, 0), Point(a, b, 1, 1), Point(a, b, -1, 1));
                    foreach (var c in signs)
                    {
                        mesh.Convex(center, Point(a, b, c, 0), Point(a, b, c, 1), Point(a, b, c, 2));
                    }
                }
            }
        }

        // A cylinder or cone around Y. Corners sit half a side off the axes, so 4 sides make faces square to them.
        private static void Round(Builder mesh, Vector3 size, int sides, bool cone)
        {
            sides = Math.Max(3, sides);
            var ring = Ring(size, sides, 1);
            var center = new Vector3(0, size.Y / 2, 0);
            var top = new Vector3(0, size.Y, 0);
            mesh.Convex(center, ring);
            if (cone)
            {
                for (var i = 0; i < sides; i++)
                {
                    mesh.Convex(center, ring[i], ring[(i + 1) % sides], top);
                }

                return;
            }

            var upper = ring.Select(p => p + top).ToArray();
            mesh.Convex(center, upper);
            for (var i = 0; i < sides; i++)
            {
                var next = (i + 1) % sides;
                mesh.Convex(center, ring[i], ring[next], upper[next], upper[i]);
            }
        }

        // A ring of corners around Y at height 0, at <paramref name="scale"/> of the size's width and depth.
        private static Vector3[] Ring(Vector3 size, int sides, float scale, float y = 0)
        {
            var ring = new Vector3[sides];
            for (var i = 0; i < sides; i++)
            {
                var angle = 2 * MathF.PI * (i + 0.5f) / sides;
                ring[i] = new Vector3(scale * size.X / 2 * MathF.Cos(angle), y, scale * size.Z / 2 * MathF.Sin(angle));
            }

            return ring;
        }

        // A sphere in rings around Y, from a point at the bottom to one at the top.
        private static void Sphere(Builder mesh, Vector3 size, int? fixedSides, PolygonBudget budget)
        {
            var sides = Math.Max(3, fixedSides ?? budget.Sides(Ellipse(size.X, size.Z)));
            var rings = Math.Max(2, (fixedSides ?? budget.Sides(Ellipse(size.Y, Math.Max(size.X, size.Z)))) / 2);
            var center = new Vector3(0, size.Y / 2, 0);
            var bands = new Vector3[rings + 1][];
            for (var r = 0; r <= rings; r++)
            {
                var polar = MathF.PI * r / rings; // 0 at the bottom
                var y = size.Y / 2 * (1 - MathF.Cos(polar));
                bands[r] = Ring(size, sides, MathF.Sin(polar), y);
            }

            for (var r = 0; r < rings; r++)
            {
                for (var i = 0; i < sides; i++)
                {
                    var next = (i + 1) % sides;
                    if (r == 0)
                    {
                        mesh.Convex(center, bands[0][0], bands[1][next], bands[1][i]);
                    }
                    else if (r == rings - 1)
                    {
                        mesh.Convex(center, bands[r][i], bands[r][next], bands[rings][0]);
                    }
                    else
                    {
                        mesh.Convex(center, bands[r][i], bands[r][next], bands[r + 1][next], bands[r + 1][i]);
                    }
                }
            }
        }

        // The regular solids, 1 m tall at a scale of 1, scaled to the size.
        private static void Solid(Builder mesh, PrimitiveKind kind, Vector3 size)
        {
            var (points, faces) = UnitSolids.Get(kind);
            var scaled = points.Select(p => p * size).ToArray();
            var center = new Vector3(0, size.Y / 2, 0);
            foreach (var face in faces)
            {
                mesh.Convex(center, face.Select(i => scaled[i]).ToArray());
            }
        }

        // Each regular solid's corners and faces at 1 m tall, standing on its pivot, built once.
        private static class UnitSolids
        {
            public static bool IsSolid(PrimitiveKind kind) =>
                kind is PrimitiveKind.Tetrahedron or PrimitiveKind.Octahedron or PrimitiveKind.Dodecahedron or PrimitiveKind.Icosahedron;

            private static readonly Dictionary<PrimitiveKind, (Vector3[] Points, int[][] Faces)> Solids = new Dictionary<PrimitiveKind, (Vector3[], int[][])>();

            public static (Vector3[] Points, int[][] Faces) Get(PrimitiveKind kind)
            {
                lock (Solids)
                {
                    if (!Solids.TryGetValue(kind, out var solid))
                    {
                        Solids[kind] = solid = Make(kind);
                    }

                    return solid;
                }
            }

            private static (Vector3[], int[][]) Make(PrimitiveKind kind)
            {
                var phi = (1 + MathF.Sqrt(5)) / 2;
                Vector3[] points;
                switch (kind)
                {
                    case PrimitiveKind.Tetrahedron:
                    {
                        // On a face, with a corner at the back and a face toward +Z: height = edge × √(2/3).
                        var edge = MathF.Sqrt(1.5f);
                        var radius = edge / MathF.Sqrt(3);
                        points = new[] { new Vector3(0, 0, -radius), new Vector3(-edge / 2, 0, radius / 2), new Vector3(edge / 2, 0, radius / 2), new Vector3(0, 1, 0) };
                        return (points, Hull(points));
                    }
                    case PrimitiveKind.Octahedron:
                    {
                        // On a tip, its middle's edges square to the axes.
                        var s = 0.5f / MathF.Sqrt(2);
                        points = new[]
                        {
                            new Vector3(0, 0, 0), new Vector3(0, 1, 0),
                            new Vector3(-s, 0.5f, -s), new Vector3(s, 0.5f, -s), new Vector3(s, 0.5f, s), new Vector3(-s, 0.5f, s),
                        };
                        return (points, Hull(points));
                    }
                    case PrimitiveKind.Dodecahedron:
                    {
                        var list = new List<Vector3>();
                        foreach (var a in new[] { -1f, 1f })
                        {
                            foreach (var b in new[] { -1f, 1f })
                            {
                                list.Add(new Vector3(0, a / phi, b * phi));
                                list.Add(new Vector3(a / phi, b * phi, 0));
                                list.Add(new Vector3(a * phi, 0, b / phi));
                                foreach (var c in new[] { -1f, 1f })
                                {
                                    list.Add(new Vector3(a, b, c));
                                }
                            }
                        }

                        return StandOnFace(list.ToArray());
                    }
                    case PrimitiveKind.Icosahedron:
                    {
                        var list = new List<Vector3>();
                        foreach (var a in new[] { -1f, 1f })
                        {
                            foreach (var b in new[] { -1f, 1f })
                            {
                                list.Add(new Vector3(0, a, b * phi));
                                list.Add(new Vector3(a, b * phi, 0));
                                list.Add(new Vector3(a * phi, 0, b));
                            }
                        }

                        return StandOnFace(list.ToArray());
                    }
                    default:
                        throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a regular solid.");
                }
            }

            // Turns a solid centered on the origin to stand on one of its faces, then sizes it 1 m tall on the pivot.
            private static (Vector3[], int[][]) StandOnFace(Vector3[] points)
            {
                var faces = Hull(points);
                var face = faces[0];
                var normal = Vector3.Normalize(Vector3.Cross(points[face[1]] - points[face[0]], points[face[2]] - points[face[0]]));
                if (Vector3.Dot(normal, points[face[0]]) < 0)
                {
                    normal = -normal; // face away from the center
                }
                var down = -Vector3.UnitY;
                var axis = Vector3.Cross(normal, down);
                var turn = axis.LengthSquared() < 1e-10f
                    ? (Vector3.Dot(normal, down) > 0 ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI))
                    : Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(Math.Clamp(Vector3.Dot(normal, down), -1, 1)));
                var turned = points.Select(p => Vector3.Transform(p, turn)).ToArray();
                float low = turned.Min(p => p.Y), high = turned.Max(p => p.Y);
                var standing = turned.Select(p => new Vector3(p.X, p.Y - low, p.Z) / (high - low)).ToArray();
                return (standing, faces);
            }

            // The faces of a small convex point set, each a list of corners in order: every plane through three
            // corners with all the rest on one side.
            private static int[][] Hull(Vector3[] points)
            {
                const float epsilon = 1e-4f;
                var faces = new List<int[]>();
                var seen = new HashSet<string>();
                for (var i = 0; i < points.Length; i++)
                {
                    for (var j = i + 1; j < points.Length; j++)
                    {
                        for (var k = j + 1; k < points.Length; k++)
                        {
                            var normal = Vector3.Cross(points[j] - points[i], points[k] - points[i]);
                            if (normal.LengthSquared() < 1e-8f)
                            {
                                continue;
                            }

                            normal = Vector3.Normalize(normal);
                            var distances = points.Select(p => Vector3.Dot(p - points[i], normal)).ToArray();
                            var above = distances.Any(d => d > epsilon);
                            var below = distances.Any(d => d < -epsilon);
                            if (above && below)
                            {
                                continue;
                            }

                            var on = Enumerable.Range(0, points.Length).Where(n => MathF.Abs(distances[n]) <= epsilon).ToArray();
                            if (seen.Add(string.Join(",", on)))
                            {
                                faces.Add(on);
                            }
                        }
                    }
                }

                return faces.ToArray();
            }
        }

        // Collects triangles, wound to face outward.
        private sealed class Builder
        {
            public List<Vector3> Triangles { get; } = new List<Vector3>();

            // A flat convex face, its corners in any order, facing away from <paramref name="inside"/>.
            public void Convex(Vector3 inside, params Vector3[] corners)
            {
                var middle = corners.Aggregate(Vector3.Zero, (sum, p) => sum + p) / corners.Length;
                var outward = middle - inside;

                // Order the corners around the middle, in the face's own plane.
                var normal = Vector3.Zero;
                for (var i = 0; i < corners.Length; i++)
                {
                    normal += Vector3.Cross(corners[i] - middle, corners[(i + 1) % corners.Length] - middle);
                }

                if (normal.LengthSquared() < 1e-12f)
                {
                    normal = Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]);
                }

                normal = Vector3.Normalize(normal);
                if (Vector3.Dot(normal, outward) < 0)
                {
                    normal = -normal;
                }

                var u = Vector3.Normalize(corners[0] - middle);
                var v = Vector3.Cross(normal, u);
                var ordered = corners.OrderBy(p => -MathF.Atan2(Vector3.Dot(p - middle, v), Vector3.Dot(p - middle, u))).ToArray();
                for (var i = 1; i + 1 < ordered.Length; i++)
                {
                    Add(ordered[0], ordered[i], ordered[i + 1], normal);
                }
            }

            // A profile in X and Y (a simple polygon, counterclockwise), run through the depth along Z around the pivot.
            public void Prism(Vector2[] profile, float depth)
            {
                float front = -depth / 2, back = depth / 2;
                Vector3 At(Vector2 p, float z) => new Vector3(p.X, p.Y, z);
                foreach (var (a, b, c) in Triangulate(profile))
                {
                    Add(At(a, front), At(b, front), At(c, front), -Vector3.UnitZ);
                    Add(At(a, back), At(b, back), At(c, back), Vector3.UnitZ);
                }

                for (var i = 0; i < profile.Length; i++)
                {
                    var p = profile[i];
                    var q = profile[(i + 1) % profile.Length];
                    var edge = q - p;
                    var outward = new Vector3(edge.Y, -edge.X, 0); // to the right of a counterclockwise edge
                    Add(At(p, front), At(q, front), At(q, back), outward);
                    Add(At(p, front), At(q, back), At(p, back), outward);
                }
            }

            // Adds a triangle, flipped if needed so it faces along <paramref name="outward"/>; drops slivers.
            private void Add(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
            {
                var normal = Vector3.Cross(b - a, c - a);
                if (normal.LengthSquared() < 1e-12f)
                {
                    return;
                }

                if (Vector3.Dot(normal, outward) < 0)
                {
                    (b, c) = (c, b);
                }

                Triangles.Add(a);
                Triangles.Add(b);
                Triangles.Add(c);
            }

            // Ear clipping of a simple counterclockwise polygon.
            private static List<(Vector2, Vector2, Vector2)> Triangulate(Vector2[] polygon)
            {
                var result = new List<(Vector2, Vector2, Vector2)>();
                var remaining = polygon.ToList();
                static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c) =>
                    Cross(a, b, p) >= -1e-7f && Cross(b, c, p) >= -1e-7f && Cross(c, a, p) >= -1e-7f;

                var guard = 0;
                while (remaining.Count > 3 && guard++ < 10000)
                {
                    var clipped = false;
                    for (var i = 0; i < remaining.Count; i++)
                    {
                        var a = remaining[(i + remaining.Count - 1) % remaining.Count];
                        var b = remaining[i];
                        var c = remaining[(i + 1) % remaining.Count];
                        var turn = Cross(a, b, c);
                        if (turn < 0)
                        {
                            continue; // a reflex corner
                        }

                        if (turn > 1e-9f && remaining.Any(p => p != a && p != b && p != c && Inside(p, a, b, c)))
                        {
                            continue;
                        }

                        if (turn > 1e-9f)
                        {
                            result.Add((a, b, c));
                        }

                        remaining.RemoveAt(i);
                        clipped = true;
                        break;
                    }

                    if (!clipped)
                    {
                        break;
                    }
                }

                if (remaining.Count == 3)
                {
                    result.Add((remaining[0], remaining[1], remaining[2]));
                }

                return result;
            }
        }
    }
}
