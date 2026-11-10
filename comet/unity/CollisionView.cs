using System.Collections.Generic;
using Comet.Content;
using Comet.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Comet.Unity
{
    public sealed class CollisionView : MonoBehaviour
    {
        private const float HiddenOpacity = 0.2f;
        private const int BodySides = 16;

        private const float TerrainCrease = 20;

        [SerializeField]
        private Shader shader = null!;

        private readonly List<(int Mesh, GameObject Lines)> _meshes = new List<(int, GameObject)>();

        private readonly List<GameObject> _fixed = new List<GameObject>();
        private readonly List<(Matrix4x4 Matrix, Color Color, float Until)> _markers = new List<(Matrix4x4, Color, float)>();
        private Material _seen = null!;
        private Material _hidden = null!;
        private Material _always = null!;
        private Mesh _body = null!;
        private CollisionWorld? _world;
        private Transform _root = null!;
        private bool _shown;

        public bool Shown
        {
            get => _shown;
            set
            {
                _shown = value;
                _root.gameObject.SetActive(value);
            }
        }

        private void Awake()
        {
            _seen = Make(CompareFunction.LessEqual, 1);
            _hidden = Make(CompareFunction.Greater, HiddenOpacity);
            _always = Make(CompareFunction.Always, 1);
            _body = Cylinder();
            _root = new GameObject("Collision view").transform;
            _root.gameObject.SetActive(false);
        }

        public void Show(CollisionWorld world, System.Func<CollisionMesh, Color> colorOf, Color terrainColor)
        {
            Clear();
            _world = world;
            for (var i = 0; i < world.Meshes.Count; i++)
            {
                var mesh = world.Meshes[i];
                var terrain = mesh.Kind == CollisionMesh.TerrainKind;
                var edges = ToUnity(FeatureEdges.Of(mesh.Triangles, terrain ? TerrainCrease : 1));
                _meshes.Add((i, AddLines("Mesh " + i, edges, terrain ? terrainColor : colorOf(mesh))));
            }
        }

        public void AddVolume(TriggerVolume volume, PolygonBudget budget, Color color, bool facing = false)
        {
            var edges = VolumeEdges(volume.Kind, Conversions.ToUnity(volume.Size), budget);
            if (facing)
            {
                var shape = edges;
                edges = new Vector3[shape.Length + 2];
                shape.CopyTo(edges, 0);
                FacingEdge(Conversions.ToUnity(volume.Size)).CopyTo(edges, shape.Length);
            }

            var place = Matrix4x4.TRS(Conversions.ToUnity(volume.Position), Quaternion.Euler(0, volume.Yaw, 0), Vector3.one);
            for (var i = 0; i < edges.Length; i++)
            {
                edges[i] = place.MultiplyPoint3x4(edges[i]);
            }

            _fixed.Add(AddLines("Volume", edges, color));
        }

        public static Vector3[] VolumeEdges(PrimitiveKind kind, Vector3 size, PolygonBudget budget) =>
            ToUnity(
                FeatureEdges.Of(PrimitiveMesher.Build(new Primitive { Kind = kind }, new System.Numerics.Vector3(size.x, size.y, size.z), budget))
            );

        public static Vector3[] FacingEdge(Vector3 size)
        {
            var from = Vector3.up * 0.05f;
            return new[] { from, from + Vector3.forward * Mathf.Max(size.z / 2, 1) };
        }

        public void DrawBody(Vector3 feet, float radius, float height, Color color)
        {
            if (_shown)
            {
                Render(Matrix4x4.TRS(feet, Quaternion.identity, new Vector3(radius, height, radius)), color);
            }
        }

        public void Mark(Vector3 feet, float radius, float height, Color color, float seconds) =>
            _markers.Add((Matrix4x4.TRS(feet, Quaternion.identity, new Vector3(radius, height, radius)), color, Time.unscaledTime + seconds));

        public void Clear()
        {
            foreach (var (_, lines) in _meshes)
            {
                Destroy(lines);
            }

            foreach (var volume in _fixed)
            {
                Destroy(volume);
            }

            _meshes.Clear();
            _fixed.Clear();
            _markers.Clear();
            _world = null;
        }

        private void LateUpdate()
        {
            var now = Time.unscaledTime;
            _markers.RemoveAll(m => m.Until <= now);
            if (!_shown)
            {
                return;
            }

            if (_world != null)
            {
                foreach (var (mesh, lines) in _meshes)
                {
                    lines.SetActive(_world.IsOn(mesh));
                }
            }

            foreach (var (matrix, color, until) in _markers)
            {
                Render(matrix, new Color(color.r, color.g, color.b, color.a * Mathf.Clamp01(until - now)));
            }
        }

        private void Render(Matrix4x4 matrix, Color color)
        {
            var block = new MaterialPropertyBlock();
            block.SetColor(Tint, color);
            var parameters = new RenderParams(_always) { matProps = block, layer = gameObject.layer };
            Graphics.RenderMesh(parameters, _body, 0, matrix);
        }

        private static readonly int Opacity = Shader.PropertyToID("_Opacity");
        private static readonly int Tint = Shader.PropertyToID("_Color");

        private static Vector3[] ToUnity(System.Numerics.Vector3[] points)
        {
            var converted = new Vector3[points.Length];
            for (var i = 0; i < points.Length; i++)
            {
                converted[i] = Conversions.ToUnity(points[i]);
            }

            return converted;
        }

        private GameObject AddLines(string name, Vector3[] points, Color color)
        {
            var indices = new int[points.Length];
            for (var i = 0; i < points.Length; i++)
            {
                indices[i] = i;
            }

            var mesh = new Mesh { name = name, indexFormat = points.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(points);
            mesh.SetColors(Fill(points.Length, color));
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            var lines = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            lines.transform.SetParent(_root, false);
            lines.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = lines.GetComponent<MeshRenderer>();
            // A second material on the one submesh draws it again: the lines in sight, then those behind something.
            renderer.sharedMaterials = new[] { _seen, _hidden };
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return lines;
        }

        private static Color[] Fill(int count, Color color)
        {
            var colors = new Color[count];
            for (var i = 0; i < count; i++)
            {
                colors[i] = color;
            }

            return colors;
        }

        private Material Make(CompareFunction depthTest, float opacity)
        {
            var material = new Material(shader) { hideFlags = HideFlags.DontSave };
            material.SetFloat("_ZTest", (float)depthTest);
            material.SetFloat(Opacity, opacity);
            return material;
        }

        private static Mesh Cylinder()
        {
            var points = new List<Vector3>();
            var indices = new List<int>();
            for (var i = 0; i < BodySides; i++)
            {
                var angle = i * 2 * Mathf.PI / BodySides;
                var x = Mathf.Sin(angle);
                var z = Mathf.Cos(angle);
                points.Add(new Vector3(x, 0, z));
                points.Add(new Vector3(x, 1, z));
            }

            for (var i = 0; i < BodySides; i++)
            {
                var next = (i + 1) % BodySides;
                indices.AddRange(new[] { 2 * i, 2 * next, 2 * i + 1, 2 * next + 1 });
                if (i % (BodySides / 4) == 0)
                {
                    indices.AddRange(new[] { 2 * i, 2 * i + 1 });
                }
            }

            var mesh = new Mesh { name = "Body" };
            mesh.SetVertices(points);
            mesh.SetColors(Fill(points.Count, Color.white));
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            return mesh;
        }

        private void OnDestroy()
        {
            Clear();
            Destroy(_body);
            Destroy(_seen);
            Destroy(_hidden);
            Destroy(_always);
            if (_root != null)
            {
                Destroy(_root.gameObject);
            }
        }
    }
}
