using System.Collections.Generic;
using Comet.Content;
using Comet.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Comet.Unity
{
    /// <summary>
    /// A debug view of what movement collides with, as lines: a <see cref="CollisionWorld"/>'s meshes, volumes the
    /// game adds, and bodies it draws each frame. The game decides when it shows (a key, a dev menu). Meshes are faint,
    /// so collision that matches its model barely shows; lines behind something are fainter still.
    /// </summary>
    public sealed class CollisionView : MonoBehaviour
    {
        // Lines behind something, as a share of their opacity in sight.
        private const float HiddenOpacity = 0.2f;

        // Terrain edges between cells meeting at less than this many degrees aren't drawn, so gentle slopes don't
        // fill the view with the grid; cliffs, skirts and piece edges still show.
        private const float TerrainCrease = 20;

        [SerializeField] private Shader shader = null!;

        private readonly List<(int Mesh, GameObject Lines)> _meshes = new List<(int, GameObject)>();
        // Volumes and terrain: lines that never turn off.
        private readonly List<GameObject> _fixed = new List<GameObject>();
        private readonly List<(Matrix4x4 Matrix, Color Color, float Until)> _markers = new List<(Matrix4x4, Color, float)>();
        private Material _seen = null!;
        private Material _hidden = null!;
        private Material _always = null!;
        private Mesh _body = null!;
        private CollisionWorld? _world;
        private Transform _root = null!;
        private bool _shown;

        /// <summary>Whether the view is drawn.</summary>
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
            _body = Box();
            // At the scene's root: the lines are in world space whatever this object's transform.
            _root = new GameObject("Collision view").transform;
            _root.gameObject.SetActive(false);
        }

        /// <summary>
        /// Shows <paramref name="world"/>'s meshes from now on, each in the color <paramref name="colorOf"/> gives it,
        /// and its terrain in <paramref name="terrainColor"/>; none of the volumes added before.
        /// </summary>
        public void Show(CollisionWorld world, System.Func<CollisionMesh, Color> colorOf, Color terrainColor)
        {
            Clear();
            _world = world;
            for (var i = 0; i < world.Meshes.Count; i++)
            {
                _meshes.Add((i, AddLines("Mesh " + i, world.Meshes[i].Triangles, colorOf(world.Meshes[i]))));
            }

            // Terrain collides as height maps; these are the meshes drawn from the same maps, so they show its shape.
            if (world.Terrain != null)
            {
                foreach (var chunk in TerrainMesher.Build(world.Terrain, 0, 0))
                {
                    var lines = AddLines($"Terrain {chunk.Key}", chunk.Value.Positions, terrainColor, TerrainCrease);
                    lines.transform.localPosition = new Vector3(chunk.Key.Origin.X, chunk.Key.Origin.Y, chunk.Key.Origin.Z);
                    _fixed.Add(lines);
                }
            }
        }

        /// <summary>Adds <paramref name="volume"/>'s shape in <paramref name="color"/>.</summary>
        public void AddVolume(TriggerVolume volume, PolygonBudget budget, Color color)
        {
            var triangles = PrimitiveMesher.Build(new Primitive { Kind = volume.Kind }, volume.Size, budget);
            var placed = CollisionMesh.Placed(triangles, System.Numerics.Matrix4x4.CreateFromQuaternion(ZoneGrid.Rotation(new System.Numerics.Vector3(0, volume.Yaw, 0)))
                * System.Numerics.Matrix4x4.CreateTranslation(volume.Position));
            _fixed.Add(AddLines("Volume", placed.Triangles, color));
        }

        /// <summary>Draws a body this frame: an upright box on <paramref name="feet"/>, <paramref name="radius"/> out each way, seen through everything.</summary>
        public void DrawBody(Vector3 feet, float radius, float height, Color color)
        {
            if (_shown)
            {
                Render(Matrix4x4.TRS(feet, Quaternion.identity, new Vector3(radius, height, radius)), color);
            }
        }

        /// <summary>Draws a body at <paramref name="feet"/> for the next <paramref name="seconds"/>, fading out.</summary>
        public void Mark(Vector3 feet, float radius, float height, Color color, float seconds) =>
            _markers.Add((Matrix4x4.TRS(feet, Quaternion.identity, new Vector3(radius, height, radius)), color, Time.unscaledTime + seconds));

        /// <summary>Forgets the world, volumes and markers.</summary>
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

            // Meshes turned off (a flipped placement) aren't solid, so they aren't drawn.
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

        private GameObject AddLines(string name, IReadOnlyList<System.Numerics.Vector3> triangles, Color color, float creaseDegrees = 1)
        {
            var edges = FeatureEdges.Of(triangles, creaseDegrees);
            var points = new Vector3[edges.Length];
            var indices = new int[edges.Length];
            for (var i = 0; i < edges.Length; i++)
            {
                points[i] = new Vector3(edges[i].X, edges[i].Y, edges[i].Z);
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

        // A player's body as Comet collides it: an upright box, its square footprint 1 from the origin each way, 1
        // tall, standing on the origin. It doesn't turn with the player.
        private static Mesh Box()
        {
            var points = new List<Vector3>();
            for (var i = 0; i < 8; i++)
            {
                points.Add(new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? 0 : 1, (i & 4) == 0 ? -1 : 1));
            }

            var mesh = new Mesh { name = "Body" };
            mesh.SetVertices(points);
            mesh.SetColors(Fill(points.Count, Color.white));
            mesh.SetIndices(new[] { 0, 1, 1, 5, 5, 4, 4, 0, 2, 3, 3, 7, 7, 6, 6, 2, 0, 2, 1, 3, 5, 7, 4, 6 }, MeshTopology.Lines, 0);
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
