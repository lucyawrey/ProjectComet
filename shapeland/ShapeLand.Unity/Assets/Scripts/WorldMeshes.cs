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

        /// <summary>
        /// How each shape is drawn at scale 1, in meters. Each shape's <c>scale</c> in content multiplies all of it.
        /// Looks only: every shape collides as the one shared body in <c>MovementRules</c> (0.4 m radius, 1 m tall),
        /// so a shape drawn much bigger or smaller than it may poke into blocks or stop short of them.
        /// </summary>
        public static readonly IReadOnlyDictionary<MeshKind, ShapeLook> Looks = new Dictionary<MeshKind, ShapeLook>
        {
            [MeshKind.Cube] = new ShapeLook
            {
                Width = 0.7f, Depth = 0.7f, Height = 0.7f, Hover = 0.10f,
                EyeWidth = 0.1f, EyeHeight = 0.14f, EyeSpacing = 0.24f, EyeLevel = 0.48f,
            },
            [MeshKind.Diamond] = new ShapeLook
            {
                Width = 0.8f, Depth = 0.8f, Height = 1.0f, Hover = 0.0f, Waist = 0.5f,
                EyeWidth = 0.1f, EyeHeight = 0.14f, EyeSpacing = 0.24f, EyeLevel = 0.65f,
            },
            [MeshKind.Pyramid] = new ShapeLook
            {
                Width = 0.8f, Depth = 0.8f, Height = 0.8f, Hover = 0.05f,
                EyeWidth = 0.1f, EyeHeight = 0.14f, EyeSpacing = 0.24f, EyeLevel = 0.4f,
            },
        };

        /// <summary>A shape's look from <see cref="Looks"/>, at its scale in content.</summary>
        public static ShapeLook LookOf(Shape shape) => Looks[shape.Mesh].Scaled(shape.Scale);

        /// <summary>How far the eyes stand out of the face, in meters.</summary>
        private const float EyeDepth = 0.06f;

        /// <summary>A shape's mesh, standing on its origin, sized by its look.</summary>
        public static Mesh Shape(MeshKind kind, ShapeLook look)
        {
            var x = look.Width / 2;
            var z = look.Depth / 2;
            var h = look.Height;
            switch (kind)
            {
                case MeshKind.Diamond:
                    var top = new Vector3(0, h, 0);
                    var bottom = Vector3.zero;
                    var w = look.Waist;
                    var ring = new[] { new Vector3(-x, w, -z), new Vector3(x, w, -z), new Vector3(x, w, z), new Vector3(-x, w, z) };
                    return Solid("Diamond", ring.SelectMany((p, i) => new[] { top, p, ring[(i + 1) % 4], bottom, p, ring[(i + 1) % 4] }), new Vector3(0, w, 0));
                case MeshKind.Pyramid:
                    var apex = new Vector3(0, h, 0);
                    var corners = new[] { new Vector3(-x, 0, -z), new Vector3(x, 0, -z), new Vector3(x, 0, z), new Vector3(-x, 0, z) };
                    var sides = corners.SelectMany((p, i) => new[] { apex, p, corners[(i + 1) % 4] });
                    return Solid("Pyramid", sides.Concat(new[] { corners[0], corners[1], corners[2], corners[0], corners[2], corners[3] }));
                default:
                    return Cuboid("Cube", new Vector3(0, h / 2, 0), new Vector3(look.Width, h, look.Depth));
            }
        }

        /// <summary>
        /// Where a shape's front face (+Z) is on its center line at height <paramref name="y"/>: how far forward,
        /// and how far it leans back from upright, in radians (negative leans forward, as under a diamond's waist).
        /// </summary>
        public static (float Forward, float Lean) FrontFace(MeshKind kind, ShapeLook look, float y)
        {
            var z = look.Depth / 2;
            var h = look.Height;
            switch (kind)
            {
                case MeshKind.Pyramid:
                    return (z * (1 - y / h), Mathf.Atan2(z, h));
                case MeshKind.Diamond:
                    var w = look.Waist;
                    return y >= w
                        ? (z * (h - y) / (h - w), Mathf.Atan2(z, h - w))
                        : (z * y / w, -Mathf.Atan2(z, w));
                default:
                    return (z, 0);
            }
        }

        /// <summary>
        /// Two eyes on the shape's front face (+Z), so its facing shows: small boxes standing slightly out of the
        /// surface and tilted with it.
        /// </summary>
        public static Mesh Eyes(MeshKind kind, ShapeLook look)
        {
            var y = look.EyeLevel;
            var (z, lean) = FrontFace(kind, look, y);
            var size = new Vector3(look.EyeWidth, look.EyeHeight, EyeDepth);
            var rotation = Quaternion.Euler(-lean * Mathf.Rad2Deg, 0, 0);
            var triangles = new List<Vector3>();
            foreach (var side in new[] { -1, 1 })
            {
                var center = new Vector3(side * look.EyeSpacing / 2, y, z) + rotation * new Vector3(0, 0, size.z / 4);
                triangles.AddRange(Oriented(BoxTriangles(size).Select(v => center + rotation * v).ToList(), center));
            }

            return Build("Eyes", triangles);
        }

        /// <summary>A box mesh around <paramref name="center"/>.</summary>
        public static Mesh Cuboid(string name, Vector3 center, Vector3 size) => Solid(name, BoxTriangles(size).Select(v => center + v), center);

        /// <summary>A box mesh for one of the world's blocks, in world space.</summary>
        public static Mesh Block(Box box) => Cuboid("Block", ToUnity(box.Center), ToUnity(box.Size));

        public static Vector3 ToUnity(System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);

        public static System.Numerics.Vector3 ToNumerics(Vector3 v) => new System.Numerics.Vector3(v.x, v.y, v.z);

        // The twelve triangles of a box centered on the origin, in no particular winding.
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

    /// <summary>A shape's drawn size and eyes, in meters (<see cref="WorldMeshes.Looks"/>).</summary>
    public sealed class ShapeLook
    {
        /// <summary>Size from side to side.</summary>
        public float Width { get; set; }

        /// <summary>Size from front to back.</summary>
        public float Depth { get; set; }

        public float Height { get; set; }

        /// <summary>How far above its feet the shape is drawn.</summary>
        public float Hover { get; set; }

        /// <summary>Diamonds only: how far above the bottom tip the widest ring is.</summary>
        public float Waist { get; set; }

        /// <summary>Each eye's width.</summary>
        public float EyeWidth { get; set; }

        /// <summary>Each eye's height.</summary>
        public float EyeHeight { get; set; }

        /// <summary>From one eye's center to the other's.</summary>
        public float EyeSpacing { get; set; }

        /// <summary>How far above the shape's bottom the eyes' centers are; they sit on the front face there, tilted with it.</summary>
        public float EyeLevel { get; set; }

        /// <summary>This look with every size times <paramref name="scale"/>.</summary>
        public ShapeLook Scaled(float scale) => new ShapeLook
        {
            Width = Width * scale, Depth = Depth * scale, Height = Height * scale, Hover = Hover * scale, Waist = Waist * scale,
            EyeWidth = EyeWidth * scale, EyeHeight = EyeHeight * scale, EyeSpacing = EyeSpacing * scale, EyeLevel = EyeLevel * scale,
        };
    }
}
