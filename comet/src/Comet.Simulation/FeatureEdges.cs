using System;
using System.Collections.Generic;
using System.Numerics;

namespace Comet.Simulation
{
    public static class FeatureEdges
    {
        private const float Weld = 0.001f;

        /// <summary>Edges with one triangle, or whose triangles meet at more than <paramref name="creaseDegrees"/>, as point pairs.</summary>
        public static Vector3[] Of(IReadOnlyList<Vector3> triangles, float creaseDegrees = 1)
        {
            var minCos = (float)Math.Cos(creaseDegrees * Math.PI / 180);
            var edges = new Dictionary<((long, long, long), (long, long, long)), Edge>();
            for (var i = 0; i + 2 < triangles.Count; i += 3)
            {
                var normal = Vector3.Cross(triangles[i + 1] - triangles[i], triangles[i + 2] - triangles[i]);
                if (normal.LengthSquared() < 1e-12f)
                {
                    continue;
                }

                normal = Vector3.Normalize(normal);
                for (var k = 0; k < 3; k++)
                {
                    var a = triangles[i + k];
                    var b = triangles[i + (k + 1) % 3];
                    var ka = Key(a);
                    var kb = Key(b);
                    var key = Compare(ka, kb) <= 0 ? (ka, kb) : (kb, ka);
                    if (edges.TryGetValue(key, out var edge))
                    {
                        edge.Feature |= ++edge.Count > 2 || Vector3.Dot(edge.Normal, normal) < minCos;
                    }
                    else
                    {
                        edges[key] = new Edge
                        {
                            A = a,
                            B = b,
                            Normal = normal,
                            Count = 1,
                        };
                    }
                }
            }

            var lines = new List<Vector3>();
            foreach (var edge in edges.Values)
            {
                if (edge.Feature || edge.Count == 1)
                {
                    lines.Add(edge.A);
                    lines.Add(edge.B);
                }
            }

            return lines.ToArray();
        }

        private static (long, long, long) Key(Vector3 p) =>
            ((long)Math.Round(p.X / Weld), (long)Math.Round(p.Y / Weld), (long)Math.Round(p.Z / Weld));

        private static int Compare((long, long, long) a, (long, long, long) b) => a.CompareTo(b);

        private sealed class Edge
        {
            public Vector3 A;
            public Vector3 B;
            public Vector3 Normal;
            public int Count;
            public bool Feature;
        }
    }
}
