using System.Collections.Generic;
using Comet.Content;
using Comet.Simulation;
using UnityEngine;

namespace Comet.Unity
{
    /// <summary>
    /// Unity meshes for primitives from <see cref="PrimitiveMesher"/>, which builds them from the same values the server
    /// collides with. For the game and the zone editor alike: one mesh per primitive while editing, and placed
    /// primitives combined into one vertex-colored mesh per chunk in the game, so a large zone stays a few draw calls.
    /// </summary>
    public static class PrimitiveMeshes
    {
        // Meshes by the mesher's shared triangles, which are the same array for the same primitive, size and budget.
        private static readonly Dictionary<System.Numerics.Vector3[], Mesh> Meshes = new Dictionary<System.Numerics.Vector3[], Mesh>();

        /// <summary>A primitive placement's own mesh, flat-shaded, shared by every placement of the same primitive and size.</summary>
        public static Mesh For(Placement placement, PolygonBudget budget)
        {
            var triangles = PrimitiveMesher.For(placement, budget);
            if (!Meshes.TryGetValue(triangles, out var mesh) || mesh == null)
            {
                mesh = new Mesh { name = ZoneName(placement), hideFlags = HideFlags.DontSave };
                var builder = new Builder();
                builder.Add(triangles, System.Numerics.Matrix4x4.Identity, 0xFFFFFF);
                builder.Fill(mesh);
                Meshes[triangles] = mesh;
            }

            return mesh;
        }

        /// <summary>Collects placed primitives into one mesh, each in its own color as vertex colors (for the Comet/Vertex Color Lit shader).</summary>
        public sealed class Builder
        {
            private readonly List<Vector3> _vertices = new List<Vector3>();
            private readonly List<Color32> _colors = new List<Color32>();

            public int VertexCount => _vertices.Count;

            /// <summary>Adds triangles placed by <paramref name="transform"/>, relative to the mesh's origin, in a 0xRRGGBB color.</summary>
            public void Add(IReadOnlyList<System.Numerics.Vector3> triangles, System.Numerics.Matrix4x4 transform, uint rgb)
            {
                var color = new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
                var mirrored = transform.GetDeterminant() < 0;
                for (var i = 0; i < triangles.Count; i += 3)
                {
                    for (var k = 0; k < 3; k++)
                    {
                        // A mirroring transform turns triangles inside out; rewind them to face out again.
                        var corner = triangles[i + (mirrored && k > 0 ? 3 - k : k)];
                        var p = System.Numerics.Vector3.Transform(corner, transform);
                        _vertices.Add(new Vector3(p.X, p.Y, p.Z));
                        _colors.Add(color);
                    }
                }
            }

            /// <summary>Replaces <paramref name="mesh"/>'s contents with what's been added.</summary>
            public void Fill(Mesh mesh)
            {
                // Every triangle has its own corners, so the recalculated normals are flat: one per face.
                mesh.Clear();
                mesh.indexFormat = _vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                mesh.SetVertices(_vertices);
                mesh.SetColors(_colors);
                var indices = new int[_vertices.Count];
                for (var i = 0; i < indices.Length; i++)
                {
                    indices[i] = i;
                }

                mesh.SetTriangles(indices, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
            }
        }

        private static string ZoneName(Placement placement) => $"{placement.Primitive!.Kind} {placement.Scale.X:0.##}×{placement.Scale.Y:0.##}×{placement.Scale.Z:0.##}";
    }
}
