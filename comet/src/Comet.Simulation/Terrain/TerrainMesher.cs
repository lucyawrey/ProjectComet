using System;
using System.Collections.Generic;
using System.Numerics;
using Comet.Content;

namespace Comet.Simulation
{
    /// <summary>One chunk's terrain mesh: triangles as consecutive corners, each with its color.</summary>
    public sealed class TerrainMesh
    {
        /// <summary>Three corners per triangle, relative to the chunk's origin (<see cref="ChunkCoord.Origin"/>), wound clockwise seen from the front (Unity's convention).</summary>
        public List<Vector3> Positions { get; } = new List<Vector3>();

        /// <summary>Each corner's color as 0xRRGGBB.</summary>
        public List<uint> Colors { get; } = new List<uint>();

        public int TriangleCount => Positions.Count / 3;

        internal void Add(Vector3 a, Vector3 b, Vector3 c, uint colorA, uint colorB, uint colorC)
        {
            Positions.Add(a);
            Positions.Add(b);
            Positions.Add(c);
            Colors.Add(colorA);
            Colors.Add(colorB);
            Colors.Add(colorC);
        }
    }

    /// <summary>
    /// Turns a zone's terrain pieces into flat-shaded, vertex-colored meshes cut per chunk, the same everywhere they're
    /// drawn (the game client and the editor; later, the LOD build). Each grid cell splits along the
    /// diagonal from (x+1, z) to (x, z+1) into two triangles, drawn only where all three corners are ground, so what's
    /// drawn is what's stood on. Wherever a piece's ground ends, skirts close it: walls down from the edge to the
    /// piece's bottom (its underside map, else its flat bottom height, but no lower than other terrain beneath), and,
    /// for pieces with rim skirts, a bottom face under the whole piece. The rim and hole toggles choose which edges get
    /// walls: the piece's outer edge, or the edges of holes inside it.
    /// </summary>
    public static class TerrainMesher
    {
        /// <summary>The meshes of <paramref name="terrain"/>'s pieces, by chunk.</summary>
        /// <param name="groundColor">The color of unpainted ground, for pieces without their own.</param>
        /// <param name="underColor">The color of unpainted undersides (skirt walls and bottoms), for pieces without their own.</param>
        /// <param name="only">
        /// Mesh only these chunks (the editor redrawing where a brush went); null for all. A chunk left with no
        /// terrain has no entry.
        /// </param>
        public static Dictionary<ChunkCoord, TerrainMesh> Build(ZoneTerrain terrain, uint groundColor, uint underColor, ISet<ChunkCoord>? only = null)
        {
            var meshes = new Dictionary<ChunkCoord, TerrainMesh>();
            foreach (var piece in terrain.Pieces)
            {
                if (piece.Tiles.Count > 0)
                {
                    new PieceMesher(terrain, piece, groundColor, underColor, meshes, only).Build();
                }
            }

            return meshes;
        }

        // Meshes one piece. Triangles are addressed by cell and half: half 0 has corners (x, z), (x, z+1), (x+1, z);
        // half 1 has (x+1, z), (x, z+1), (x+1, z+1). The piece's samples are read once into flat arrays, since looking
        // each up in its tile is slow and every sample is used by up to six triangles.
        private sealed class PieceMesher
        {
            private readonly ZoneTerrain _terrain;
            private readonly ZoneTerrain.Piece _piece;
            private readonly uint _groundColor;
            private readonly uint _underColor;
            private readonly Dictionary<ChunkCoord, TerrainMesh> _meshes;
            private readonly ISet<ChunkCoord>? _only;
            // The cells looked at: the piece's extent plus a margin of one empty cell on every side.
            private readonly int _minX;
            private readonly int _minZ;
            private readonly int _width;
            private readonly int _depth;
            private readonly bool[] _ground;
            private readonly bool[] _outside;
            // Per sample over those cells' corners ((_width + 1) by (_depth + 1)): the ground height (NaN where there's
            // none), the bottom (the underside, else the flat bottom), and the ground and underside colors.
            private readonly float[] _top;
            private readonly float[] _bottom;
            private readonly uint[] _color;
            private readonly uint[] _under;

            public PieceMesher(ZoneTerrain terrain, ZoneTerrain.Piece piece, uint groundColor, uint underColor, Dictionary<ChunkCoord, TerrainMesh> meshes, ISet<ChunkCoord>? only)
            {
                _terrain = terrain;
                _piece = piece;
                _groundColor = piece.Settings.GroundColor ?? groundColor;
                _underColor = piece.Settings.UnderColor ?? underColor;
                _meshes = meshes;
                _only = only;
                var (minX, minZ, maxX, maxZ) = piece.Extent;
                _minX = minX - 1;
                _minZ = minZ - 1;
                _width = maxX - minX + 2;
                _depth = maxZ - minZ + 2;
                _ground = new bool[_width * _depth * 2];
                _outside = new bool[_ground.Length];
                var samples = (_width + 1) * (_depth + 1);
                _top = new float[samples];
                _bottom = new float[samples];
                _color = new uint[samples];
                _under = new uint[samples];
            }

            public void Build()
            {
                // Every height, to tell the outside from holes; the rest only where triangles are made (the chunks
                // meshed, and their edge samples).
                var (fromX, fromZ, toX, toZ) = (int.MinValue, int.MinValue, int.MaxValue, int.MaxValue);
                if (_only != null)
                {
                    (fromX, fromZ, toX, toZ) = (int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);
                    foreach (var chunk in _only)
                    {
                        fromX = Math.Min(fromX, chunk.X * ZoneGrid.ChunkSize);
                        fromZ = Math.Min(fromZ, chunk.Z * ZoneGrid.ChunkSize);
                        toX = Math.Max(toX, (chunk.X + 1) * ZoneGrid.ChunkSize);
                        toZ = Math.Max(toZ, (chunk.Z + 1) * ZoneGrid.ChunkSize);
                    }
                }

                var flatBottom = _piece.BottomHeight;
                _piece.ReadHeights(_minX, _minZ, _width + 1, _top);
                for (var z = Math.Max(_minZ, fromZ); z <= Math.Min(_minZ + _depth, toZ); z++)
                {
                    for (var x = Math.Max(_minX, fromX); x <= Math.Min(_minX + _width, toX); x++)
                    {
                        var i = Sample(x, z);
                        if (!float.IsNaN(_top[i]))
                        {
                            _bottom[i] = _piece.TryGetUnderside(x, z, out var under) ? under : flatBottom;
                            _color[i] = _piece.ColorAt(x, z, _groundColor);
                            _under[i] = _piece.UndersideColorAt(x, z, _underColor);
                        }
                    }
                }

                for (var cz = _minZ; cz < _minZ + _depth; cz++)
                {
                    for (var cx = _minX; cx < _minX + _width; cx++)
                    {
                        for (var half = 0; half < 2; half++)
                        {
                            _ground[Index(cx, cz, half)] = IsGround(cx, cz, half);
                        }
                    }
                }

                MarkOutside();
                for (var cz = _minZ; cz < _minZ + _depth; cz++)
                {
                    for (var cx = _minX; cx < _minX + _width; cx++)
                    {
                        var chunk = new ChunkCoord(FloorDiv(cx, ZoneGrid.ChunkSize), FloorDiv(cz, ZoneGrid.ChunkSize));
                        if (_only != null && !_only.Contains(chunk))
                        {
                            continue;
                        }

                        for (var half = 0; half < 2; half++)
                        {
                            if (_ground[Index(cx, cz, half)])
                            {
                                AddTriangle(chunk, cx, cz, half);
                            }
                        }
                    }
                }
            }

            private int Index(int cx, int cz, int half) => ((cz - _minZ) * _width + (cx - _minX)) * 2 + half;

            private int Sample(int x, int z) => (z - _minZ) * (_width + 1) + (x - _minX);

            private int Sample((int X, int Z) sample) => Sample(sample.X, sample.Z);

            private bool InRange(int cx, int cz) => cx >= _minX && cz >= _minZ && cx < _minX + _width && cz < _minZ + _depth;

            private static ((int X, int Z) A, (int X, int Z) B, (int X, int Z) C) Corners(int cx, int cz, int half) => half == 0
                ? ((cx, cz), (cx, cz + 1), (cx + 1, cz))
                : ((cx + 1, cz), (cx, cz + 1), (cx + 1, cz + 1));

            private bool IsGround(int cx, int cz, int half)
            {
                var (a, b, c) = Corners(cx, cz, half);
                return !float.IsNaN(_top[Sample(a)]) && !float.IsNaN(_top[Sample(b)]) && !float.IsNaN(_top[Sample(c)]);
            }

            // The triangle across edge `edge` of a triangle: edges a-b, b-c and c-a in Corners' order.
            private static (int X, int Z, int Half) Neighbor(int cx, int cz, int half, int edge) => half == 0
                ? edge == 0 ? (cx - 1, cz, 1) : edge == 1 ? (cx, cz, 1) : (cx, cz - 1, 1)
                : edge == 0 ? (cx, cz, 0) : edge == 1 ? (cx, cz + 1, 0) : (cx + 1, cz, 0);

            // Flood-fills the empty triangles reachable from the margin: they're past the piece's outer edge, and the
            // empty triangles left over are holes.
            private void MarkOutside()
            {
                var queue = new Queue<(int X, int Z, int Half)>();
                for (var cz = _minZ; cz < _minZ + _depth; cz++)
                {
                    for (var cx = _minX; cx < _minX + _width; cx++)
                    {
                        if (cx == _minX || cz == _minZ || cx == _minX + _width - 1 || cz == _minZ + _depth - 1)
                        {
                            for (var half = 0; half < 2; half++)
                            {
                                Visit(cx, cz, half, queue);
                            }
                        }
                    }
                }

                while (queue.Count > 0)
                {
                    var (cx, cz, half) = queue.Dequeue();
                    for (var edge = 0; edge < 3; edge++)
                    {
                        var (nx, nz, nh) = Neighbor(cx, cz, half, edge);
                        Visit(nx, nz, nh, queue);
                    }
                }
            }

            private void Visit(int cx, int cz, int half, Queue<(int, int, int)> queue)
            {
                if (!InRange(cx, cz))
                {
                    return;
                }

                var index = Index(cx, cz, half);
                if (!_ground[index] && !_outside[index])
                {
                    _outside[index] = true;
                    queue.Enqueue((cx, cz, half));
                }
            }

            private void AddTriangle(ChunkCoord chunk, int cx, int cz, int half)
            {
                if (!_meshes.TryGetValue(chunk, out var mesh))
                {
                    mesh = new TerrainMesh();
                    _meshes.Add(chunk, mesh);
                }

                var origin = chunk.Origin;
                var (a, b, c) = Corners(cx, cz, half);
                mesh.Add(Top(a) - origin, Top(b) - origin, Top(c) - origin, _color[Sample(a)], _color[Sample(b)], _color[Sample(c)]);

                var settings = _piece.Settings;
                if (settings.RimSkirts)
                {
                    // The bottom face, under the triangle and facing down.
                    mesh.Add(Bottom(a) - origin, Bottom(c) - origin, Bottom(b) - origin, _under[Sample(a)], _under[Sample(c)], _under[Sample(b)]);
                }

                for (var edge = 0; edge < 3; edge++)
                {
                    var (nx, nz, nh) = Neighbor(cx, cz, half, edge);
                    var index = Index(nx, nz, nh);
                    if (_ground[index] || !(_outside[index] ? settings.RimSkirts : settings.HoleSkirts))
                    {
                        continue;
                    }

                    // Edge p-q, with the triangle's third corner inside.
                    var (p, q, inner) = edge == 0 ? (a, b, c) : edge == 1 ? (b, c, a) : (c, a, b);
                    AddWall(mesh, origin, p, q, inner);
                }
            }

            // A wall down from edge p-q, facing away from the triangle's third corner.
            private void AddWall(TerrainMesh mesh, Vector3 origin, (int X, int Z) p, (int X, int Z) q, (int X, int Z) inner)
            {
                Vector3 pTop = Top(p), qTop = Top(q);
                Vector3 pBottom = WallBottom(p, pTop.Y), qBottom = WallBottom(q, qTop.Y);
                if (pBottom.Y >= pTop.Y && qBottom.Y >= qTop.Y)
                {
                    return;
                }

                var outward = (pTop + qTop) / 2 - Top(inner);
                var normal = Vector3.Cross(qTop - pTop, qBottom - pTop);
                pTop -= origin;
                qTop -= origin;
                pBottom -= origin;
                qBottom -= origin;
                uint pColor = _under[Sample(p)], qColor = _under[Sample(q)];
                if (Vector3.Dot(new Vector3(normal.X, 0, normal.Z), new Vector3(outward.X, 0, outward.Z)) >= 0)
                {
                    mesh.Add(pTop, qTop, qBottom, pColor, qColor, qColor);
                    mesh.Add(pTop, qBottom, pBottom, pColor, qColor, pColor);
                }
                else
                {
                    mesh.Add(pTop, qBottom, qTop, pColor, qColor, qColor);
                    mesh.Add(pTop, pBottom, qBottom, pColor, pColor, qColor);
                }
            }

            private Vector3 Top((int X, int Z) sample) => At(sample, _top[Sample(sample)]);

            private Vector3 Bottom((int X, int Z) sample) => At(sample, _bottom[Sample(sample)]);

            // A wall's foot: the piece's bottom, unless other terrain lower than the edge but higher than that is in
            // the way, where the wall stops on it.
            private Vector3 WallBottom((int X, int Z) sample, float top)
            {
                var bottom = Bottom(sample);
                foreach (var other in _terrain.Pieces)
                {
                    if (other != _piece && other.TryGetHeight(sample.X, sample.Z, out var ground) && ground < top && ground > bottom.Y)
                    {
                        bottom.Y = ground;
                    }
                }

                return bottom;
            }

            private static Vector3 At((int X, int Z) sample, float height) =>
                new Vector3(sample.X * ZoneGrid.SampleSpacing, height, sample.Z * ZoneGrid.SampleSpacing);

            private static int FloorDiv(int a, int b) => a >= 0 ? a / b : (a - b + 1) / b;
        }
    }
}
