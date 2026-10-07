using Comet.Simulation;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;
using UnityEngine;

namespace ShapeLand.Client
{
    /// <summary>
    /// The game scene's view: draws the island and its blocks once the content has loaded, and the player's own
    /// shape (with eyes, to show its facing) once the server spawns it, with the orbit camera following.
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

        /// <summary>The player's own shape, once spawned.</summary>
        public LocalPlayer Player { get; private set; }

        private void Awake()
        {
            _join = GetComponent<JoinClient>();
            _join.ContentLoaded += OnContentLoaded;
            _join.OwnSpawned += OnOwnSpawned;
            _controls = new ShapeLandControls();
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
            var material = new Material(shapeMaterial) { color = Colour(spawn.Colour) };
            var player = new GameObject($"{spawn.Name} (you)");
            var body = AddMesh("Shape", WorldMeshes.Shape(shape.Mesh, ShapeLandWorld.Rules), material, player.transform);
            body.transform.localPosition = new Vector3(0, HoverHeight, 0);
            var eyes = new Material(eyeMaterial) { color = Colour(spawn.EyeColour) };
            AddMesh("Eyes", WorldMeshes.Eyes(shape.Mesh, ShapeLandWorld.Rules), eyes, body.transform);
            Player = player.AddComponent<LocalPlayer>();
            Player.Begin(_join.Session, _world, shape, new Vector3(spawn.X, spawn.Y, spawn.Z), spawn.Facing, orbitCamera, _controls);
            orbitCamera.Follow(Player.transform, _controls, _world);
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
            _controls.Dispose();
            if (Player != null)
            {
                Destroy(Player.gameObject);
            }
        }
    }
}
