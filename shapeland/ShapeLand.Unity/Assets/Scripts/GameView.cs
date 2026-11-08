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
    /// <summary>
    /// The game scene's view: draws the zone (its terrain, placed objects and pads) once the content has loaded, and
    /// again for each zone the player goes to, showing placements as triggers flip them; the player's own shape
    /// (with eyes, to show its facing) once the server spawns it, with the orbit camera following, and the other
    /// players where the interpolation buffer puts them. Shapes fade out as they fall off the island, before a
    /// teleport and as others leave, and back in where they arrive (<see cref="ShapeFade"/>): another's jump in
    /// place holds them where they stood while they fade out.
    /// </summary>
    [RequireComponent(typeof(JoinClient))]
    public sealed partial class GameView : MonoBehaviour
    {
        /// <summary>A layer the main camera doesn't draw, for the join screen's shape preview.</summary>
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
        private PositionCorrection? _pendingArrival; // where the player arrived in a zone not drawn yet
        private Mesh _warmUpMesh;
        private bool _padFading;
        private readonly List<(ShapeBody Shape, ShapeFade Fade)> _leaving = new List<(ShapeBody, ShapeFade)>();

        // How another player's shape fades: where it was last drawn from the buffer, to see it jump, and where it's
        // held while fading out after one.
        private sealed class OtherFade
        {
            public readonly ShapeFade Fade = new ShapeFade();
            public Vector3 Last;
            public Vector3 Held;
        }

        private readonly Dictionary<uint, PlayerSpawn> _spawns = new Dictionary<uint, PlayerSpawn>();

        // The drawn zone's pads, and its tagged placements by tag (with whether each starts off), for flipping.
        private readonly List<Pad> _pads = new List<Pad>();
        private readonly Dictionary<string, List<(GameObject Thing, bool StartsOff)>> _tagged = new Dictionary<string, List<(GameObject, bool)>>();

        // The player standing in spawn points, teleporters and triggers, timed as the server times them, to draw pads charging.
        private readonly TriggerCharge _spawnCharge = new TriggerCharge();
        private readonly TriggerCharge _teleportCharge = new TriggerCharge();
        private readonly TriggerCharge _triggerCharge = new TriggerCharge();

        // The teleporter about to send the player, so only its pad (not the one they arrive on) holds its color.
        private Teleporter _sending;
        private MaterialPropertyBlock _padColor;
        private Triggers _triggers;

        // Every teleporter in the content, so one whose target isn't there yet doesn't charge: the server won't send it.
        private HashSet<string> _teleporterIds;
        private ShapeLandContent _content;
        private Shape _showcased;
        private JoinClient _join;
        private CollisionWorld _world;
        private Transform _drawnZone;
        private ZoneDrawing _drawing;
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

        public OrbitCamera OrbitCamera => orbitCamera;

        /// <summary>Whether the player's own shape shows where something hides it (V); on unless turned off, and remembered.</summary>
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

        /// <summary>A player's drawn shape (this one's too), or null.</summary>
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

        // Others are drawn where the interpolation buffer puts them, a little behind the newest states.
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

        // Another zone is shown (another shape picked, or the player went there): draw that instead, before the
        // player arrives in it.
        private void OnZoneChanged(Zone zone)
        {
            if (_drawnZone == null)
            {
                return; // the content just loaded: OnContentLoaded draws the zone shown by then
            }

            // The player's shape is still fading out where it stood: the old zone stays drawn around it until it's gone.
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
            ClearLeaving(); // others from the old zone would fade out in the new one
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

        // Shows or hides the placements with a tag as triggers flip them.
        private void OnFlipped(string tag, bool flipped)
        {
            // While the old zone stays drawn, flips are the new zone's: DrawZone shows them.
            if (!_zonePending && _tagged.TryGetValue(tag, out var things))
            {
                foreach (var (thing, startsOff) in things)
                {
                    thing.SetActive(startsOff == flipped);
                }
            }

            Player?.Flipped(tag);
        }

        // Draws the join client's zone: its terrain, placed objects and pads.
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

            // Behind the join screen, the camera circles the picked shape's start here, else another shape's or another
            // spawn point.
            var shown =
                (_showcased != null && _triggers.TryGetSpawn(_showcased.Spawn, out var picked) ? picked : null)
                ?? content.Shapes.All.Select(shape => _triggers.TryGetSpawn(shape.Spawn, out var start) ? start : null).FirstOrDefault(s => s != null)
                ?? _triggers.Spawns.FirstOrDefault();
            orbitCamera.Showcase(shown != null ? Conversions.ToUnity(Triggers.Center(shown, _world)) : Vector3.zero);
        }

        /// <summary>
        /// Behind the join screen, circles <paramref name="shape"/>'s start: at once if it's in the zone drawn (gliding
        /// there if <paramref name="glide"/>), else once its zone is drawn.
        /// </summary>
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
