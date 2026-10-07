using System.Collections.Generic;
using Comet.Simulation;
using Comet.Unity;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;
using UnityEngine;

namespace ShapeLand.Client
{
    /// <summary>
    /// The game scene's view: draws the island and its blocks once the content has loaded, the player's own shape
    /// (with eyes, to show its facing) once the server spawns it, with the orbit camera following, and the other
    /// players where the interpolation buffer puts them.
    /// </summary>
    [RequireComponent(typeof(JoinClient))]
    public sealed class GameView : MonoBehaviour
    {
        /// <summary>How high shapes are drawn above their feet, in metres. Only drawing: collision and movement are unchanged.</summary>
        public const float HoverHeight = 0.15f;

        [SerializeField] private Material terrainMaterial;
        [SerializeField] private Material blockMaterial;
        [SerializeField] private Material shapeMaterial;
        [SerializeField] private Material eyeMaterial;
        [SerializeField] private OrbitCamera orbitCamera;

        private JoinClient _join;
        private CollisionWorld _world;
        private ShapeLandControls _controls;

        private readonly Dictionary<uint, Transform> _others = new Dictionary<uint, Transform>();
        private readonly Dictionary<MeshKind, (Mesh Body, Mesh Eyes)> _meshes = new Dictionary<MeshKind, (Mesh Body, Mesh Eyes)>();

        /// <summary>The player's own shape, once spawned.</summary>
        public LocalPlayer Player { get; private set; }

        /// <summary>The other players drawn now, by entity ID.</summary>
        public IReadOnlyDictionary<uint, Transform> Others => _others;

        private void Awake()
        {
            _join = GetComponent<JoinClient>();
            _join.ContentLoaded += OnContentLoaded;
            _join.OwnSpawned += OnOwnSpawned;
            _join.OtherSpawned += OnOtherSpawned;
            _join.OtherLeft += OnOtherLeft;
            _controls = new ShapeLandControls();
        }

        // Others are drawn where the interpolation buffer puts them, a little behind the newest states.
        private void Update()
        {
            if (_others.Count == 0)
            {
                return;
            }

            var session = _join.Session;
            var renderTick = session.RenderTick(CometConnection.Now);
            foreach (var other in _others)
            {
                if (session.Entities.TrySample(other.Key, renderTick, out var pose))
                {
                    other.Value.SetPositionAndRotation(WorldMeshes.ToUnity(pose.Position), Quaternion.Euler(0, pose.Facing * Mathf.Rad2Deg, 0));
                }
            }
        }

        private void OnContentLoaded(ShapeLandContent content)
        {
            _world = ShapeLandWorld.Create(content);
            AddMesh("Island", WorldMeshes.Island(_world.Terrain), terrainMaterial, transform);
            var blocks = new GameObject("Blocks").transform;
            blocks.SetParent(transform, false);
            foreach (var box in _world.Boxes)
            {
                AddMesh("Block", WorldMeshes.Block(box), blockMaterial, blocks);
            }
        }

        private void OnOwnSpawned(PlayerSpawn spawn)
        {
            var shape = _join.Content.Shapes[spawn.Shape];
            Player = CreateShape(spawn, $"{spawn.Name} (you)").AddComponent<LocalPlayer>();
            Player.Begin(_join.Session, _world, shape, new Vector3(spawn.X, spawn.Y, spawn.Z), spawn.Facing, orbitCamera, _controls);
            orbitCamera.Follow(Player.transform, _controls, _world);
        }

        private void OnOtherSpawned(PlayerSpawn spawn)
        {
            OnOtherLeft(spawn.EntityId);
            var other = CreateShape(spawn, $"{spawn.Name} ({spawn.EntityId})");
            other.transform.SetPositionAndRotation(new Vector3(spawn.X, spawn.Y, spawn.Z), Quaternion.Euler(0, spawn.Facing * Mathf.Rad2Deg, 0));
            _others[spawn.EntityId] = other.transform;
        }

        private void OnOtherLeft(uint entityId)
        {
            if (_others.TryGetValue(entityId, out var other))
            {
                _others.Remove(entityId);
                DestroyShape(other.gameObject);
            }
        }

        // A player's shape: the body and eyes in their colours, hovering, on an object standing at their feet.
        private GameObject CreateShape(PlayerSpawn spawn, string name)
        {
            var kind = _join.Content.Shapes[spawn.Shape].Mesh;
            if (!_meshes.TryGetValue(kind, out var meshes))
            {
                meshes = (WorldMeshes.Shape(kind, ShapeLandWorld.Rules), WorldMeshes.Eyes(kind, ShapeLandWorld.Rules));
                _meshes[kind] = meshes;
            }

            var player = new GameObject(name);
            var body = AddMesh("Shape", meshes.Body, new Material(shapeMaterial) { color = Colour(spawn.Colour) }, player.transform);
            body.transform.localPosition = new Vector3(0, HoverHeight, 0);
            AddMesh("Eyes", meshes.Eyes, new Material(eyeMaterial) { color = Colour(spawn.EyeColour) }, body.transform);
            return player;
        }

        // Destroys a shape and the materials made for it.
        private static void DestroyShape(GameObject shape)
        {
            foreach (var renderer in shape.GetComponentsInChildren<MeshRenderer>())
            {
                Destroy(renderer.sharedMaterial);
            }

            Destroy(shape);
        }

        private static GameObject AddMesh(string name, Mesh mesh, Material material, Transform parent)
        {
            var thing = new GameObject(name);
            thing.transform.SetParent(parent, false);
            thing.AddComponent<MeshFilter>().sharedMesh = mesh;
            thing.AddComponent<MeshRenderer>().sharedMaterial = material;
            return thing;
        }

        private static Color Colour(uint rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

        private void OnDestroy()
        {
            _join.ContentLoaded -= OnContentLoaded;
            _join.OwnSpawned -= OnOwnSpawned;
            _join.OtherSpawned -= OnOtherSpawned;
            _join.OtherLeft -= OnOtherLeft;
            _controls.Dispose();
            if (Player != null)
            {
                DestroyShape(Player.gameObject);
            }

            foreach (var other in _others.Values)
            {
                DestroyShape(other.gameObject);
            }
        }
    }
}
