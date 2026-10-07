using System.Collections.Generic;
using System.Linq;
using Comet.Simulation;
using ShapeLand.Shared.Content;
using UnityEngine;

namespace ShapeLand.Client
{
    /// <summary>
    /// Flat-shaded meshes built in code: the island from the same triangles <see cref="TerrainGround"/> stands
    /// players on, the blocks from the world's boxes, and the three player shapes.
    /// </summary>
    public static class WorldMeshes
    {
        /// <summary>
        /// The island: each grid cell split along <see cref="TerrainGround"/>'s diagonal, a triangle drawn only
        /// where all three corners are ground, so what's drawn is exactly what's stood on.
        /// </summary>
        public static Mesh Island(TerrainGround terrain)
        {
            var map = terrain.Map;
            var spacing = terrain.Spacing;
            var origin = ToUnity(terrain.Origin);
            var vertices = new List<Vector3>();

            void Triangle((int X, int Z) a, (int X, int Z) b, (int X, int Z) c)
            {
                if (!map.TryGetHeight(a.X, a.Z, out var ha) || !map.TryGetHeight(b.X, b.Z, out var hb) || !map.TryGetHeight(c.X, c.Z, out var hc))
                {
                    return;
                }

                vertices.Add(origin + new Vector3(a.X * spacing, ha, a.Z * spacing));
                vertices.Add(origin + new Vector3(b.X * spacing, hb, b.Z * spacing));
                vertices.Add(origin + new Vector3(c.X * spacing, hc, c.Z * spacing));
            }

            for (var z = 0; z < map.SizeZ - 1; z++)
            {
                for (var x = 0; x < map.SizeX - 1; x++)
                {
                    Triangle((x, z), (x, z + 1), (x + 1, z));
                    Triangle((x + 1, z), (x, z + 1), (x + 1, z + 1));
                }
            }

            return Build("Island", vertices);
        }

        /// <summary>A shape's mesh, standing on its origin and filling the player's collision body.</summary>
        public static Mesh Shape(MeshKind kind, MovementRules rules)
        {
            var r = rules.BodyRadius;
            var h = rules.BodyHeight;
            switch (kind)
            {
                case MeshKind.Diamond:
                    var top = new Vector3(0, h, 0);
                    var bottom = Vector3.zero;
                    var ring = new[] { new Vector3(-r, h / 2, -r), new Vector3(r, h / 2, -r), new Vector3(r, h / 2, r), new Vector3(-r, h / 2, r) };
                    return Solid("Diamond", ring.SelectMany((p, i) => new[] { top, p, ring[(i + 1) % 4], bottom, p, ring[(i + 1) % 4] }));
                case MeshKind.Pyramid:
                    var apex = new Vector3(0, h, 0);
                    var corners = new[] { new Vector3(-r, 0, -r), new Vector3(r, 0, -r), new Vector3(r, 0, r), new Vector3(-r, 0, r) };
                    var sides = corners.SelectMany((p, i) => new[] { apex, p, corners[(i + 1) % 4] });
                    return Solid("Pyramid", sides.Concat(new[] { corners[0], corners[1], corners[2], corners[0], corners[2], corners[3] }));
                default:
                    return Cuboid("Cube", new Vector3(0, r, 0), new Vector3(2 * r, 2 * r, 2 * r));
            }
        }

        /// <summary>
        /// Two eyes on the shape's front face (+Z), so its facing shows: small boxes standing slightly out of the
        /// surface and tilted with it.
        /// </summary>
        public static Mesh Eyes(MeshKind kind, MovementRules rules)
        {
            var r = rules.BodyRadius;
            var h = rules.BodyHeight;

            // Each shape's front face on its centre line: a height on it, how far forward it is there, and how far it leans back.
            var (y, z, lean) = kind switch
            {
                MeshKind.Diamond => (0.65f * h, r * 0.7f, Mathf.Atan2(r, h / 2)),
                MeshKind.Pyramid => (0.4f * h, r * 0.6f, Mathf.Atan2(r, h)),
                _ => (1.2f * r, r, 0f),
            };

            var size = new Vector3(0.1f * r / 0.4f, 0.14f * r / 0.4f, 0.06f);
            var rotation = Quaternion.Euler(-lean * Mathf.Rad2Deg, 0, 0);
            var triangles = new List<Vector3>();
            foreach (var side in new[] { -1, 1 })
            {
                var centre = new Vector3(side * 0.3f * r, y, z) + rotation * new Vector3(0, 0, size.z / 4);
                triangles.AddRange(Oriented(BoxTriangles(size).Select(v => centre + rotation * v).ToList(), centre));
            }

            return Build("Eyes", triangles);
        }

        /// <summary>A box mesh around <paramref name="centre"/>.</summary>
        public static Mesh Cuboid(string name, Vector3 centre, Vector3 size) => Solid(name, BoxTriangles(size).Select(v => centre + v), centre);

        /// <summary>A box mesh for one of the world's blocks, in world space.</summary>
        public static Mesh Block(Box box) => Cuboid("Block", ToUnity(box.Centre), ToUnity(box.Size));

        public static Vector3 ToUnity(System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);

        public static System.Numerics.Vector3 ToNumerics(Vector3 v) => new System.Numerics.Vector3(v.x, v.y, v.z);

        // The twelve triangles of a box centred on the origin, in no particular winding.
        private static IEnumerable<Vector3> BoxTriangles(Vector3 size)
        {
            var e = size / 2;
            var c = new Vector3[8];
            for (var i = 0; i < 8; i++)
            {
                c[i] = new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            }

            int[][] faces = { new[] { 0, 1, 3, 2 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 }, new[] { 0, 2, 6, 4 }, new[] { 1, 3, 7, 5 } };
            return faces.SelectMany(f => new[] { c[f[0]], c[f[1]], c[f[2]], c[f[0]], c[f[2]], c[f[3]] });
        }

        // A closed convex solid from loose triangles in any winding.
        private static Mesh Solid(string name, IEnumerable<Vector3> triangles, Vector3? middle = null)
        {
            var vertices = triangles.ToList();
            return Build(name, Oriented(vertices, middle ?? vertices.Aggregate(Vector3.zero, (sum, v) => sum + v) / vertices.Count));
        }

        // Turns each triangle of a convex solid to face away from a point inside it.
        private static List<Vector3> Oriented(List<Vector3> vertices, Vector3 inside)
        {
            for (var i = 0; i < vertices.Count; i += 3)
            {
                var (a, b, c) = (vertices[i], vertices[i + 1], vertices[i + 2]);
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3 - inside) < 0)
                {
                    vertices[i + 1] = c;
                    vertices[i + 2] = b;
                }
            }

            return vertices;
        }

        // Unity's front faces wind clockwise seen from outside: the normal is (b - a) × (c - a).
        private static Mesh Build(string name, List<Vector3> vertices)
        {
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65535)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            mesh.SetVertices(vertices);
            mesh.SetTriangles(Enumerable.Range(0, vertices.Count).ToArray(), 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
