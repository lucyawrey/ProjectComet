using System.Collections.Generic;
using System.Linq;
using Comet.Content;
using Comet.Protocol.Messages;
using Comet.Simulation;
using Comet.Unity;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using UnityEngine;

namespace ShapeLand.Client
{
    [RequireComponent(typeof(JoinClient))]
    public sealed partial class GameView : MonoBehaviour
    {
        public const int PreviewLayer = 31;

        [Tooltip("Terrain's material: Comet/Vertex Color Lit, since its colors are vertex colors.")]
        [SerializeField]
        private Material terrainMaterial;

        [Tooltip("The material placed objects are drawn with (their models' own are ignored).")]
        [SerializeField]
        private Material blockMaterial;

        [Tooltip("The material blockout placements are drawn with (Comet/Blockout: a grid over their color).")]
        [SerializeField]
        private Material blockoutMaterial;

        [Tooltip("The models for prop ids, kept up to date by the editor from the content.")]
        [SerializeField]
        private PropLibrary props;

        [Tooltip("Players' shapes: their looks' parts other than eyes, colored per part.")]
        [SerializeField]
        private Material shapeMaterial;

        [Tooltip("Players' eyes (parts with the role eyes).")]
        [SerializeField]
        private Material eyesMaterial;

        [Tooltip("The transparent material a shape's parts fade with; its color is set from each part's.")]
        [SerializeField]
        private Material shapeFadeMaterial;

        [Tooltip("The transparent material eyes fade with.")]
        [SerializeField]
        private Material eyesFadeMaterial;

        [Tooltip("Marks where the player's own shape is in sight, before its silhouette draws (Comet's Silhouette shader).")]
        [SerializeField]
        private Material shapeMaskMaterial;

        [Tooltip("The player's own shape where blocks, props or terrain hide it; its color is set from the body's, its alpha is the opacity.")]
        [SerializeField]
        private Material shapeSilhouetteMaterial;

        [SerializeField]
        private OrbitCamera orbitCamera;

        private readonly Dictionary<uint, ShapeBody> _others = new Dictionary<uint, ShapeBody>();
        private readonly Dictionary<uint, OtherFade> _otherFades = new Dictionary<uint, OtherFade>();
        private bool _zonePending;
        private PositionCorrection? _pendingArrival;
        private Mesh _warmUpMesh;
        private bool _padFading;
        private readonly List<(ShapeBody Shape, ShapeFade Fade)> _leaving = new List<(ShapeBody, ShapeFade)>();

        private sealed class OtherFade
        {
            public readonly ShapeFade Fade = new ShapeFade();
            public Vector3 Last;
            public Vector3 Held;
        }

        private readonly Dictionary<uint, PlayerSpawn> _spawns = new Dictionary<uint, PlayerSpawn>();

        private readonly List<Pad> _pads = new List<Pad>();
        private readonly Dictionary<string, List<(GameObject Thing, bool StartsOff)>> _tagged = new Dictionary<string, List<(GameObject, bool)>>();

        private readonly TriggerCharge _spawnCharge = new TriggerCharge();
        private readonly TriggerCharge _teleportCharge = new TriggerCharge();
        private readonly TriggerCharge _triggerCharge = new TriggerCharge();

        private Teleporter _sending;
        private MaterialPropertyBlock _padColor;
        private Triggers _triggers;

        private HashSet<string> _teleporterIds;
        private ShapeLandContent _content;
        private Shape _showcased;
        private JoinClient _join;
        private CollisionWorld _world;
        private Transform _drawnZone;
        private ZoneDrawing _drawing;
        private ShapeLandControls _controls;
        private ShapeBody _playerShape;

        public LocalPlayer Player { get; private set; }

        public IReadOnlyDictionary<uint, ShapeBody> Others => _others;

        public IReadOnlyDictionary<uint, PlayerSpawn> Spawns => _spawns;

        /// <summary>The same as a concrete dictionary, so looping over it every frame doesn't box an enumerator.</summary>
        internal Dictionary<uint, PlayerSpawn> SpawnTable => _spawns;

        public ShapeLandControls Controls => _controls;

        public Camera Camera => orbitCamera.GetComponent<Camera>();

        public OrbitCamera OrbitCamera => orbitCamera;

        public bool ShowThroughWalls
        {
            get => GameSettings.ShowThroughWalls;
            set
            {
                GameSettings.ShowThroughWalls = value;
                ApplyShowThroughWalls();
            }
        }

        private void ApplyShowThroughWalls()
        {
            if (_playerShape != null)
            {
                var shown = ShowThroughWalls;
                _playerShape.ShowThroughWalls(shown ? shapeMaskMaterial : null, shown ? shapeSilhouetteMaterial : null);
            }
        }

        public ShapeBody ShapeOf(uint entityId) =>
            Player != null && entityId == _join.Session?.EntityId ? _playerShape
            : _others.TryGetValue(entityId, out var other) ? other
            : null;

        private void Awake()
        {
            _join = GetComponent<JoinClient>();
            _join.ContentLoaded += OnContentLoaded;
            _join.ZoneChanged += OnZoneChanged;
            _join.PlacementsFlipped += OnFlipped;
            _join.OwnSpawned += OnOwnSpawned;
            _join.OtherSpawned += OnOtherSpawned;
            _join.OtherLeft += OnOtherLeft;
            _join.ConnectionLost += OnConnectionLost;
            _controls = new ShapeLandControls();
            _controls.Enable();
            Camera.cullingMask &= ~(1 << PreviewLayer);
        }

        private void Update()
        {
            if (_zonePending && (Player == null || !Player.Departing))
            {
                OnZoneChanged(_join.Zone);
            }

            ColorPads();
            if (Player != null && _playerShape != null)
            {
                _playerShape.Faded = Player.Faded;
            }

            var now = Time.unscaledTime;
            var seconds = Time.unscaledDeltaTime;
            for (var i = _leaving.Count - 1; i >= 0; i--)
            {
                var (shape, fade) = _leaving[i];
                fade.Step(now, seconds, shape.transform.position.y, _world.KillHeight);
                shape.Faded = fade.Faded;
                if (fade.Faded >= 1)
                {
                    Destroy(shape.gameObject);
                    _leaving.RemoveAt(i);
                }
            }

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
                    var track = _otherFades[other.Key];
                    var position = Conversions.ToUnity(pose.Position);
                    if (Vector3.Distance(position, track.Last) > session.Entities.TeleportSpeed * seconds + 1)
                    {
                        if (!track.Fade.Holding)
                        {
                            track.Held = shape.transform.position;
                        }

                        track.Fade.Moved(now);
                    }

                    track.Last = position;
                    track.Fade.Step(now, seconds, track.Fade.Holding ? track.Held.y : position.y, _world.KillHeight);
                    if (!track.Fade.Holding)
                    {
                        shape.transform.SetPositionAndRotation(position, Quaternion.Euler(0, pose.Facing * Mathf.Rad2Deg, 0));
                    }

                    shape.Faded = track.Fade.Faded;
                }
            }
        }

        private void OnContentLoaded(ShapeLandContent content)
        {
            _content = content;
            var world = content.Settings.World;
            _drawing = new ZoneDrawing(
                terrainMaterial,
                blockMaterial,
                blockoutMaterial,
                props,
                content.Settings.Primitives,
                world.PlacedColor,
                world.GroundColor,
                world.UnderColor
            );
            DrawZone();
        }

        private void OnZoneChanged(Zone zone)
        {
            if (_drawnZone == null)
            {
                return;
            }

            if (Player != null && Player.Departing)
            {
                Player.World = _join.World;
                _zonePending = true;
                return;
            }

            _zonePending = false;

            Destroy(_drawnZone.gameObject);
            _pads.Clear();
            _tagged.Clear();
            _spawnCharge.Clear();
            _teleportCharge.Clear();
            _triggerCharge.Clear();
            _sending = null;
            ClearLeaving();
            DrawZone();
            if (Player != null)
            {
                Player.World = _world;
                orbitCamera.World = _world;
            }

            if (_pendingArrival is { } arrival)
            {
                _pendingArrival = null;
                OnCorrected(arrival);
            }
        }

        private void OnFlipped(string tag, bool flipped)
        {
            if (!_zonePending && _tagged.TryGetValue(tag, out var things))
            {
                foreach (var (thing, startsOff) in things)
                {
                    thing.SetActive(startsOff == flipped);
                }
            }

            Player?.Flipped(tag);
        }

        private void DrawZone()
        {
            var content = _content;
            var zone = _join.Zone;
            _world = _join.World;
            _triggers = new Triggers(zone);
            _teleporterIds = content.Zones.All.SelectMany(z => z.Chunks).SelectMany(c => c.Teleporters).Select(t => t.Id).ToHashSet();
            _drawnZone = _drawing.Draw(
                zone,
                _world,
                transform,
                (placement, thing) =>
                {
                    if (!_tagged.TryGetValue(placement.Tag, out var things))
                    {
                        _tagged.Add(placement.Tag, things = new List<(GameObject, bool)>());
                    }

                    things.Add((thing, placement.Off));
                    thing.SetActive(placement.Off == _join.Flipped.Contains(placement.Tag));
                },
                out var placed
            );
            foreach (var chunk in zone.Chunks)
            {
                foreach (var spawn in chunk.Spawns)
                {
                    AddPad(spawn, spawn.Id, spawn.Volume, placed);
                }

                foreach (var teleporter in chunk.Teleporters)
                {
                    AddPad(teleporter, teleporter.Id, teleporter.Volume, placed);
                }

                foreach (var trigger in chunk.Triggers)
                {
                    AddPad(trigger, trigger.Id, trigger.Volume, placed);
                }
            }

            var shown =
                (_showcased != null && _triggers.TryGetSpawn(_showcased.Spawn, out var picked) ? picked : null)
                ?? content.Shapes.All.Select(shape => _triggers.TryGetSpawn(shape.Spawn, out var start) ? start : null).FirstOrDefault(s => s != null)
                ?? _triggers.Spawns.FirstOrDefault();
            orbitCamera.Showcase(shown != null ? Conversions.ToUnity(Triggers.Center(shown, _world)) : Vector3.zero);
        }

        public void Showcase(Shape shape, bool glide)
        {
            _showcased = shape;
            if (_triggers != null && _triggers.TryGetSpawn(shape.Spawn, out var start))
            {
                orbitCamera.Showcase(Conversions.ToUnity(Triggers.Center(start, _world)), glide);
            }
        }

        private void OnDestroy()
        {
            if (_warmUpMesh != null)
            {
                Destroy(_warmUpMesh);
            }

            _join.ContentLoaded -= OnContentLoaded;
            _join.ZoneChanged -= OnZoneChanged;
            _join.PlacementsFlipped -= OnFlipped;
            _join.OwnSpawned -= OnOwnSpawned;
            _join.OtherSpawned -= OnOtherSpawned;
            _join.OtherLeft -= OnOtherLeft;
            _join.ConnectionLost -= OnConnectionLost;
            _controls.Dispose();
        }
    }
}
