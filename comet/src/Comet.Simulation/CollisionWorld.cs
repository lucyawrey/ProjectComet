using System;
using System.Collections.Generic;
using System.Numerics;

namespace Comet.Simulation
{
    /// <summary>Safe to query from several threads while one thread flips meshes; bots take <see cref="WithOwnFlips"/> of a shared world.</summary>
    public sealed class CollisionWorld
    {
        private const float CellSize = 4f;
        private const float Epsilon = 0.001f;

        // How far below a clear body IsWedged looks for support; must exceed TryRest's millimeter precision.
        private const float WedgeDepth = 0.01f;

        private const float PushMargin = 0.002f;

        private readonly List<CollisionMesh> _meshes;

        private readonly float _walkableNormalY;

        private readonly bool[] _terrain;
        private readonly Vector3[] _corners;
        private readonly Vector3[] _normals;

        private readonly (int X, int Z)[] _firstCells;

        private readonly int[] _meshOf;
        private readonly Dictionary<(int X, int Z), List<int>> _cells = new Dictionary<(int X, int Z), List<int>>();
        private readonly Dictionary<string, List<int>> _tagged = new Dictionary<string, List<int>>();

        // Replaced whole on a change so queries on other threads see one state or the other.
        private volatile bool[] _off;

        [ThreadStatic]
        private static List<Vector3>? _polygon;

        [ThreadStatic]
        private static List<Vector3>? _scratch;

        [ThreadStatic]
        private static Dictionary<int, int>? _crossings;

        /// <summary>Falling below <paramref name="killHeight"/> meters is out of the world.</summary>
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

        public CollisionWorld WithOwnFlips() => new CollisionWorld(this);

        public ZoneTerrain? Terrain { get; }

        /// <summary>The meshes given, then the terrain's (<see cref="CollisionMesh.TerrainKind"/>).</summary>
        public IReadOnlyList<CollisionMesh> Meshes => _meshes;

        public float KillHeight { get; }

        public IReadOnlyList<int> MeshesTagged(string tag) => _tagged.TryGetValue(tag, out var list) ? list : (IReadOnlyList<int>)Array.Empty<int>();

        public bool IsOn(int mesh) => !_off[mesh];

        public void SetFlipped(string tag, bool flipped)
        {
            var off = (bool[])_off.Clone();
            foreach (var mesh in MeshesTagged(tag))
            {
                off[mesh] = _meshes[mesh].StartsOff != flipped;
            }

            _off = off;
        }

        public bool Overlaps(int mesh, Vector3 feet, float radius, float bodyHeight) =>
            TouchesMesh(feet, radius, bodyHeight, null, only: mesh) || IsInsideMesh(feet + new Vector3(0, bodyHeight / 2, 0), null, only: mesh);

        /// <summary>Terrain may be <paramref name="terrainClimb"/> higher (slopes); radius 0 is the point under feet; false over a hole.</summary>
        public bool TryGetGround(Vector3 feet, float radius, float maxY, out float height, float terrainClimb = 0, ISet<int>? passThrough = null)
        {
            var found = false;
            height = float.MinValue;

            // A hair inside the footprint, so a triangle that only touches its edge doesn't hold it up.
            var r = MathF.Max(0, radius - Epsilon);
            var off = _off;
            int fromX = Cell(feet.X - r),
                fromZ = Cell(feet.Z - r);
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

        /// <summary>The highest ground at or below a position, or null over a hole or past the edge; for dead-reckoning other players.</summary>
        public Func<Vector3, float?> GroundHeight(MovementRules rules) =>
            feet => TryGetGround(feet, rules.BodyRadius, feet.Y, out var height) ? height : null;

        /// <summary>Backs don't count, so a line from inside a mesh sees out.</summary>
        public bool IsLineBlocked(Vector3 from, Vector3 to, ISet<int>? passThrough = null)
        {
            var off = _off;
            var line = to - from;

            int x = Cell(from.X),
                z = Cell(from.Z),
                endX = Cell(to.X),
                endZ = Cell(to.Z);
            int stepX = Math.Sign(line.X),
                stepZ = Math.Sign(line.Z);
            float NextEdge(float start, float delta, int cell, int step) =>
                step == 0 ? float.PositiveInfinity : ((cell + (step > 0 ? 1 : 0)) * CellSize - start) / delta;
            float crossX = NextEdge(from.X, line.X, x, stepX),
                crossZ = NextEdge(from.Z, line.Z, z, stepZ);
            var cellX = stepX == 0 ? float.PositiveInfinity : CellSize / MathF.Abs(line.X);
            var cellZ = stepZ == 0 ? float.PositiveInfinity : CellSize / MathF.Abs(line.Z);
            for (var cells = Math.Abs(endX - x) + Math.Abs(endZ - z); cells >= 0; cells--)
            {
                if (_cells.TryGetValue((x, z), out var list))
                {
                    foreach (var t in list)
                    {
                        if (!Skip(t, off, passThrough) && Vector3.Dot(_normals[t], line) < 0 && CrossesTriangle(t, from, line))
                        {
                            return true;
                        }
                    }
                }

                if (crossX < crossZ)
                {
                    x += stepX;
                    crossX += cellX;
                }
                else
                {
                    z += stepZ;
                    crossZ += cellZ;
                }
            }

            return false;
        }

        private bool CrossesTriangle(int t, Vector3 from, Vector3 line)
        {
            var (a, b, c) = (_corners[3 * t], _corners[3 * t + 1], _corners[3 * t + 2]);
            Vector3 ab = b - a,
                ac = c - a;
            var p = Vector3.Cross(line, ac);
            var determinant = Vector3.Dot(ab, p);
            if (MathF.Abs(determinant) < 1e-9f)
            {
                return false;
            }

            var inverse = 1 / determinant;
            var toFrom = from - a;
            var u = Vector3.Dot(toFrom, p) * inverse;
            if (u < 0 || u > 1)
            {
                return false;
            }

            var q = Vector3.Cross(toFrom, ab);
            var v = Vector3.Dot(line, q) * inverse;
            if (v < 0 || u + v > 1)
            {
                return false;
            }

            var along = Vector3.Dot(ac, q) * inverse;
            return along >= 0 && along <= 1;
        }

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

        public bool IsBlocked(Vector3 feet, float radius, float bodyHeight, ISet<int>? passThrough = null)
        {
            var middle = feet + new Vector3(0, bodyHeight / 2, 0);
            return TouchesMesh(feet, radius, bodyHeight, passThrough) || IsInsideMesh(middle, passThrough) || IsInsideTerrain(middle, 0);
        }

        public bool IsWedged(Vector3 feet, float radius, float bodyHeight, ISet<int>? passThrough = null)
        {
            var lower = feet - new Vector3(0, WedgeDepth, 0);
            return !IsBlocked(feet, radius, bodyHeight, passThrough)
                && IsBlocked(lower, radius, bodyHeight, passThrough)
                && !TryPushOut(ref lower, radius, bodyHeight, passThrough);
        }

        /// <summary>Lowest fit from <paramref name="feet"/> to clear <paramref name="clearY"/>, to a millimeter; false if not clear there.</summary>
        public bool TryRest(ref Vector3 feet, float clearY, float radius, float bodyHeight, ISet<int>? passThrough = null)
        {
            var high = new Vector3(feet.X, clearY, feet.Z);
            if (IsBlocked(high, radius, bodyHeight, passThrough))
            {
                return false;
            }

            var low = feet.Y;
            while (high.Y - low > 0.001f)
            {
                var middle = new Vector3(feet.X, (low + high.Y) / 2, feet.Z);
                if (IsBlocked(middle, radius, bodyHeight, passThrough))
                {
                    low = middle.Y;
                }
                else
                {
                    high = middle;
                }
            }

            feet = high;
            return true;
        }

        /// <summary>Pushes a body out of what it touches, up to its width; false, leaving <paramref name="feet"/>, if it still isn't clear.</summary>
        public bool TryPushOut(ref Vector3 feet, float radius, float bodyHeight, ISet<int>? passThrough = null)
        {
            if (!IsBlocked(feet, radius, bodyHeight, passThrough))
            {
                return true;
            }

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

        private Vector2 DeepestPush(Vector3 feet, float radius, float bodyHeight, ISet<int>? passThrough)
        {
            var off = _off;
            var r = radius - Epsilon;
            float bottom = feet.Y + Epsilon,
                top = feet.Y + bodyHeight - Epsilon;
            var polygon = _polygon ??= new List<Vector3>(8);
            var scratch = _scratch ??= new List<Vector3>(8);
            var center = Flat(feet);
            var deepest = Vector2.Zero;
            int fromX = Cell(feet.X - r),
                fromZ = Cell(feet.Z - r);
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
                        if (
                            !FirstLook(t, x, z, fromX, fromZ)
                            || Skip(t, off, passThrough)
                            || !TriangleTouchesCylinder(t, feet, r, bottom, top, polygon, scratch)
                        )
                        {
                            continue;
                        }

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

        private bool TouchesMesh(Vector3 feet, float radius, float bodyHeight, ISet<int>? passThrough, int only = -1)
        {
            var off = _off;
            var r = radius - Epsilon;
            float bottom = feet.Y + Epsilon,
                top = feet.Y + bodyHeight - Epsilon;
            var polygon = _polygon ??= new List<Vector3>(8);
            var scratch = _scratch ??= new List<Vector3>(8);
            int fromX = Cell(feet.X - r),
                fromZ = Cell(feet.Z - r);
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
                        if (
                            FirstLook(t, x, z, fromX, fromZ)
                            && (only < 0 ? !Skip(t, off, passThrough) : _meshOf[t] == only)
                            && TriangleTouchesCylinder(t, feet, r, bottom, top, polygon, scratch)
                        )
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        // A ray straight up crosses the surface an odd number of times; the point is nudged off the grid so the ray doesn't run along a shared edge.
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
                if (
                    !_terrain[_meshOf[t]]
                    && (only < 0 ? !Skip(t, off, passThrough) : _meshOf[t] == only)
                    && CrossesAbove(point, _corners[3 * t], _corners[3 * t + 1], _corners[3 * t + 2])
                )
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

        private static bool CrossesAbove(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            var d1 = (p.X - b.X) * (a.Z - b.Z) - (a.X - b.X) * (p.Z - b.Z);
            var d2 = (p.X - c.X) * (b.Z - c.Z) - (b.X - c.X) * (p.Z - c.Z);
            var d3 = (p.X - a.X) * (c.Z - a.Z) - (c.X - a.X) * (p.Z - a.Z);
            if ((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0))
            {
                return false;
            }

            var normal = Vector3.Cross(b - a, c - a);
            if (MathF.Abs(normal.Y) < 1e-8f)
            {
                return false;
            }

            var y = a.Y - (normal.X * (p.X - a.X) + normal.Z * (p.Z - a.Z)) / normal.Y;
            return y > p.Y;
        }

        public bool IsInsideTerrain(Vector3 position, float slack)
        {
            if (Terrain == null)
            {
                return false;
            }

            foreach (var piece in Terrain.Pieces)
            {
                if (
                    piece.TryGetHeightAt(position.X, position.Z, out var top)
                    && position.Y < top - slack
                    && position.Y > piece.BottomAt(position.X, position.Z)
                )
                {
                    return true;
                }
            }

            return false;
        }

        private bool Skip(int t, bool[] off, ISet<int>? passThrough) =>
            off[_meshOf[t]] || (passThrough != null && passThrough.Count > 0 && passThrough.Contains(_meshOf[t]));

        private static int Cell(float coordinate) => (int)MathF.Floor(coordinate / CellSize);

        private bool FirstLook(int triangle, int x, int z, int fromX, int fromZ)
        {
            var (firstX, firstZ) = _firstCells[triangle];
            return x == Math.Max(firstX, fromX) && z == Math.Max(firstZ, fromZ);
        }

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

            var uphill = new Vector2(-normal.X, -normal.Z);
            var point = uphill.LengthSquared() > 1e-12f ? Flat(feet) + Vector2.Normalize(uphill) * r : Flat(feet);
            var spot = new Vector3(point.X, 0, point.Y);
            if (Over(spot, a, b, c))
            {
                top = MathF.Max(top, HeightOn(normal, a, point.X, point.Y));
            }

            return top;
        }

        private bool TriangleTouchesCylinder(int t, Vector3 feet, float r, float bottom, float top, List<Vector3> polygon, List<Vector3> scratch)
        {
            if (!ClipToHeights(t, bottom, top, polygon, scratch))
            {
                return false;
            }

            var center = Flat(feet);
            var r2 = r * r;
            var sign = 0;
            var inside = true;
            for (var i = 0; i < polygon.Count; i++)
            {
                var p = Flat(polygon[i]);
                var q = Flat(polygon[(i + 1) % polygon.Count]);
                if (DistanceSquaredToSegment(center, p, q) <= r2)
                {
                    return true;
                }

                if ((q - p).LengthSquared() < 1e-12f)
                {
                    continue;
                }

                var side = Cross(q - p, center - p);
                var s =
                    side > 1e-9f ? 1
                    : side < -1e-9f ? -1
                    : 0;
                if (s == 0 || (sign != 0 && s != sign))
                {
                    inside = false;
                }

                sign = s != 0 ? s : sign;
            }

            return inside;
        }

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

        private static bool Over(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            var d1 = (point.X - b.X) * (a.Z - b.Z) - (a.X - b.X) * (point.Z - b.Z);
            var d2 = (point.X - c.X) * (b.Z - c.Z) - (b.X - c.X) * (point.Z - c.Z);
            var d3 = (point.X - a.X) * (c.Z - a.Z) - (c.X - a.X) * (point.Z - a.Z);
            return !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
        }

        private static float HeightOn(Vector3 normal, Vector3 a, float x, float z) => a.Y - (normal.X * (x - a.X) + normal.Z * (z - a.Z)) / normal.Y;
    }
}
