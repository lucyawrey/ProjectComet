using System.Collections.Generic;
using Comet.Simulation;
using UnityEngine;

namespace Comet.Unity
{
    /// <summary>
    /// Unity meshes for terrain chunks from <see cref="TerrainMesher"/>, which builds them from the same data the server
    /// collides with, so what's drawn is what's stood on. For the game and the zone editor alike.
    /// </summary>
    public static class TerrainMeshes
    {
        /// <summary>A terrain chunk's mesh, flat-shaded, with its colors as vertex colors (for the Comet/Vertex Color Lit shader).</summary>
        public static Mesh Build(string name, TerrainMesh terrain)
        {
            var vertices = new List<Vector3>(terrain.Positions.Count);
            foreach (var position in terrain.Positions)
            {
                vertices.Add(new Vector3(position.X, position.Y, position.Z));
            }

            var colors = new Color32[terrain.Colors.Count];
            for (var i = 0; i < colors.Length; i++)
            {
                var rgb = terrain.Colors[i];
                colors[i] = new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
            }

            // Every triangle has its own corners, so the recalculated normals are flat: one per face.
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65535)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            var indices = new int[vertices.Count];
            for (var i = 0; i < indices.Length; i++)
            {
                indices[i] = i;
            }

            mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
