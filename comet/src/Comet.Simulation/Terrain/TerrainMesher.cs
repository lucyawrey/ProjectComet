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
        /// <param name="groundColor">The color of unpainted ground.</param>
        /// <param name="skirtColor">The color of skirt walls and bottoms.</param>
        public static Dictionary<ChunkCoord, TerrainMesh> Build(ZoneTerrain terrain, uint groundColor, uint skirtColor)
        {
            var meshes = new Dictionary<ChunkCoord, TerrainMesh>();
            foreach (var piece in terrain.Pieces)
            {
                if (piece.Tiles.Count > 0)
                {
                    new PieceMesher(terrain, piece, groundColor, skirtColor, meshes).Build();
                }
            }

            return meshes;
        }

        // Meshes one piece. Triangles are addressed by cell and half: half 0 has corners (x, z), (x, z+1), (x+1, z);
        // half 1 has (x+1, z), (x, z+1), (x+1, z+1).
        private sealed class PieceMesher
        {
            private readonly ZoneTerrain _terrain;
            private readonly ZoneTerrain.Piece _piece;
            private readonly uint _groundColor;
            private readonly uint _skirtColor;
            private readonly Dictionary<ChunkCoord, TerrainMesh> _meshes;
            // The cells looked at: the piece's extent plus a margin of one empty cell on every side.
            private readonly int _minX;
            private readonly int _minZ;
            private readonly int _width;
            private readonly int _depth;
            private readonly bool[] _ground;
            private readonly bool[] _outside;

            public PieceMesher(ZoneTerrain terrain, ZoneTerrain.Piece piece, uint groundColor, uint skirtColor, Dictionary<ChunkCoord, TerrainMesh> meshes)
            {
                _terrain = terrain;
                _piece = piece;
                _groundColor = groundColor;
                _skirtColor = skirtColor;
                _meshes = meshes;
                var (minX, minZ, maxX, maxZ) = piece.Extent;
                _minX = minX - 1;
                _minZ = minZ - 1;
                _width = maxX - minX + 2;
                _depth = maxZ - minZ + 2;
                _ground = new bool[_width * _depth * 2];
                _outside = new bool[_ground.Length];
            }

            public void Build()
            {
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
                        for (var half = 0; half < 2; half++)
                        {
                            if (_ground[Index(cx, cz, half)])
                            {
                                AddTriangle(cx, cz, half);
                            }
                        }
                    }
                }
            }

            private int Index(int cx, int cz, int half) => ((cz - _minZ) * _width + (cx - _minX)) * 2 + half;

            private bool InRange(int cx, int cz) => cx >= _minX && cz >= _minZ && cx < _minX + _width && cz < _minZ + _depth;

            private static ((int X, int Z) A, (int X, int Z) B, (int X, int Z) C) Corners(int cx, int cz, int half) => half == 0
                ? ((cx, cz), (cx, cz + 1), (cx + 1, cz))
                : ((cx + 1, cz), (cx, cz + 1), (cx + 1, cz + 1));

            private bool IsGround(int cx, int cz, int half)
            {
                var (a, b, c) = Corners(cx, cz, half);
                return _piece.TryGetHeight(a.X, a.Z, out _) && _piece.TryGetHeight(b.X, b.Z, out _) && _piece.TryGetHeight(c.X, c.Z, out _);
            }

            // The triangle across each edge of a triangle: edges a-b, b-c and c-a in Corners' order.
            private static (int X, int Z, int Half)[] Neighbors(int cx, int cz, int half) => half == 0
                ? new[] { (cx - 1, cz, 1), (cx, cz, 1), (cx, cz - 1, 1) }
                : new[] { (cx, cz, 0), (cx, cz + 1, 0), (cx + 1, cz, 0) };

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
                    foreach (var (nx, nz, nh) in Neighbors(cx, cz, half))
                    {
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

            private void AddTriangle(int cx, int cz, int half)
            {
                var chunk = new ChunkCoord(FloorDiv(cx, ZoneGrid.ChunkSize), FloorDiv(cz, ZoneGrid.ChunkSize));
                if (!_meshes.TryGetValue(chunk, out var mesh))
                {
                    mesh = new TerrainMesh();
                    _meshes.Add(chunk, mesh);
                }

                var origin = chunk.Origin;
                var (a, b, c) = Corners(cx, cz, half);
                var top = new[] { Top(a) - origin, Top(b) - origin, Top(c) - origin };
                mesh.Add(top[0], top[1], top[2], Color(a), Color(b), Color(c));

                var settings = _piece.Settings;
                if (settings.RimSkirts)
                {
                    // The bottom face, under the triangle and facing down.
                    mesh.Add(Bottom(a) - origin, Bottom(c) - origin, Bottom(b) - origin, _skirtColor, _skirtColor, _skirtColor);
                }

                var corners = new[] { a, b, c };
                var neighbors = Neighbors(cx, cz, half);
                for (var edge = 0; edge < 3; edge++)
                {
                    var (nx, nz, nh) = neighbors[edge];
                    var index = Index(nx, nz, nh);
                    if (_ground[index] || !(_outside[index] ? settings.RimSkirts : settings.HoleSkirts))
                    {
                        continue;
                    }

                    AddWall(mesh, origin, corners[edge], corners[(edge + 1) % 3], corners[(edge + 2) % 3]);
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
                var color = _skirtColor;
                if (Vector3.Dot(new Vector3(normal.X, 0, normal.Z), new Vector3(outward.X, 0, outward.Z)) >= 0)
                {
                    mesh.Add(pTop, qTop, qBottom, color, color, color);
                    mesh.Add(pTop, qBottom, pBottom, color, color, color);
                }
                else
                {
                    mesh.Add(pTop, qBottom, qTop, color, color, color);
                    mesh.Add(pTop, pBottom, qBottom, color, color, color);
                }
            }

            private Vector3 Top((int X, int Z) sample)
            {
                _piece.TryGetHeight(sample.X, sample.Z, out var height);
                return At(sample, height);
            }

            private Vector3 Bottom((int X, int Z) sample) =>
                At(sample, _piece.TryGetUnderside(sample.X, sample.Z, out var under) ? under : _piece.Settings.BottomHeight);

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

            private uint Color((int X, int Z) sample) => _piece.ColorAt(sample.X, sample.Z, _groundColor);

            private static Vector3 At((int X, int Z) sample, float height) =>
                new Vector3(sample.X * ZoneGrid.SampleSpacing, height, sample.Z * ZoneGrid.SampleSpacing);

            private static int FloorDiv(int a, int b) => a >= 0 ? a / b : (a - b + 1) / b;
        }
    }
}
