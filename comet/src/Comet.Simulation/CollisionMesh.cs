using System.Collections.Generic;
using System.Numerics;

namespace Comet.Simulation
{
    /// <summary>
    /// Solid triangles in the world: three corners each, in world meters, wound clockwise seen from outside (Unity's convention).
    /// </summary>
    public sealed class CollisionMesh
    {
        public CollisionMesh(Vector3[] triangles) => Triangles = triangles;

        public Vector3[] Triangles { get; }

        public string? Tag { get; set; }

        public bool StartsOff { get; set; }

        public const string TerrainKind = "terrain";

        public string? Kind { get; set; }

        public static CollisionMesh Placed(IReadOnlyList<Vector3> triangles, Matrix4x4 transform)
        {
            var placed = new Vector3[triangles.Count];
            for (var i = 0; i < placed.Length; i++)
            {
                placed[i] = Vector3.Transform(triangles[i], transform);
            }

            // A mirroring scale (negative on one axis) turns triangles inside out; rewind them to face out again.
            if (transform.GetDeterminant() < 0)
            {
                for (var i = 0; i < placed.Length; i += 3)
                {
                    (placed[i + 1], placed[i + 2]) = (placed[i + 2], placed[i + 1]);
                }
            }

            return new CollisionMesh(placed);
        }

        public static CollisionMesh Box(Vector3 size, Matrix4x4 transform) => Placed(BoxTriangles(size), transform);

        public static CollisionMesh Box(Vector3 min, Vector3 max) => Box(max - min, Matrix4x4.CreateTranslation((min + max) / 2));

        private static Vector3[] BoxTriangles(Vector3 size)
        {
            var e = size / 2;
            var c = new Vector3[8];
            for (var i = 0; i < 8; i++)
            {
                c[i] = new Vector3((i & 1) == 0 ? -e.X : e.X, (i & 2) == 0 ? -e.Y : e.Y, (i & 4) == 0 ? -e.Z : e.Z);
            }

            int[][] faces =
            {
                new[] { 0, 2, 3, 1 },
                new[] { 4, 5, 7, 6 },
                new[] { 0, 1, 5, 4 },
                new[] { 2, 6, 7, 3 },
                new[] { 0, 4, 6, 2 },
                new[] { 1, 3, 7, 5 },
            };
            var triangles = new List<Vector3>();
            foreach (var f in faces)
            {
                triangles.AddRange(new[] { c[f[0]], c[f[1]], c[f[2]], c[f[0]], c[f[2]], c[f[3]] });
            }

            return triangles.ToArray();
        }
    }
}
