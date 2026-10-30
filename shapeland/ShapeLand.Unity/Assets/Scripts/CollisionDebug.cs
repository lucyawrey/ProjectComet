using Comet.Content;
using Comet.Protocol.Messages;
using Comet.Simulation;
using Comet.Unity;
using ShapeLand.Shared.World;
using UnityEngine;

namespace ShapeLand.Client
{
    /// <summary>
    /// F4 shows or hides Comet's <see cref="CollisionView"/> of the zone: what movement collides with, the volumes of
    /// spawn points, teleporters and triggers (with the way arrivals face), and every player's body. A snap-back for a
    /// broken movement rule leaves a red body where the player was and says which rule in the chat log. The choice is
    /// remembered.
    /// </summary>
    [RequireComponent(typeof(Hud), typeof(Chat), typeof(CollisionView))]
    public sealed class CollisionDebug : MonoBehaviour
    {
        private const string ShownKey = "collisionViewShown";
        private const float MarkSeconds = 4;

        // Comet's colors (terrain, volumes, bodies), and ShapeLand's for its kinds of mesh, muted alike. Changes in
        // play mode redraw the view.
        [SerializeField] private CollisionColors colors = null!;
        [SerializeField] private Color propColor = new Color(0.75f, 0.75f, 0.75f, 0.45f);
        [SerializeField] private Color primitiveColor = new Color(0.7f, 0.6f, 1, 0.45f);

        private Hud _hud;
        private Chat _chat;
        private CollisionView _view;
        private CollisionWorld _shownWorld;
        private Comet.Client.ClientSession _session;
        private Vector3 _lastFeet;

        /// <summary>Whether showing or hiding is remembered (PlayerPrefs); off in tests.</summary>
        public bool Remember { get; set; } = true;

        private void OnEnable()
        {
            _hud = GetComponent<Hud>();
            _chat = GetComponent<Chat>();
            _view = GetComponent<CollisionView>();
            colors.Changed += Redraw;
        }

        // In Start, after the view's Awake: components on one object wake one at a time, in no set order.
        private void Start() => _view.Shown = Remember && PlayerPrefs.GetInt(ShownKey, 0) == 1;

        // Draws the zone again with the new colors.
        private void OnValidate() => Redraw();

        private void Redraw() => _shownWorld = null;

        /// <summary>True while the view is showing.</summary>
        public bool Shown => _view != null && _view.Shown;

        /// <summary>Shows or hides the view, and remembers the choice.</summary>
        public void Toggle()
        {
            _view.Shown = !_view.Shown;
            if (Remember)
            {
                PlayerPrefs.SetInt(ShownKey, _view.Shown ? 1 : 0);
            }
        }

        private void Update()
        {
            var view = _hud.View;
            if (view.Controls.Game.CollisionView.WasPressedThisFrame() && !_chat.Open)
            {
                Toggle();
            }

            var join = _hud.Join;
            if (join.World != _shownWorld && join.World != null)
            {
                _shownWorld = join.World;
                ShowZone(join.Zone, join.World, join.Content.Settings.Primitives);
            }

            var session = join.Session;
            if (session != _session)
            {
                if (_session != null)
                {
                    _session.Corrected -= OnCorrected;
                }

                _session = session;
                if (_session != null)
                {
                    _session.Corrected += OnCorrected;
                }
            }
        }

        // Bodies are drawn where the shapes are drawn, after they've moved this frame.
        private void LateUpdate()
        {
            var view = _hud.View;
            var rules = _hud.Join.Content?.Movement;
            if (rules == null)
            {
                return;
            }

            if (view.Player != null)
            {
                _lastFeet = view.Player.transform.position;
                _view.DrawBody(_lastFeet, rules.BodyRadius, rules.BodyHeight, colors.body);
            }

            foreach (var other in view.Others.Values)
            {
                _view.DrawBody(other.transform.position, rules.BodyRadius, rules.BodyHeight, colors.body);
            }
        }

        private void ShowZone(Zone zone, CollisionWorld world, PolygonBudget budget)
        {
            _view.Show(world, ColorOf, colors.terrain);
            foreach (var chunk in zone.Chunks)
            {
                foreach (var spawn in chunk.Spawns)
                {
                    _view.AddVolume(spawn.Volume, budget, colors.spawn, spawn.Facing == SpawnFacing.Direction);
                }

                foreach (var teleporter in chunk.Teleporters)
                {
                    _view.AddVolume(teleporter.Volume, budget, colors.teleporter, teleporter.Facing == SpawnFacing.Direction);
                }

                foreach (var trigger in chunk.Triggers)
                {
                    _view.AddVolume(trigger.Volume, budget, colors.trigger);
                }
            }
        }

        private Color ColorOf(CollisionMesh mesh) => mesh.Kind == ShapeLandWorld.PropKind ? propColor : primitiveColor;

        private void OnCorrected(PositionCorrection correction)
        {
            if (correction.Reason != CorrectionReason.SnapBack || correction.Verdict == MovementVerdict.Accepted)
            {
                return;
            }

            var rules = _hud.Join.Content.Movement;
            _view.Mark(_lastFeet, rules.BodyRadius, rules.BodyHeight, colors.snapBack, MarkSeconds);
            if (_view.Shown)
            {
                var strings = _hud.Join.Content.Strings;
                _chat.Note(strings.Format("collision.snap_back", ("rule", strings.Get("collision." + Key(correction.Verdict)))));
            }
        }

        private static string Key(MovementVerdict verdict) => verdict switch
        {
            MovementVerdict.NotFinite => "not_finite",
            MovementVerdict.TooFast => "too_fast",
            MovementVerdict.TooHigh => "too_high",
            _ => "inside_solid",
        };

        private void OnDisable()
        {
            colors.Changed -= Redraw;
            if (_session != null)
            {
                _session.Corrected -= OnCorrected;
                _session = null;
            }
        }
    }
}
