using System.Collections.Generic;
using System.Linq;
using Comet.Client;
using Comet.Content;
using Comet.Protocol.Messages;
using Comet.Simulation;
using Comet.Unity;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.Messages;
using ShapeLand.Shared.World;
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
    public sealed class GameView : MonoBehaviour
    {
        /// <summary>A layer the main camera doesn't draw, for the join screen's shape preview.</summary>
        public const int PreviewLayer = 31;

        [Tooltip("Terrain's material: Comet/Vertex Color Lit, since its colors are vertex colors.")]
        [SerializeField] private Material terrainMaterial;
        [Tooltip("The material placed objects are drawn with (their models' own are ignored).")]
        [SerializeField] private Material blockMaterial;
        [Tooltip("The material blockout placements are drawn with (Comet/Blockout: a grid over their color).")]
        [SerializeField] private Material blockoutMaterial;
        [Tooltip("The models for prop ids, kept up to date by the editor from the content.")]
        [SerializeField] private PropLibrary props;
        [Tooltip("Players' shapes: their looks' parts other than eyes, colored per part.")]
        [SerializeField] private Material shapeMaterial;
        [Tooltip("Players' eyes (parts with the role eyes).")]
        [SerializeField] private Material eyesMaterial;
        [Tooltip("The transparent material a shape's parts fade with; its color is set from each part's.")]
        [SerializeField] private Material shapeFadeMaterial;
        [Tooltip("The transparent material eyes fade with.")]
        [SerializeField] private Material eyesFadeMaterial;
        [SerializeField] private OrbitCamera orbitCamera;

        private readonly Dictionary<uint, ShapeBody> _others = new Dictionary<uint, ShapeBody>();
        private readonly Dictionary<uint, OtherFade> _otherFades = new Dictionary<uint, OtherFade>();
        private bool _zonePending;
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
        private ShapeLandContent _content;
        private Shape _showcased;
        private JoinClient _join;
        private CollisionWorld _world;
        private Transform _drawnZone;
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

        /// <summary>A player's drawn shape (this one's too), or null.</summary>
        public ShapeBody ShapeOf(uint entityId) =>
            Player != null && entityId == _join.Session?.EntityId ? _playerShape
            : _others.TryGetValue(entityId, out var other) ? other : null;

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
                    var position = Vectors.ToUnity(pose.Position);
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
            DrawZone();
            if (Player != null)
            {
                Player.World = _world;
                orbitCamera.World = _world;
            }
        }

        // Shows or hides the placements with a tag as triggers flip them.
        private void OnFlipped(string tag, bool flipped)
        {
            if (_tagged.TryGetValue(tag, out var things))
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
            _drawnZone = new GameObject(zone.Id).transform;
            _drawnZone.SetParent(transform, false);
            var terrain = new GameObject("Terrain").transform;
            terrain.SetParent(_drawnZone, false);
            foreach (var chunk in TerrainMesher.Build(_world.Terrain, content.Settings.World.GroundColor, content.Settings.World.UnderColor))
            {
                var name = $"Chunk {chunk.Key}";
                var drawn = AddMesh(name, TerrainMeshes.Build(name, chunk.Value), terrainMaterial, terrain);
                drawn.transform.localPosition = Vectors.ToUnity(chunk.Key.Origin);
            }

            var placed = new GameObject("Placed").transform;
            placed.SetParent(_drawnZone, false);
            foreach (var chunk in zone.Chunks)
            {
                // Primitives without a tag are drawn as one mesh per chunk; tagged ones stay apart for game code to find,
                // and blockout ones for their own material.
                var primitives = new PrimitiveMeshes.Builder();
                var toChunk = System.Numerics.Matrix4x4.CreateTranslation(-chunk.Coord.Origin);
                foreach (var placement in chunk.Placements)
                {
                    if (placement.Primitive != null && placement.Tag == null && !placement.Blockout)
                    {
                        primitives.Add(PrimitiveMesher.For(placement, content.Settings.Primitives), placement.Matrix() * toChunk, placement.Color ?? content.Settings.World.PlacedColor);
                        continue;
                    }

                    var thing = placement.Primitive != null ? PlacePrimitive(placement, placed) : Place(placement, placed);
                    if (thing != null && placement.Tag != null)
                    {
                        if (!_tagged.TryGetValue(placement.Tag, out var things))
                        {
                            _tagged.Add(placement.Tag, things = new List<(GameObject, bool)>());
                        }

                        things.Add((thing, placement.Off));
                        thing.SetActive(placement.Off == _join.Flipped.Contains(placement.Tag));
                    }
                }

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

                if (primitives.VertexCount > 0)
                {
                    var name = $"Primitives {chunk.Coord}";
                    var mesh = new Mesh { name = name };
                    primitives.Fill(mesh);
                    AddMesh(name, mesh, terrainMaterial, placed).transform.localPosition = Vectors.ToUnity(chunk.Coord.Origin);
                }
            }

            // Behind the join screen, the camera circles the picked shape's start here, else another shape's or another
            // spawn point.
            var shown = (_showcased != null && _triggers.TryGetSpawn(_showcased.Spawn, out var picked) ? picked : null)
                ?? content.Shapes.All.Select(shape => _triggers.TryGetSpawn(shape.Spawn, out var start) ? start : null).FirstOrDefault(s => s != null)
                ?? _triggers.Spawns.FirstOrDefault();
            orbitCamera.Showcase(shown != null ? Vectors.ToUnity(ShapeLandWorld.Center(shown, _world)) : Vector3.zero);
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
                orbitCamera.Showcase(Vectors.ToUnity(ShapeLandWorld.Center(start, _world)), glide);
            }
        }

        // A spawn point's, teleporter's or trigger's pad, if it has one: drawn on its own, to be colored.
        private void AddPad(object owner, string id, TriggerVolume volume, Transform parent)
        {
            if (volume.Pad is not { } pad)
            {
                return;
            }

            var thing = PlacePrimitive(pad, parent);
            thing.name = id;
            var color = owner is SpawnPoint ? Color(_content.Settings.Spawns.UnsetColor) : Color(pad.Color ?? _content.Settings.World.PlacedColor);
            _pads.Add(new Pad(owner, thing.GetComponentsInChildren<Renderer>(), color));
        }

        private sealed class Pad
        {
            public Pad(object owner, Renderer[] renderers, Color color)
            {
                Owner = owner;
                Renderers = renderers;
                Color = color;
            }

            /// <summary>The spawn point, teleporter or trigger it's under.</summary>
            public object Owner { get; }

            public Renderer[] Renderers { get; }

            /// <summary>Its color at rest (a spawn's while it isn't the player's).</summary>
            public Color Color { get; }
        }

        // A tagged or blockout primitive on its own, in the placed objects' material (or blockout's), so game code can
        // find and color it.
        private GameObject PlacePrimitive(Placement placement, Transform parent)
        {
            var thing = AddMesh(placement.Tag ?? "Blockout", PrimitiveMeshes.For(placement, _content.Settings.Primitives), MaterialFor(placement), parent);
            var rotation = placement.Rotation;
            thing.transform.SetLocalPositionAndRotation(Vectors.ToUnity(placement.Position), Quaternion.Euler(rotation.X, rotation.Y, rotation.Z));
            PlacedColors.Apply(thing, Color(placement.Color ?? _content.Settings.World.PlacedColor));
            return thing;
        }

        // A prop's model where a zone places it, in the placed objects' material; its collision objects are hidden.
        private GameObject Place(Placement placement, Transform parent)
        {
            var model = props.Model(placement.Prop);
            if (model == null)
            {
                Debug.LogWarning($"No model for {placement.Prop} in the prop library; is the content up to date in the editor?");
                return null;
            }

            var thing = Instantiate(model, parent, false);
            thing.name = placement.Prop;
            var rotation = placement.Rotation;
            thing.transform.SetLocalPositionAndRotation(Vectors.ToUnity(placement.Position), Quaternion.Euler(rotation.X, rotation.Y, rotation.Z));
            thing.transform.localScale = Vectors.ToUnity(placement.Scale);
            foreach (var renderer in thing.GetComponentsInChildren<Renderer>())
            {
                if (renderer.name.EndsWith(Prop.CollisionSuffix, System.StringComparison.Ordinal))
                {
                    renderer.enabled = false;
                }
                else
                {
                    renderer.sharedMaterial = MaterialFor(placement);
                }
            }

            var own = placement.Color is { } rgb ? PlacedColors.FromRgb(rgb) : (Color?)null;
            var prop = props.TryGet(placement.Prop, out var entry) && entry.hasColor ? entry.color : (Color?)null;
            PlacedColors.Apply(thing, own ?? prop ?? Color(_content.Settings.World.PlacedColor));
            return thing;
        }

        private Material MaterialFor(Placement placement) => placement.Blockout && blockoutMaterial != null ? blockoutMaterial : blockMaterial;

        // The player's own spawn's pad takes their body color, other spawns' a pale color used nowhere else, and
        // other pads their own. A pad the player stands in charges toward firing (Charging): a spawn's toward their
        // body color, a teleporter's and a trigger's toward their charge color.
        private void ColorPads()
        {
            if (_content == null || _pads.Count == 0)
            {
                return;
            }

            _padColor ??= new MaterialPropertyBlock();
            var now = Time.time;
            var own = Player != null ? _join.SpawnNumber : null;
            var body = Player != null && _join.Spawn is { } me ? Color(me.Color) : Color(_content.Settings.Spawns.UnsetColor);
            if (Player != null)
            {
                var feet = Player.Motor.Position;
                var spawn = _triggers.SpawnAt(feet);
                var teleporter = _triggers.TeleporterAt(feet);
                var trigger = _triggers.All.FirstOrDefault(t => Triggers.Inside(t.Volume, feet));
                _spawnCharge.Step(spawn, spawn?.Volume, now);
                _triggerCharge.Step(trigger, trigger?.Volume, now);
                if (_teleportCharge.Step(teleporter, teleporter?.Volume, now))
                {
                    _sending = teleporter;
                    _padFading = false;
                }

                // The shape starts fading out just before the pad fires, to be gone as it does; stepping off first
                // brings it back.
                if (teleporter != null && _teleportCharge.Inside == teleporter && !_teleportCharge.Fired)
                {
                    var left = (1 - _teleportCharge.Progress(now)) * teleporter.Volume.StandSeconds;
                    if (!_padFading && left <= ShapeFade.FadeOutSeconds)
                    {
                        Player.TeleportSoon(left);
                        _padFading = true;
                    }
                }
                else if (_padFading)
                {
                    Player.TeleportCancelled();
                    _padFading = false;
                }

                if (_sending != null && _teleportCharge.Inside != _sending && !Player.Departing)
                {
                    _sending = null;
                }
            }

            foreach (var pad in _pads)
            {
                Color color;
                if (pad.Owner is SpawnPoint spawn)
                {
                    var charge = _spawnCharge.Inside == spawn ? (_spawnCharge.Fired ? 1 : _spawnCharge.Progress(now)) : 0;
                    color = spawn.Number == own ? body : Charging(pad.Color, body, charge, spawn.Volume.StandSeconds);
                }
                else if (pad.Owner is Teleporter teleporter)
                {
                    // Lit from firing until the player has faded out of it, and the target from arriving until they step off.
                    var charge = teleporter == _sending && Player != null && Player.Departing ? 1
                        : _teleportCharge.Inside != teleporter ? 0
                        : _teleportCharge.Fired ? 1
                        : _teleportCharge.Progress(now);
                    var to = Color(teleporter.ChargeColor ?? _content.Settings.Teleporters.ChargeColor);
                    color = Charging(pad.Color, to, charge, teleporter.Volume.StandSeconds);
                }
                else if (pad.Owner is Trigger trigger)
                {
                    var charge = _triggerCharge.Inside != trigger ? 0 : _triggerCharge.Fired ? 1 : _triggerCharge.Progress(now);
                    var to = Color(trigger.ChargeColor ?? _content.Settings.Triggers.ChargeColor);
                    color = Charging(pad.Color, to, charge, trigger.Volume.StandSeconds);
                }
                else
                {
                    continue;
                }

                _padColor.SetColor(BaseColor, color);
                foreach (var renderer in pad.Renderers)
                {
                    renderer.SetPropertyBlock(_padColor);
                }
            }
        }

        // Arriving somewhere (a spawn, a teleporter's target) counts as already in what's there: it charges only after a step out and in.
        private void OnCorrected(PositionCorrection correction)
        {
            if (_triggers == null)
            {
                return;
            }

            var feet = new System.Numerics.Vector3(correction.X, correction.Y, correction.Z);
            if (_triggers.SpawnAt(feet) is { } spawn)
            {
                _spawnCharge.Arrived(spawn, spawn.Volume);
            }

            if (_triggers.TeleporterAt(feet) is { } teleporter)
            {
                _teleportCharge.Arrived(teleporter, teleporter.Volume);
            }
        }

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        // Every pad charges alike: from its resting color straight toward the color it charges to, over its stand time
        // (charge 0 to 1, or 1 once fired), blinking back to its resting color once, BlinkSeconds long, just before it
        // fires, so the moment shows on any color. A pad that fires at once has no time to blink.
        private const float BlinkSeconds = 0.1f;

        private static Color Charging(Color rest, Color to, float charge, float standSeconds)
        {
            var left = (1 - charge) * standSeconds;
            return charge > 0 && left > BlinkSeconds && left <= 2 * BlinkSeconds ? rest : UnityEngine.Color.Lerp(rest, to, charge);
        }

        // On joining, the player and their shape; on a change of look, a new shape where they stand, so the motor and
        // the camera carry on as they were (the server's snap-back has already put the player where it has them).
        private void OnOwnSpawned(PlayerSpawn spawn)
        {
            WarmUpFades();
            _spawns[spawn.EntityId] = spawn;
            var shape = _join.Content.Shapes[spawn.Shape];
            if (Player == null)
            {
                Player = new GameObject().AddComponent<LocalPlayer>();
                Player.Begin(_join.Session, _world, _join.Content.Movement, shape, new Vector3(spawn.X, spawn.Y, spawn.Z), spawn.Facing, orbitCamera, _controls);
                orbitCamera.Follow(Player.transform, _controls, _world);
                _join.Session.Corrected += OnCorrected;
                OnCorrected(new PositionCorrection { X = spawn.X, Y = spawn.Y, Z = spawn.Z }); // joining on a spawn doesn't claim it
            }
            else
            {
                Destroy(_playerShape.gameObject);
                Player.ChangeShape(shape);
            }

            Player.name = $"{spawn.Name} (you)";
            _playerShape = BuildShape(shape, spawn.Color, spawn.EyeColor, Player.transform);
        }

        // Draws the fade materials once, out of sight, so their shaders are ready (compiled) before a shape first fades,
        // rather than showing the editor's placeholder or a hitch then.
        private void WarmUpFades()
        {
            _warmUpMesh ??= new Mesh { name = "Fade warm-up", vertices = new[] { Vector3.zero, Vector3.right * 0.001f, Vector3.up * 0.001f }, triangles = new[] { 0, 1, 2 } };
            var place = Matrix4x4.Translate(Camera.transform.position + Camera.transform.forward * (Camera.nearClipPlane * 2));
            foreach (var material in new[] { shapeFadeMaterial, eyesFadeMaterial })
            {
                if (material != null)
                {
                    Graphics.RenderMesh(new RenderParams(material) { camera = Camera }, _warmUpMesh, 0, place);
                }
            }
        }

        // A player coming into view fades in; one already in view (a new look) is swapped at once.
        private void OnOtherSpawned(PlayerSpawn spawn)
        {
            var track = _otherFades.TryGetValue(spawn.EntityId, out var known) ? known : new OtherFade();
            if (known == null)
            {
                track.Fade.StartHidden();
            }

            if (_others.Remove(spawn.EntityId, out var old))
            {
                Destroy(old.gameObject);
            }

            _spawns[spawn.EntityId] = spawn;
            var other = BuildShape(_join.Content.Shapes[spawn.Shape], spawn.Color, spawn.EyeColor, transform);
            other.name = $"{spawn.Name} ({spawn.EntityId})";
            var position = new Vector3(spawn.X, spawn.Y, spawn.Z);
            other.transform.SetPositionAndRotation(position, Quaternion.Euler(0, spawn.Facing * Mathf.Rad2Deg, 0));
            other.Faded = track.Fade.Faded;
            track.Last = position;
            _others[spawn.EntityId] = other;
            _otherFades[spawn.EntityId] = track;
        }

        // Everyone leaves the world, this player too, and the camera goes back to orbiting the island.
        private void OnConnectionLost(CloseReason reason)
        {
            foreach (var other in _others.Values)
            {
                Destroy(other.gameObject);
            }

            foreach (var (shape, _) in _leaving)
            {
                Destroy(shape.gameObject);
            }

            _others.Clear();
            _otherFades.Clear();
            _leaving.Clear();

            if (Player != null)
            {
                orbitCamera.Release();
                Destroy(Player.gameObject);
                Player = null;
                _playerShape = null;
            }

            _spawns.Clear(); // this player's own entry too, or a rejoin would tag someone with the old name
        }

        // A player leaving view fades out where they were last drawn.
        private void OnOtherLeft(uint entityId)
        {
            if (_others.Remove(entityId, out var other))
            {
                _spawns.Remove(entityId);
                var fade = _otherFades.Remove(entityId, out var track) ? track.Fade : new ShapeFade();
                fade.Depart(Time.unscaledTime);
                _leaving.Add((other, fade));
            }
        }

        /// <summary>
        /// A player's shape in their colors (0xRRGGBB), standing at its parent's origin: its look (a group of
        /// primitives) at its scale and hover in content.
        /// </summary>
        public ShapeBody BuildShape(Shape shape, uint color, uint eyeColor, Transform parent) =>
            BuildShape(_content, shape, color, eyeColor, parent, shapeMaterial, eyesMaterial, shapeFadeMaterial, eyesFadeMaterial);

        /// <summary><see cref="BuildShape(Shape, uint, uint, Transform)"/> with its content and materials given, as tests build shapes.</summary>
        public static ShapeBody BuildShape(ShapeLandContent content, Shape shape, uint color, uint eyeColor, Transform parent,
            Material shapeMaterial, Material eyesMaterial, Material shapeFadeMaterial, Material eyesFadeMaterial)
        {
            var drawn = ShapeBody.Create(shape.DisplayName, parent, shape.Scale, shape.Hover);
            foreach (var part in Groups.Expand(new Placement { Group = shape.Look }, id => content.Groups.TryGet(id, out var group) ? group : null))
            {
                if (part.Primitive == null)
                {
                    continue; // looks are built from primitives
                }

                var r = part.Rotation;
                var eyes = part.Role == "eyes";
                drawn.AddPart(PrimitiveMeshes.For(part, content.Settings.Primitives), Vectors.ToUnity(part.Position), Quaternion.Euler(r.X, r.Y, r.Z), part.Role,
                    Color(part.Color ?? content.Settings.World.PlacedColor), eyes ? eyesMaterial : shapeMaterial, eyes ? eyesFadeMaterial : shapeFadeMaterial);
            }

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
