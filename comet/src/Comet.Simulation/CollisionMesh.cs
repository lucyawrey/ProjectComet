using System.Collections.Generic;
using System.Numerics;

namespace Comet.Simulation
{
    /// <summary>
    /// Solid triangles placed in the world: a prop's collision mesh at its placement, or a hand-placed volume. Three
    /// corners per triangle in world meters, wound clockwise seen from outside (Unity's convention).
    /// </summary>
    public sealed class CollisionMesh
    {
        public CollisionMesh(Vector3[] triangles) => Triangles = triangles;

        public Vector3[] Triangles { get; }

        /// <summary>The tag of the placement it belongs to, for flipping it (<see cref="CollisionWorld.SetFlipped"/>), or null.</summary>
        public string? Tag { get; set; }

        /// <summary>Whether it starts off: not solid until it's turned on.</summary>
        public bool StartsOff { get; set; }

        /// <summary>The <see cref="Kind"/> of the meshes <see cref="CollisionWorld"/> makes from its terrain.</summary>
        public const string TerrainKind = "terrain";

        /// <summary>What made it (a prop, a primitive, a wall…), named by the game, for debug views; or null.</summary>
        public string? Kind { get; set; }

        /// <summary>A prop's collision triangles (in its own space) placed by <paramref name="transform"/>.</summary>
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

        /// <summary>A box of <paramref name="size"/> centered on the origin, placed by <paramref name="transform"/>.</summary>
        public static CollisionMesh Box(Vector3 size, Matrix4x4 transform) => Placed(BoxTriangles(size), transform);

        /// <summary>An axis-aligned box from <paramref name="min"/> to <paramref name="max"/>.</summary>
        public static CollisionMesh Box(Vector3 min, Vector3 max) => Box(max - min, Matrix4x4.CreateTranslation((min + max) / 2));

        // The twelve triangles of a box centered on the origin, wound so the faces point outward.
        private static Vector3[] BoxTriangles(Vector3 size)
        {
            var e = size / 2;
            var c = new Vector3[8];
            for (var i = 0; i < 8; i++)
            {
                c[i] = new Vector3((i & 1) == 0 ? -e.X : e.X, (i & 2) == 0 ? -e.Y : e.Y, (i & 4) == 0 ? -e.Z : e.Z);
            }

            int[][] faces = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
            var triangles = new List<Vector3>();
            foreach (var f in faces)
            {
                triangles.AddRange(new[] { c[f[0]], c[f[1]], c[f[2]], c[f[0]], c[f[2]], c[f[3]] });
            }

            return triangles.ToArray();
        }
    }
}
