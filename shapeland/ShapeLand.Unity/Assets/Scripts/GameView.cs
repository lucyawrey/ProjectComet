using System.Collections.Generic;
using Comet.Client;
using Comet.Content;
using Comet.Simulation;
using Comet.Unity;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;
using UnityEngine;

namespace ShapeLand.Client
{
    /// <summary>
    /// The game scene's view: draws the zone (its terrain and placed objects) once the content has loaded, the player's own shape
    /// (with eyes, to show its facing) once the server spawns it, with the orbit camera following, and the other
    /// players where the interpolation buffer puts them. Others fade out as they fall off the island and back in
    /// after respawning (<see cref="RespawnFade"/>); the player's own fall darkens the screen instead (<see cref="Hud"/>).
    /// </summary>
    [RequireComponent(typeof(JoinClient))]
    public sealed class GameView : MonoBehaviour
    {
        /// <summary>A layer the main camera doesn't draw, for the join screen's shape preview.</summary>
        public const int PreviewLayer = 31;

        [Tooltip("Terrain's material: Comet/Vertex Color Lit, since its colors are vertex colors.")]
        [SerializeField] private Material terrainMaterial;
        [Tooltip("The material placed objects are drawn with (their models' own are ignored).")]
        [SerializeField] private Material blockMaterial;
        [Tooltip("The models for asset keys, kept up to date by the editor from the content.")]
        [SerializeField] private AssetLibrary assets;
        [Tooltip("The color of terrain no one has painted.")]
        [SerializeField] private Color groundColor = new Color32(0x73, 0x9e, 0x4d, 0xff);
        [Tooltip("The color of terrain's skirts: the walls and bottoms where its ground ends.")]
        [SerializeField] private Color skirtColor = new Color32(0x7a, 0x5c, 0x3e, 0xff);
        [Tooltip("The player shape for content's mesh = \"cube\".")]
        [SerializeField] private ShapeBody cubePrefab;
        [Tooltip("The player shape for content's mesh = \"diamond\".")]
        [SerializeField] private ShapeBody diamondPrefab;
        [Tooltip("The player shape for content's mesh = \"pyramid\".")]
        [SerializeField] private ShapeBody pyramidPrefab;
        [SerializeField] private OrbitCamera orbitCamera;

        private readonly Dictionary<uint, ShapeBody> _others = new Dictionary<uint, ShapeBody>();
        private readonly Dictionary<uint, PlayerSpawn> _spawns = new Dictionary<uint, PlayerSpawn>();
        private JoinClient _join;
        private CollisionWorld _world;
        private ShapeLandControls _controls;
        private ShapeBody _playerShape;

        /// <summary>The player, once spawned; their shape is a child of it.</summary>
        public LocalPlayer Player { get; private set; }

        /// <summary>The other players' shapes drawn now, by entity ID.</summary>
        public IReadOnlyDictionary<uint, ShapeBody> Others => _others;

        /// <summary>Every player in view, this one included, as the server spawned them (name, shape, colors), by entity ID.</summary>
        public IReadOnlyDictionary<uint, PlayerSpawn> Spawns => _spawns;

        /// <summary>The same as a concrete dictionary, so looping over it every frame doesn't box an enumerator.</summary>
        internal Dictionary<uint, PlayerSpawn> SpawnTable => _spawns;

        public ShapeLandControls Controls => _controls;

        public Camera Camera => orbitCamera.GetComponent<Camera>();

        /// <summary>A player's drawn shape (this one's too), or null.</summary>
        public ShapeBody ShapeOf(uint entityId) =>
            Player != null && entityId == _join.Session?.EntityId ? _playerShape
            : _others.TryGetValue(entityId, out var other) ? other : null;

        private void Awake()
        {
            _join = GetComponent<JoinClient>();
            _join.ContentLoaded += OnContentLoaded;
            _join.OwnSpawned += OnOwnSpawned;
            _join.OtherSpawned += OnOtherSpawned;
            _join.OtherLeft += OnOtherLeft;
            _join.ConnectionLost += OnConnectionLost;
            _controls = new ShapeLandControls();
            _controls.Enable();
            Camera.cullingMask &= ~(1 << PreviewLayer);
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
                    var shape = other.Value;
                    shape.transform.SetPositionAndRotation(WorldMeshes.ToUnity(pose.Position), Quaternion.Euler(0, pose.Facing * Mathf.Rad2Deg, 0));
                    shape.Faded = RespawnFade.Step(shape.Faded, pose.Position.Y, _world.KillHeight, Time.unscaledDeltaTime);
                }
            }
        }

        private void OnContentLoaded(ShapeLandContent content)
        {
            _world = ShapeLandWorld.Create(content);
            var terrain = new GameObject("Terrain").transform;
            terrain.SetParent(transform, false);
            foreach (var chunk in TerrainMesher.Build(_world.Terrain, Rgb(groundColor), Rgb(skirtColor)))
            {
                var name = $"Chunk {chunk.Key}";
                var drawn = AddMesh(name, WorldMeshes.Terrain(name, chunk.Value), terrainMaterial, terrain);
                drawn.transform.localPosition = WorldMeshes.ToUnity(chunk.Key.Origin);
            }

            var placed = new GameObject("Placed").transform;
            placed.SetParent(transform, false);
            foreach (var chunk in ShapeLandWorld.Zone(content).Chunks)
            {
                foreach (var placement in chunk.Placements)
                {
                    Place(placement, placed);
                }
            }

            orbitCamera.Showcase(WorldMeshes.ToUnity(ShapeLandWorld.SpawnPoint(content, _world)));
        }

        // An asset's model where a zone places it, in the placed objects' material; its collision objects are hidden.
        private void Place(Placement placement, Transform parent)
        {
            var model = assets.Model(placement.Asset);
            if (model == null)
            {
                Debug.LogWarning($"No model for {placement.Asset} in the asset library; is the content up to date in the editor?");
                return;
            }

            var thing = Instantiate(model, parent, false);
            thing.name = placement.Asset;
            var rotation = placement.Rotation;
            thing.transform.SetLocalPositionAndRotation(WorldMeshes.ToUnity(placement.Position), Quaternion.Euler(rotation.X, rotation.Y, rotation.Z));
            thing.transform.localScale = WorldMeshes.ToUnity(placement.Scale);
            foreach (var renderer in thing.GetComponentsInChildren<Renderer>())
            {
                if (renderer.name.EndsWith(Asset.CollisionSuffix, System.StringComparison.Ordinal))
                {
                    renderer.enabled = false;
                }
                else
                {
                    renderer.sharedMaterial = blockMaterial;
                }
            }
        }

        // On joining, the player and their shape; on a change of look, a new shape where they stand, so the motor and
        // the camera carry on as they were (the server's snap-back has already put the player where it has them).
        private void OnOwnSpawned(PlayerSpawn spawn)
        {
            _spawns[spawn.EntityId] = spawn;
            var shape = _join.Content.Shapes[spawn.Shape];
            if (Player == null)
            {
                Player = new GameObject().AddComponent<LocalPlayer>();
                Player.Begin(_join.Session, _world, shape, new Vector3(spawn.X, spawn.Y, spawn.Z), spawn.Facing, orbitCamera, _controls);
                orbitCamera.Follow(Player.transform, _controls, _world);
            }
            else
            {
                Destroy(_playerShape.gameObject);
                Player.ChangeShape(shape);
            }

            Player.name = $"{spawn.Name} (you)";
            _playerShape = BuildShape(shape, spawn.Color, spawn.EyeColor, Player.transform);
        }

        private void OnOtherSpawned(PlayerSpawn spawn)
        {
            OnOtherLeft(spawn.EntityId);
            _spawns[spawn.EntityId] = spawn;
            var other = BuildShape(_join.Content.Shapes[spawn.Shape], spawn.Color, spawn.EyeColor, transform);
            other.name = $"{spawn.Name} ({spawn.EntityId})";
            other.transform.SetPositionAndRotation(new Vector3(spawn.X, spawn.Y, spawn.Z), Quaternion.Euler(0, spawn.Facing * Mathf.Rad2Deg, 0));
            _others[spawn.EntityId] = other;
        }

        // Everyone leaves the world, this player too, and the camera goes back to orbiting the island.
        private void OnConnectionLost(CloseReason reason)
        {
            foreach (var id in new List<uint>(_others.Keys))
            {
                OnOtherLeft(id);
            }

            if (Player != null)
            {
                orbitCamera.Release();
                Destroy(Player.gameObject);
                Player = null;
                _playerShape = null;
            }

            _spawns.Clear(); // this player's own entry too, or a rejoin would tag someone with the old name
        }

        private void OnOtherLeft(uint entityId)
        {
            if (_others.Remove(entityId, out var other))
            {
                _spawns.Remove(entityId);
                Destroy(other.gameObject);
            }
        }

        /// <summary>
        /// A player's shape in their colors (0xRRGGBB), standing at its parent's origin: the prefab for its mesh,
        /// sized by its scale in content.
        /// </summary>
        public ShapeBody BuildShape(Shape shape, uint color, uint eyeColor, Transform parent)
        {
            var prefab = shape.Mesh switch
            {
                MeshKind.Diamond => diamondPrefab,
                MeshKind.Pyramid => pyramidPrefab,
                _ => cubePrefab,
            };
            var drawn = Instantiate(prefab, parent, false);
            drawn.name = shape.DisplayName;
            drawn.transform.localScale = Vector3.one * shape.Scale;
            drawn.SetColors(color, eyeColor);
            return drawn;
        }

        private static GameObject AddMesh(string name, Mesh mesh, Material material, Transform parent)
        {
            var thing = new GameObject(name);
            thing.transform.SetParent(parent, false);
            thing.AddComponent<MeshFilter>().sharedMesh = mesh;
            thing.AddComponent<MeshRenderer>().sharedMaterial = material;
            return thing;
        }

        /// <summary>A 0xRRGGBB color as Unity's.</summary>
        public static Color Color(uint rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

        /// <summary>A Unity color as 0xRRGGBB.</summary>
        public static uint Rgb(Color color)
        {
            Color32 c = color;
            return (uint)(c.r << 16 | c.g << 8 | c.b);
        }

        private void OnDestroy()
        {
            _join.ContentLoaded -= OnContentLoaded;
            _join.OwnSpawned -= OnOwnSpawned;
            _join.OtherSpawned -= OnOtherSpawned;
            _join.OtherLeft -= OnOtherLeft;
            _join.ConnectionLost -= OnConnectionLost;
            _controls.Dispose();
        }
    }
}
