using System;
using System.Collections.Generic;
using System.Numerics;

namespace Comet.Simulation
{
    /// <summary>
    /// What players stand on and bump into: a zone's terrain pieces (heightmaps, possibly stacked), solid triangle
    /// meshes (placed props and volumes), and a kill height below which a player has fallen out of the world. Terrain
    /// collides as its triangles (<see cref="TerrainMesher"/>'s, skirts included), added as meshes of their own after
    /// the given ones, so one set of checks covers everything.
    /// Players are upright cylinders: <c>radius</c> round the feet's center, <c>bodyHeight</c> tall, with a flat base.
    /// Tagged meshes can be flipped off and on (<see cref="SetFlipped"/>), and queries can pass through a player's own set of
    /// meshes (<see cref="PassThrough"/>). The same on client and server; safe to query from several threads at once,
    /// with turning meshes on and off from one thread (bots each take <see cref="WithOwnFlips"/> of a shared world).
    /// </summary>
    public sealed class CollisionWorld
    {
        // Triangles go into square cells of the ground plane, so a query looks only at those near it.
        private const float CellSize = 4f;
        private const float Epsilon = 0.001f;
        // How far past touching a push-out goes, so the body ends clear rather than exactly against the surface.
        private const float PushMargin = 0.002f;

        private readonly List<CollisionMesh> _meshes;
        // Triangles at most the steepest walkable slope (their normal at least this upright) can be stood on.
        private readonly float _walkableNormalY;
        // Which meshes are terrain: not closed, so never "inside", and not obstacles for the camera.
        private readonly bool[] _terrain;
        private readonly Vector3[] _corners;
        private readonly Vector3[] _normals;
        // Each triangle's lowest cell on each axis: a query spanning several cells looks at a triangle only in the
        // first of them it's listed in, so it isn't looked at twice.
        private readonly (int X, int Z)[] _firstCells;
        // Which mesh each triangle belongs to.
        private readonly int[] _meshOf;
        private readonly Dictionary<(int X, int Z), List<int>> _cells = new Dictionary<(int X, int Z), List<int>>();
        private readonly Dictionary<string, List<int>> _tagged = new Dictionary<string, List<int>>();
        // Which meshes are off, replaced whole on a change so queries on other threads see one state or the other.
        private volatile bool[] _off;

        // Scratch space for clipping triangles to footprints, per thread.
        [ThreadStatic] private static List<Vector3>? _polygon;
        [ThreadStatic] private static List<Vector3>? _scratch;
        [ThreadStatic] private static Dictionary<int, int>? _crossings;

        /// <summary>A world of <paramref name="terrain"/> (if any) and <paramref name="meshes"/>, where falling below <paramref name="killHeight"/> meters is out of the world.</summary>
        public CollisionWorld(ZoneTerrain? terrain, IEnumerable<CollisionMesh> meshes, float killHeight, float maxSlope = 60)
        {
            Terrain = terrain;
            _walkableNormalY = MathF.Cos(maxSlope * MathF.PI / 180);
            _meshes = new List<CollisionMesh>(meshes);
            if (terrain != null)
            {
                foreach (var chunk in TerrainMesher.Build(terrain, 0, 0))
                {
                    var origin = chunk.Key.Origin;
                    var triangles = new Vector3[chunk.Value.Positions.Count];
                    for (var i = 0; i < triangles.Length; i++)
                    {
                        triangles[i] = chunk.Value.Positions[i] + origin;
                    }

                    _meshes.Add(new CollisionMesh(triangles) { Kind = CollisionMesh.TerrainKind });
                }
            }

            _terrain = new bool[_meshes.Count];
            for (var m = 0; m < _meshes.Count; m++)
            {
                _terrain[m] = _meshes[m].Kind == CollisionMesh.TerrainKind;
            }

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
            _off = new bool[_meshes.Count];
            for (var m = 0; m < _meshes.Count; m++)
            {
                _off[m] = _meshes[m].StartsOff;
                if (_meshes[m].Tag is { } tag)
                {
                    if (!_tagged.TryGetValue(tag, out var list))
                    {
                        list = new List<int>();
                        _tagged.Add(tag, list);
                    }

                    list.Add(m);
                }
            }

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

        // A copy sharing everything but which meshes are off, which starts as the meshes do.
        private CollisionWorld(CollisionWorld shared)
        {
            Terrain = shared.Terrain;
            KillHeight = shared.KillHeight;
            _meshes = shared._meshes;
            _walkableNormalY = shared._walkableNormalY;
            _terrain = shared._terrain;
            _corners = shared._corners;
            _normals = shared._normals;
            _firstCells = shared._firstCells;
            _meshOf = shared._meshOf;
            _cells = shared._cells;
            _tagged = shared._tagged;
            _off = new bool[_meshes.Count];
            for (var m = 0; m < _meshes.Count; m++)
            {
                _off[m] = _meshes[m].StartsOff;
            }
        }

        /// <summary>
        /// This world with its own flips, starting as its meshes do: for one player's view of a world many share (a
        /// bot's), so their flips don't race. Shares the terrain and meshes, so it costs little.
        /// </summary>
        public CollisionWorld WithOwnFlips() => new CollisionWorld(this);

        /// <summary>The terrain, or null for a world of meshes only.</summary>
        public ZoneTerrain? Terrain { get; }

        /// <summary>The meshes given, then the terrain's (one per chunk, <see cref="CollisionMesh.TerrainKind"/>).</summary>
        public IReadOnlyList<CollisionMesh> Meshes => _meshes;

        /// <summary>Below this height a player has fallen out of the world.</summary>
        public float KillHeight { get; }

        /// <summary>The meshes tagged <paramref name="tag"/>, by index in <see cref="Meshes"/>; none for an unknown tag.</summary>
        public IReadOnlyList<int> MeshesTagged(string tag) => _tagged.TryGetValue(tag, out var list) ? list : (IReadOnlyList<int>)Array.Empty<int>();

        /// <summary>Whether mesh <paramref name="mesh"/> is solid now.</summary>
        public bool IsOn(int mesh) => !_off[mesh];

        /// <summary>
        /// Flips the meshes tagged <paramref name="tag"/> from how they start (<paramref name="flipped"/>), or back:
        /// each one that starts on turns off, and each one that starts off turns on.
        /// </summary>
        public void SetFlipped(string tag, bool flipped)
        {
            var off = (bool[])_off.Clone();
            foreach (var mesh in MeshesTagged(tag))
            {
                off[mesh] = _meshes[mesh].StartsOff != flipped;
            }

            _off = off;
        }

        /// <summary>Whether a body standing at <paramref name="feet"/> overlaps mesh <paramref name="mesh"/>, on or off: touches a triangle of it, or is wholly inside it.</summary>
        public bool Overlaps(int mesh, Vector3 feet, float radius, float bodyHeight) =>
            TouchesMesh(feet, radius, bodyHeight, null, only: mesh) || IsInsideMesh(feet + new Vector3(0, bodyHeight / 2, 0), null, only: mesh);

        /// <summary>
        /// The highest ground under a body that is at or below <paramref name="maxY"/>: the highest point of a walkable
        /// triangle within its footprint (terrain allowed up to <paramref name="terrainClimb"/> higher, for slopes),
        /// skipping meshes that are off or in <paramref name="passThrough"/>. A radius of 0 asks about the point under
        /// the feet. False if there is none (over a hole, past the edge).
        /// </summary>
        public bool TryGetGround(Vector3 feet, float radius, float maxY, out float height, float terrainClimb = 0, ISet<int>? passThrough = null)
        {
            var found = false;
            height = float.MinValue;

            // A hair inside the footprint, so a triangle that only touches its edge doesn't hold it up.
            var r = MathF.Max(0, radius - Epsilon);
            var off = _off;
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
                        if (!FirstLook(t, x, z, fromX, fromZ) || _normals[t].Y < _walkableNormalY || Skip(t, off, passThrough))
                        {
                            continue;
                        }

                        var top = HighestInCircle(t, feet, r);
                        if (top <= maxY + (_terrain[_meshOf[t]] ? terrainClimb : 0) && top > height)
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
        /// Whether a body standing at <paramref name="feet"/> overlaps something solid: touches a triangle of a mesh or
        /// the terrain, or is wholly inside a mesh or a terrain piece, skipping meshes that are off or in
        /// <paramref name="passThrough"/>.
        /// </summary>
        public bool IsBlocked(Vector3 feet, float radius, float bodyHeight, ISet<int>? passThrough = null)
        {
            var middle = feet + new Vector3(0, bodyHeight / 2, 0);
            return TouchesMesh(feet, radius, bodyHeight, passThrough) || IsInsideMesh(middle, passThrough) || IsInsideTerrain(middle, 0);
        }

        /// <summary>
        /// Moves a body that touches something sideways out of it, the deepest overlap first, a few times over: for
        /// bodies that end up against a steep surface (landing beside a cone or a terrain slope), which no sideways
        /// move would otherwise get out of. Only sideways, and at most a body's width in all; false, with
        /// <paramref name="feet"/> unchanged, if the body isn't clear after that.
        /// </summary>
        public bool TryPushOut(ref Vector3 feet, float radius, float bodyHeight, ISet<int>? passThrough = null)
        {
            var moved = feet;
            for (var i = 0; i < 4 && IsBlocked(moved, radius, bodyHeight, passThrough); i++)
            {
                var push = DeepestPush(moved, radius, bodyHeight, passThrough);
                if (push == Vector2.Zero)
                {
                    return false;
                }

                moved += new Vector3(push.X, 0, push.Y);
            }

            if (IsBlocked(moved, radius, bodyHeight, passThrough) || Flat(moved - feet).Length() > 2 * radius)
            {
                return false;
            }

            feet = moved;
            return true;
        }

        // The longest of the sideways moves that would each take the body just clear of one triangle it touches.
        private Vector2 DeepestPush(Vector3 feet, float radius, float bodyHeight, ISet<int>? passThrough)
        {
            var off = _off;
            var r = radius - Epsilon;
            float bottom = feet.Y + Epsilon, top = feet.Y + bodyHeight - Epsilon;
            var polygon = _polygon ??= new List<Vector3>(8);
            var scratch = _scratch ??= new List<Vector3>(8);
            var center = Flat(feet);
            var deepest = Vector2.Zero;
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
                        if (!FirstLook(t, x, z, fromX, fromZ) || Skip(t, off, passThrough) || !TriangleTouchesCylinder(t, feet, r, bottom, top, polygon, scratch))
                        {
                            continue;
                        }

                        // Away from the triangle's surface, seen from above; level ones (floors, ceilings) can't be
                        // left sideways. Far enough that every point of the cut triangle is a radius behind the body.
                        var away = Flat(_normals[t]);
                        if (away.LengthSquared() < 0.01f)
                        {
                            continue;
                        }

                        away = Vector2.Normalize(away);
                        var reach = float.NegativeInfinity;
                        foreach (var p in polygon)
                        {
                            reach = MathF.Max(reach, Vector2.Dot(Flat(p) - center, away));
                        }

                        var push = away * (reach + radius + PushMargin);
                        if (push.LengthSquared() > deepest.LengthSquared())
                        {
                            deepest = push;
                        }
                    }
                }
            }

            return deepest;
        }

        // Whether the body touches a mesh's triangle: any solid one not passed through, or only mesh only (on or off).
        // A hair smaller than the body, so resting on a surface or against a wall isn't touching it.
        private bool TouchesMesh(Vector3 feet, float radius, float bodyHeight, ISet<int>? passThrough, int only = -1)
        {
            var off = _off;
            var r = radius - Epsilon;
            float bottom = feet.Y + Epsilon, top = feet.Y + bodyHeight - Epsilon;
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
                        if (FirstLook(t, x, z, fromX, fromZ) && (only < 0 ? !Skip(t, off, passThrough) : _meshOf[t] == only)
                            && TriangleTouchesCylinder(t, feet, r, bottom, top, polygon, scratch))
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
        private bool IsInsideMesh(Vector3 point, ISet<int>? passThrough, int only = -1)
        {
            var off = _off;
            point += new Vector3(1.2345e-4f, 0, 2.3456e-4f);
            if (!_cells.TryGetValue((Cell(point.X), Cell(point.Z)), out var list))
            {
                return false;
            }

            var crossings = _crossings ??= new Dictionary<int, int>();
            crossings.Clear();
            foreach (var t in list)
            {
                if (!_terrain[_meshOf[t]] && (only < 0 ? !Skip(t, off, passThrough) : _meshOf[t] == only) && CrossesAbove(point, _corners[3 * t], _corners[3 * t + 1], _corners[3 * t + 2]))
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
        /// <paramref name="maxDistance"/> (approximately: where the ray hits, less the radius). Terrain and meshes that
        /// are off are ignored. For keeping a camera out of objects.
        /// </summary>
        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, float radius, out float distance)
        {
            direction = Vector3.Normalize(direction);
            distance = maxDistance;
            var hit = false;
            var off = _off;
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
                        if (FirstLook(t, x, z, fromX, fromZ) && !_terrain[_meshOf[t]] && !Skip(t, off, null) && RayHitsTriangle(origin, direction, _corners[3 * t], _corners[3 * t + 1], _corners[3 * t + 2], out var along))
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

        // Whether a query skips triangle t: its mesh is off, or the player passes through it.
        private bool Skip(int t, bool[] off, ISet<int>? passThrough) => off[_meshOf[t]] || (passThrough != null && passThrough.Count > 0 && passThrough.Contains(_meshOf[t]));

        private static int Cell(float coordinate) => (int)MathF.Floor(coordinate / CellSize);

        // Whether cell (x, z) is the first cell of a query starting at (fromX, fromZ) that lists the triangle.
        private bool FirstLook(int triangle, int x, int z, int fromX, int fromZ)
        {
            var (firstX, firstZ) = _firstCells[triangle];
            return x == Math.Max(firstX, fromX) && z == Math.Max(firstZ, fromZ);
        }

        // The highest point of triangle t within the circle of radius r round the feet, seen from above (the point
        // under the feet for r = 0); minus infinity if they don't meet. Height is linear over a triangle, so its highest
        // point in the overlap lies on the overlap's edge: a corner inside the circle, where an edge enters or leaves the
        // circle, or the circle's uphill point if it's over the triangle. Only for walkable triangles (not vertical).
        private float HighestInCircle(int t, Vector3 feet, float r)
        {
            var a = _corners[3 * t];
            var b = _corners[3 * t + 1];
            var c = _corners[3 * t + 2];
            var normal = _normals[t];
            var top = float.NegativeInfinity;
            if (r <= 0)
            {
                return Over(feet, a, b, c) ? HeightOn(normal, a, feet.X, feet.Z) : top;
            }

            var r2 = r * r;
            for (var k = 0; k < 3; k++)
            {
                var p = _corners[3 * t + k];
                var q = _corners[3 * t + (k + 1) % 3];
                if (Flat(p - feet).LengthSquared() <= r2)
                {
                    top = MathF.Max(top, p.Y);
                }

                // Where the edge p + s(q - p) crosses the circle: |f + s d|² = r² seen from above.
                var d = Flat(q - p);
                var f = Flat(p - feet);
                var dd = d.LengthSquared();
                if (dd < 1e-12f)
                {
                    continue;
                }

                var half = Vector2.Dot(f, d) / dd;
                var root = half * half - (f.LengthSquared() - r2) / dd;
                if (root < 0)
                {
                    continue;
                }

                root = MathF.Sqrt(root);
                var enter = MathF.Max(0, -half - root);
                var leave = MathF.Min(1, -half + root);
                if (enter <= leave)
                {
                    top = MathF.Max(top, MathF.Max(p.Y + enter * (q.Y - p.Y), p.Y + leave * (q.Y - p.Y)));
                }
            }

            // Uphill on the plane, seen from above; level ground has none, and the feet's own point stands in for it.
            var uphill = new Vector2(-normal.X, -normal.Z);
            var point = uphill.LengthSquared() > 1e-12f ? Flat(feet) + Vector2.Normalize(uphill) * r : Flat(feet);
            var spot = new Vector3(point.X, 0, point.Y);
            if (Over(spot, a, b, c))
            {
                top = MathF.Max(top, HeightOn(normal, a, point.X, point.Y));
            }

            return top;
        }

        // Whether triangle t, between heights bottom and top, comes within r of the vertical line through the feet:
        // the triangle clipped to that height range, then the distance to its outline seen from above.
        private bool TriangleTouchesCylinder(int t, Vector3 feet, float r, float bottom, float top, List<Vector3> polygon, List<Vector3> scratch)
        {
            if (!ClipToHeights(t, bottom, top, polygon, scratch))
            {
                return false;
            }

            var center = Flat(feet);
            var r2 = r * r;
            int sign = 0;
            var inside = true;
            for (var i = 0; i < polygon.Count; i++)
            {
                var p = Flat(polygon[i]);
                var q = Flat(polygon[(i + 1) % polygon.Count]);
                if (DistanceSquaredToSegment(center, p, q) <= r2)
                {
                    return true;
                }

                // The center is inside the outline if it's on the same side of every edge; an outline with no area
                // seen from above (a wall) has no inside.
                if ((q - p).LengthSquared() < 1e-12f)
                {
                    continue; // a corner repeated by the clipping
                }

                var side = Cross(q - p, center - p);
                var s = side > 1e-9f ? 1 : side < -1e-9f ? -1 : 0;
                if (s == 0 || (sign != 0 && s != sign))
                {
                    inside = false;
                }

                sign = s != 0 ? s : sign;
            }

            return inside;
        }

        // Triangle t cut to the heights from bottom to top, into polygon; false if nothing is left.
        private bool ClipToHeights(int t, float bottom, float top, List<Vector3> polygon, List<Vector3> scratch)
        {
            var a = _corners[3 * t];
            var b = _corners[3 * t + 1];
            var c = _corners[3 * t + 2];
            if (MathF.Max(a.Y, MathF.Max(b.Y, c.Y)) < bottom || MathF.Min(a.Y, MathF.Min(b.Y, c.Y)) > top)
            {
                return false;
            }

            polygon.Clear();
            polygon.Add(a);
            polygon.Add(b);
            polygon.Add(c);
            ClipY(polygon, scratch, bottom, keepAbove: true);
            ClipY(polygon, scratch, top, keepAbove: false);
            return polygon.Count > 0;
        }

        // Clips a convex polygon to one side of a level plane.
        private static void ClipY(List<Vector3> polygon, List<Vector3> scratch, float plane, bool keepAbove)
        {
            scratch.Clear();
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                var da = keepAbove ? a.Y - plane : plane - a.Y;
                var db = keepAbove ? b.Y - plane : plane - b.Y;
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

        private static Vector2 Flat(Vector3 v) => new Vector2(v.X, v.Z);

        private static float Cross(Vector2 u, Vector2 v) => u.X * v.Y - u.Y * v.X;

        private static float DistanceSquaredToSegment(Vector2 point, Vector2 p, Vector2 q)
        {
            var d = q - p;
            var dd = d.LengthSquared();
            var s = dd < 1e-12f ? 0 : Math.Clamp(Vector2.Dot(point - p, d) / dd, 0, 1);
            return Vector2.DistanceSquared(point, p + s * d);
        }

        // Whether a triangle lies over the point, seen from above (edges included).
        private static bool Over(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            var d1 = (point.X - b.X) * (a.Z - b.Z) - (a.X - b.X) * (point.Z - b.Z);
            var d2 = (point.X - c.X) * (b.Z - c.Z) - (b.X - c.X) * (point.Z - c.Z);
            var d3 = (point.X - a.X) * (c.Z - a.Z) - (c.X - a.X) * (point.Z - a.Z);
            return !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
        }

        // The height at (x, z) of the plane through a with this normal (not vertical).
        private static float HeightOn(Vector3 normal, Vector3 a, float x, float z) => a.Y - (normal.X * (x - a.X) + normal.Z * (z - a.Z)) / normal.Y;

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
    }
}
