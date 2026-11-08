using System;
using System.Collections.Generic;
using Comet.Content;
using Comet.Simulation;
using UnityEngine;

namespace Comet.Unity
{
    /// <summary>
    /// Unity meshes for primitives from <see cref="PrimitiveMesher"/>, built from the values the server collides with.
    /// In the game, placed primitives are combined into one mesh per chunk, so a large zone stays a few draw calls.
    /// </summary>
    public static class PrimitiveMeshes
    {
        private static Dictionary<string, Entry>? _meshes;

        // Meshes an earlier domain built (a reload empties the dictionary but keeps them, as they're DontSave): not
        // reused, since the mesher may have changed, but destroyed once nothing shows them.
        private static List<Mesh> _strays = new List<Mesh>();
        private static int _uses;
        private static int _pruneAbove = PruneAbove;

        // Past this many meshes, the ones nothing shows are destroyed (the zone editor makes one per size tried),
        // except the most recently asked for, which a caller may still be collecting.
        private const int PruneAbove = 2048,
            KeepRecent = 512;
        private const string NamePrefix = "Primitive ";

        private sealed class Entry
        {
            public Mesh Mesh = null!;
            public int Used;
        }

        public static Mesh For(Placement placement, PolygonBudget budget)
        {
            var meshes = _meshes ??= FirstUse();
            var name = MeshName(placement, budget);
            if (!meshes.TryGetValue(name, out var entry) || entry.Mesh == null)
            {
                // Built here rather than taken from the mesher's cache, which keeps every size it's asked for.
                var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
                var builder = new Builder();
                builder.Add(PrimitiveMesher.Build(placement.Primitive!, placement.Scale, budget), System.Numerics.Matrix4x4.Identity, 0xFFFFFF);
                builder.Fill(mesh);
                meshes[name] = entry = new Entry { Mesh = mesh };
            }

            entry.Used = ++_uses;
            if (meshes.Count > _pruneAbove)
            {
                Prune(meshes);
            }

            return entry.Mesh;
        }

        private static Dictionary<string, Entry> FirstUse()
        {
            foreach (var mesh in Resources.FindObjectsOfTypeAll<Mesh>())
            {
                if (mesh.hideFlags == HideFlags.DontSave && mesh.name.StartsWith(NamePrefix, StringComparison.Ordinal))
                {
                    _strays.Add(mesh);
                }
            }

            if (_strays.Count > 0)
            {
                var shown = Shown();
                _strays.RemoveAll(mesh => Destroy(mesh, shown));
            }

            return new Dictionary<string, Entry>();
        }

        private static HashSet<Mesh> Shown()
        {
            var shown = new HashSet<Mesh>();
            foreach (var filter in Resources.FindObjectsOfTypeAll<MeshFilter>())
            {
                shown.Add(filter.sharedMesh);
            }

            foreach (var collider in Resources.FindObjectsOfTypeAll<MeshCollider>())
            {
                shown.Add(collider.sharedMesh);
            }

            return shown;
        }

        // True if the mesh is gone; one that is still shown is kept.
        private static bool Destroy(Mesh mesh, HashSet<Mesh> shown)
        {
            if (mesh == null)
            {
                return true;
            }

            if (shown.Contains(mesh))
            {
                return false;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(mesh);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }

            return true;
        }

        private static void Prune(Dictionary<string, Entry> meshes)
        {
            var shown = Shown();
            _strays.RemoveAll(mesh => Destroy(mesh, shown));
            foreach (var (name, entry) in new List<KeyValuePair<string, Entry>>(meshes))
            {
                if (entry.Used <= _uses - KeepRecent && Destroy(entry.Mesh, shown))
                {
                    meshes.Remove(name);
                }
            }

            _pruneAbove = Math.Max(PruneAbove, meshes.Count * 2);
        }

        public sealed class Builder
        {
            private readonly List<Vector3> _vertices = new List<Vector3>();
            private readonly List<Color32> _colors = new List<Color32>();

            public int VertexCount => _vertices.Count;

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

        // "Primitive Box 2×1×2", then every value the mesh depends on, exactly.
        private static string MeshName(Placement placement, PolygonBudget budget)
        {
            var p = placement.Primitive!;
            var size = placement.Scale;
            return FormattableString.Invariant(
                $"{NamePrefix}{p.Kind} {size.X:0.##}×{size.Y:0.##}×{size.Z:0.##} ({size.X:R} {size.Y:R} {size.Z:R} {p.Sides} {p.Bevel:R} {p.Steps} {p.OpeningWidth:R} {p.OpeningHeight:R} {p.Thickness:R} {budget.FacetLength:R} {budget.MinSides} {budget.MaxSides})"
            );
        }
    }
}
