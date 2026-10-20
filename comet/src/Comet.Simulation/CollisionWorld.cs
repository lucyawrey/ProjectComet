using System;
using System.Collections.Generic;
using System.Numerics;

namespace Comet.Simulation
{
    /// <summary>
    /// What players stand on and bump into: a zone's terrain pieces (heightmaps, possibly stacked), solid triangle
    /// meshes (placed props and volumes), and a kill height below which a player has fallen out of the world.
    /// Players are an upright box: a square footprint <c>radius</c> from the feet's center, <c>bodyHeight</c> tall.
    /// The same on client and server; safe to query from several threads at once (bots share one).
    /// </summary>
    public sealed class CollisionWorld
    {
        /// <summary>Mesh triangles at most this steep (their normal at least this upright) can be stood on: 60°.</summary>
        public const float WalkableNormalY = 0.5f;

        // Triangles go into square cells of the ground plane, so a query looks only at those near it.
        private const float CellSize = 4f;
        private const float Epsilon = 0.001f;

        private readonly List<CollisionMesh> _meshes;
        private readonly Vector3[] _corners;
        private readonly Vector3[] _normals;
        // Each triangle's lowest cell on each axis: a query spanning several cells looks at a triangle only in the
        // first of them it's listed in, so it isn't looked at twice.
        private readonly (int X, int Z)[] _firstCells;
        // Which mesh each triangle belongs to.
        private readonly int[] _meshOf;
        private readonly Dictionary<(int X, int Z), List<int>> _cells = new Dictionary<(int X, int Z), List<int>>();

        // Scratch space for clipping triangles to footprints, per thread.
        [ThreadStatic] private static List<Vector3>? _polygon;
        [ThreadStatic] private static List<Vector3>? _scratch;
        [ThreadStatic] private static Dictionary<int, int>? _crossings;

        /// <summary>A world of <paramref name="terrain"/> (if any) and <paramref name="meshes"/>, where falling below <paramref name="killHeight"/> meters is out of the world.</summary>
        public CollisionWorld(ZoneTerrain? terrain, IEnumerable<CollisionMesh> meshes, float killHeight)
        {
            Terrain = terrain;
            _meshes = new List<CollisionMesh>(meshes);
            KillHeight = killHeight;
            var corners = new List<Vector3>();
            var meshOf = new List<int>();
            for (var m = 0; m < _meshes.Count; m++)
            {
                corners.AddRange(_meshes[m].Triangles);
                meshOf.AddRange(System.Linq.Enumerable.Repeat(m, _meshes[m].Triangles.Length / 3));
            }

            _corners = corners.ToArray();
            _meshOf = meshOf.ToArray();
            _normals = new Vector3[_corners.Length / 3];
            _firstCells = new (int X, int Z)[_normals.Length];
            for (var t = 0; t < _normals.Length; t++)
            {
                var (a, b, c) = (_corners[3 * t], _corners[3 * t + 1], _corners[3 * t + 2]);
                var cross = Vector3.Cross(b - a, c - a);
                _normals[t] = cross.LengthSquared() > 0 ? Vector3.Normalize(cross) : Vector3.Zero;
                var min = Vector3.Min(a, Vector3.Min(b, c));
                var max = Vector3.Max(a, Vector3.Max(b, c));
                _firstCells[t] = (Cell(min.X), Cell(min.Z));
                for (var z = Cell(min.Z); z <= Cell(max.Z); z++)
                {
                    for (var x = Cell(min.X); x <= Cell(max.X); x++)
                    {
                        if (!_cells.TryGetValue((x, z), out var list))
                        {
                            list = new List<int>();
                            _cells.Add((x, z), list);
                        }

                        list.Add(t);
                    }
                }
            }
        }

        /// <summary>The terrain, or null for a world of meshes only.</summary>
        public ZoneTerrain? Terrain { get; }

        public IReadOnlyList<CollisionMesh> Meshes => _meshes;

        /// <summary>Below this height a player has fallen out of the world.</summary>
        public float KillHeight { get; }

        /// <summary>
        /// The highest ground under a body that is at or below <paramref name="maxY"/>: terrain under its center (any
        /// piece, allowed up to <paramref name="terrainClimb"/> higher, for slopes), or the highest point of a walkable
        /// mesh triangle under its footprint. False if there is none (over a hole, past the edge).
        /// </summary>
        public bool TryGetGround(Vector3 feet, float radius, float maxY, out float height, float terrainClimb = 0)
        {
            var found = TryGetTerrainHeight(feet.X, feet.Z, maxY + terrainClimb, out height);
            if (!found)
            {
                height = float.MinValue;
            }

            // A hair inside the footprint, so a triangle that only touches its edge doesn't hold it up.
            var r = radius - Epsilon;
            var polygon = _polygon ??= new List<Vector3>(8);
            var scratch = _scratch ??= new List<Vector3>(8);
            int fromX = Cell(feet.X - r), fromZ = Cell(feet.Z - r);
            for (var z = fromZ; z <= Cell(feet.Z + r); z++)
            {
                for (var x = fromX; x <= Cell(feet.X + r); x++)
                {
                    if (!_cells.TryGetValue((x, z), out var list))
                    {
                        continue;
                    }

                    foreach (var t in list)
                    {
                        if (!FirstLook(t, x, z, fromX, fromZ) || _normals[t].Y < WalkableNormalY)
                        {
                            continue;
                        }

                        var top = HighestInFootprint(t, feet, r, polygon, scratch);
                        if (top <= maxY && top > height)
                        {
                            height = top;
                            found = true;
                        }
                    }
                }
            }

            return found;
        }

        /// <summary>The highest terrain at a point, among the pieces with ground there at or below <paramref name="maxY"/>.</summary>
        public bool TryGetTerrainHeight(float x, float z, float maxY, out float height)
        {
            var found = false;
            height = float.MinValue;
            if (Terrain == null)
            {
                return false;
            }

            foreach (var piece in Terrain.Pieces)
            {
                if (piece.TryGetHeightAt(x, z, out var ground) && ground <= maxY && ground > height)
                {
                    height = ground;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// Whether a body standing at <paramref name="feet"/> overlaps a solid mesh: touches one of its triangles, or is
        /// wholly inside it. Terrain is checked by <see cref="IsBlockedByTerrain"/>.
        /// </summary>
        public bool IsBlocked(Vector3 feet, float radius, float bodyHeight) =>
            TouchesMesh(feet, radius, bodyHeight) || IsInsideMesh(feet + new Vector3(0, bodyHeight / 2, 0));

        private bool TouchesMesh(Vector3 feet, float radius, float bodyHeight)
        {
            var min = new Vector3(feet.X - radius + Epsilon, feet.Y + Epsilon, feet.Z - radius + Epsilon);
            var max = new Vector3(feet.X + radius - Epsilon, feet.Y + bodyHeight - Epsilon, feet.Z + radius - Epsilon);
            int fromX = Cell(min.X), fromZ = Cell(min.Z);
            for (var z = fromZ; z <= Cell(max.Z); z++)
            {
                for (var x = fromX; x <= Cell(max.X); x++)
                {
                    if (!_cells.TryGetValue((x, z), out var list))
                    {
                        continue;
                    }

                    foreach (var t in list)
                    {
                        if (FirstLook(t, x, z, fromX, fromZ) && TriangleOverlapsBox(_corners[3 * t], _corners[3 * t + 1], _corners[3 * t + 2], min, max))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        // Whether a point is inside a closed mesh: a ray straight up from it crosses that mesh's surface an odd number
        // of times. Nudged off the grid a little, so the ray doesn't run exactly along an edge two triangles share.
        private bool IsInsideMesh(Vector3 point)
        {
            point += new Vector3(1.2345e-4f, 0, 2.3456e-4f);
            if (!_cells.TryGetValue((Cell(point.X), Cell(point.Z)), out var list))
            {
                return false;
            }

            var crossings = _crossings ??= new Dictionary<int, int>();
            crossings.Clear();
            foreach (var t in list)
            {
                if (CrossesAbove(point, _corners[3 * t], _corners[3 * t + 1], _corners[3 * t + 2]))
                {
                    crossings.TryGetValue(_meshOf[t], out var count);
                    crossings[_meshOf[t]] = count + 1;
                }
            }

            foreach (var count in crossings.Values)
            {
                if (count % 2 == 1)
                {
                    return true;
                }
            }

            return false;
        }

        // Whether a triangle lies over the point (seen from above) and above it.
        private static bool CrossesAbove(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            var d1 = (p.X - b.X) * (a.Z - b.Z) - (a.X - b.X) * (p.Z - b.Z);
            var d2 = (p.X - c.X) * (b.Z - c.Z) - (b.X - c.X) * (p.Z - c.Z);
            var d3 = (p.X - a.X) * (c.Z - a.Z) - (c.X - a.X) * (p.Z - a.Z);
            if ((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0))
            {
                return false;
            }

            // The triangle's height over the point, from its plane; a vertical triangle (no area seen from above) isn't crossed.
            var normal = Vector3.Cross(b - a, c - a);
            if (MathF.Abs(normal.Y) < 1e-8f)
            {
                return false;
            }

            var y = a.Y - (normal.X * (p.X - a.X) + normal.Z * (p.Z - a.Z)) / normal.Y;
            return y > p.Y;
        }

        /// <summary>
        /// Whether terrain stands in a moving body's way: a piece whose ground under the body rises above the feet by
        /// more than <paramref name="climb"/> (the most a move may climb: steps and slopes), while its bottom is below
        /// the head. Checked at the body's center and at the middles of its footprint's sides, each side allowed a
        /// little more for a slope across the footprint. Catches cliffs, chasm walls and the undersides of pieces above.
        /// </summary>
        public bool IsBlockedByTerrain(Vector3 feet, float radius, float bodyHeight, float climb)
        {
            if (Terrain == null)
            {
                return false;
            }

            var head = feet.Y + bodyHeight;
            foreach (var piece in Terrain.Pieces)
            {
                for (var point = 0; point < 5; point++)
                {
                    var (dx, dz) = point switch { 1 => (radius, 0f), 2 => (-radius, 0f), 3 => (0f, radius), 4 => (0f, -radius), _ => (0f, 0f) };
                    // Slopes the motor may climb rise up to twice the distance across.
                    var allowance = climb + 2 * MathF.Abs(dx + dz);
                    if (piece.TryGetHeightAt(feet.X + dx, feet.Z + dz, out var top) && top > feet.Y + allowance + Epsilon
                        && piece.BottomAt(feet.X + dx, feet.Z + dz) < head - Epsilon)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Whether <paramref name="position"/> is inside a terrain piece: below its ground by more than
        /// <paramref name="slack"/> and above its bottom. For the server's check of reported positions.
        /// </summary>
        public bool IsInsideTerrain(Vector3 position, float slack)
        {
            if (Terrain == null)
            {
                return false;
            }

            foreach (var piece in Terrain.Pieces)
            {
                if (piece.TryGetHeightAt(position.X, position.Z, out var top) && position.Y < top - slack && position.Y > piece.BottomAt(position.X, position.Z))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// How far along a ray a sphere of <paramref name="radius"/> gets before touching a mesh, up to
        /// <paramref name="maxDistance"/> (approximately: where the ray hits, less the radius). Terrain is ignored.
        /// For keeping a camera out of objects.
        /// </summary>
        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, float radius, out float distance)
        {
            direction = Vector3.Normalize(direction);
            distance = maxDistance;
            var hit = false;
            var end = origin + direction * (maxDistance + radius);
            var min = Vector3.Min(origin, end) - new Vector3(radius);
            var max = Vector3.Max(origin, end) + new Vector3(radius);
            int fromX = Cell(min.X), fromZ = Cell(min.Z);
            for (var z = fromZ; z <= Cell(max.Z); z++)
            {
                for (var x = fromX; x <= Cell(max.X); x++)
                {
                    if (!_cells.TryGetValue((x, z), out var list))
                    {
                        continue;
                    }

                    foreach (var t in list)
                    {
                        if (FirstLook(t, x, z, fromX, fromZ) && RayHitsTriangle(origin, direction, _corners[3 * t], _corners[3 * t + 1], _corners[3 * t + 2], out var along))
                        {
                            var reach = MathF.Max(0, along - radius);
                            if (reach < distance)
                            {
                                distance = reach;
                                hit = true;
                            }
                        }
                    }
                }
            }

            return hit;
        }

        private static int Cell(float coordinate) => (int)MathF.Floor(coordinate / CellSize);

        // Whether cell (x, z) is the first cell of a query starting at (fromX, fromZ) that lists the triangle.
        private bool FirstLook(int triangle, int x, int z, int fromX, int fromZ)
        {
            var (firstX, firstZ) = _firstCells[triangle];
            return x == Math.Max(firstX, fromX) && z == Math.Max(firstZ, fromZ);
        }

        // The highest point of a triangle within the square footprint: the triangle clipped to the square, then its
        // highest corner. Minus infinity if they don't overlap.
        private float HighestInFootprint(int t, Vector3 feet, float r, List<Vector3> polygon, List<Vector3> scratch)
        {
            polygon.Clear();
            polygon.Add(_corners[3 * t]);
            polygon.Add(_corners[3 * t + 1]);
            polygon.Add(_corners[3 * t + 2]);
            Clip(polygon, scratch, 0, feet.X - r, keepAbove: true);
            Clip(polygon, scratch, 0, feet.X + r, keepAbove: false);
            Clip(polygon, scratch, 2, feet.Z - r, keepAbove: true);
            Clip(polygon, scratch, 2, feet.Z + r, keepAbove: false);
            var top = float.NegativeInfinity;
            foreach (var p in polygon)
            {
                top = MathF.Max(top, p.Y);
            }

            return top;
        }

        // Clips a convex polygon to one side of an axis-aligned plane (axis 0 is X, 2 is Z).
        private static void Clip(List<Vector3> polygon, List<Vector3> scratch, int axis, float plane, bool keepAbove)
        {
            scratch.Clear();
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                var da = (axis == 0 ? a.X : a.Z) - plane;
                var db = (axis == 0 ? b.X : b.Z) - plane;
                if (!keepAbove)
                {
                    da = -da;
                    db = -db;
                }

                if (da >= 0)
                {
                    scratch.Add(a);
                }

                if ((da >= 0) != (db >= 0))
                {
                    scratch.Add(Vector3.Lerp(a, b, da / (da - db)));
                }
            }

            polygon.Clear();
            polygon.AddRange(scratch);
        }

        // Möller–Trumbore, both faces: where along the ray it crosses the triangle, if ahead of the origin.
        private static bool RayHitsTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float along)
        {
            along = 0;
            var ab = b - a;
            var ac = c - a;
            var p = Vector3.Cross(direction, ac);
            var determinant = Vector3.Dot(ab, p);
            if (MathF.Abs(determinant) < 1e-8f)
            {
                return false;
            }

            var inverse = 1 / determinant;
            var toOrigin = origin - a;
            var u = Vector3.Dot(toOrigin, p) * inverse;
            if (u < 0 || u > 1)
            {
                return false;
            }

            var q = Vector3.Cross(toOrigin, ab);
            var v = Vector3.Dot(direction, q) * inverse;
            if (v < 0 || u + v > 1)
            {
                return false;
            }

            along = Vector3.Dot(ac, q) * inverse;
            return along >= 0;
        }

        // The separating axis test between a triangle and an axis-aligned box (Akenine-Möller): they overlap unless
        // some axis separates them, among the box's three, the triangle's normal and the nine edge cross products.
        private static bool TriangleOverlapsBox(Vector3 a, Vector3 b, Vector3 c, Vector3 boxMin, Vector3 boxMax)
        {
            var center = (boxMin + boxMax) / 2;
            var half = (boxMax - boxMin) / 2;
            var v0 = a - center;
            var v1 = b - center;
            var v2 = c - center;

            // The box's axes.
            if (MathF.Max(v0.X, MathF.Max(v1.X, v2.X)) < -half.X || MathF.Min(v0.X, MathF.Min(v1.X, v2.X)) > half.X
                || MathF.Max(v0.Y, MathF.Max(v1.Y, v2.Y)) < -half.Y || MathF.Min(v0.Y, MathF.Min(v1.Y, v2.Y)) > half.Y
                || MathF.Max(v0.Z, MathF.Max(v1.Z, v2.Z)) < -half.Z || MathF.Min(v0.Z, MathF.Min(v1.Z, v2.Z)) > half.Z)
            {
                return false;
            }

            // The triangle's plane.
            var e0 = v1 - v0;
            var e1 = v2 - v1;
            var e2 = v0 - v2;
            var normal = Vector3.Cross(e0, e1);
            var reach = half.X * MathF.Abs(normal.X) + half.Y * MathF.Abs(normal.Y) + half.Z * MathF.Abs(normal.Z);
            if (MathF.Abs(Vector3.Dot(normal, v0)) > reach)
            {
                return false;
            }

            // The edges crossed with the box's axes.
            return !Separates(e0, v0, v1, v2, half) && !Separates(e1, v0, v1, v2, half) && !Separates(e2, v0, v1, v2, half);
        }

        // Whether an axis made from a triangle edge and one of the box's axes separates them.
        private static bool Separates(Vector3 edge, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 half) =>
            SeparatesOn(Vector3.Cross(edge, Vector3.UnitX), v0, v1, v2, half)
            || SeparatesOn(Vector3.Cross(edge, Vector3.UnitY), v0, v1, v2, half)
            || SeparatesOn(Vector3.Cross(edge, Vector3.UnitZ), v0, v1, v2, half);

        private static bool SeparatesOn(Vector3 axis, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 half)
        {
            if (axis.LengthSquared() < 1e-12f)
            {
                return false;
            }

            var p0 = Vector3.Dot(v0, axis);
            var p1 = Vector3.Dot(v1, axis);
            var p2 = Vector3.Dot(v2, axis);
            var r = half.X * MathF.Abs(axis.X) + half.Y * MathF.Abs(axis.Y) + half.Z * MathF.Abs(axis.Z);
            return MathF.Min(p0, MathF.Min(p1, p2)) > r || MathF.Max(p0, MathF.Max(p1, p2)) < -r;
        }
    }
}
