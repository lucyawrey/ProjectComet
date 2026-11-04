using System.Collections.Generic;
using Comet.Content;
using ShapeLand.Shared.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    /// <summary>
    /// The signs of the zone shown: each one's text over its point, sized in the world (smaller further away) or one
    /// size on screen like a name tag, while it's in range and in sight of the camera. Behind the name tags and
    /// bubbles, in the same layer.
    /// </summary>
    [DefaultExecutionOrder(100)] // after the camera has moved
    [RequireComponent(typeof(Hud))]
    public sealed class Signs : MonoBehaviour
    {
        // Sight checks each frame, taken in turn by the signs in range, so many signs stay cheap.
        private const int ChecksPerFrame = 8;
        private const float FadeSeconds = 0.15f;

        // The sight line starts a little in front of the camera (one brushing a wall still sees) and stops short of
        // the sign (one on a wall or post isn't hidden by it), in meters.
        private const float CameraGap = 0.1f, SignGap = 0.3f;

        // World-sized signs are laid out at a screen-sized sign's default font size, then scaled as a whole (padding
        // and arrow too) to their text height at their distance. The arrow's height is the USS's .sign-arrow's.
        private const float WorldLayoutFontSize = 13, ArrowHeight = 7, MaxWorldScale = 20;

        private readonly List<Shown> _signs = new List<Shown>();
        private readonly HashSet<int> _walls = new HashSet<int>();
        private Hud _hud;
        private VisualElement _layer;
        private int _next;

        private void OnEnable()
        {
            _hud = GetComponent<Hud>();
            _layer = GetComponent<UIDocument>().rootVisualElement.Q("tags");
            _hud.Join.ZoneChanged += OnZoneChanged;
            if (_hud.Join.Zone != null)
            {
                OnZoneChanged(_hud.Join.Zone);
            }
        }

        private void OnDisable()
        {
            _hud.Join.ZoneChanged -= OnZoneChanged;
            Clear();
        }

        private void Clear()
        {
            foreach (var sign in _signs)
            {
                sign.Panel.RemoveFromHierarchy();
            }

            _signs.Clear();
        }

        private void OnZoneChanged(Zone zone)
        {
            Clear();
            foreach (var chunk in zone.Chunks)
            {
                foreach (var (point, sign) in chunk.AllSigns())
                {
                    var panel = new VisualElement { pickingMode = PickingMode.Ignore };
                    panel.AddToClassList("sign");
                    var text = new Label(sign.Text) { pickingMode = PickingMode.Ignore, enableRichText = false };
                    text.style.color = GameView.Color(sign.TextColor);
                    text.style.fontSize = sign.ScreenSized ? sign.FontSize : WorldLayoutFontSize;
                    panel.Add(text);
                    var back = GameView.Color(sign.BackgroundColor);
                    back.a = sign.Background ? sign.BackgroundOpacity : 0;
                    panel.style.backgroundColor = back;
                    if (sign.Arrow && sign.Background)
                    {
                        // Over the point, with the arrow's tip on it, as a speech bubble sits over its speaker.
                        panel.AddToClassList("with-arrow");
                        panel.Add(new PanelArrow(back, "sign-arrow"));
                    }

                    panel.style.display = DisplayStyle.None;
                    _layer.Insert(0, panel); // first, so the name tags and bubbles draw over it
                    _signs.Add(new Shown(sign, point, panel) { HasArrow = sign.Arrow && sign.Background });
                }
            }

            // Invisible walls don't hide signs.
            _walls.Clear();
            var meshes = _hud.Join.World.Meshes;
            for (var m = 0; m < meshes.Count; m++)
            {
                if (meshes[m].Kind == ShapeLandWorld.WallKind)
                {
                    _walls.Add(m);
                }
            }
        }

        private void LateUpdate()
        {
            var panel = _layer.panel;
            if (!_hud.Shown || panel == null || _signs.Count == 0)
            {
                return;
            }

            var camera = _hud.View.Camera;
            var eye = camera.transform.position;
            var checks = ChecksPerFrame;
            var start = _next;
            for (var i = 0; i < _signs.Count; i++)
            {
                var index = (start + i) % _signs.Count;
                var shown = _signs[index];
                var point = shown.Point;
                var screen = camera.WorldToScreenPoint(point);
                var inRange = screen.z > 0 && Vector3.Distance(eye, point) <= shown.Sign.ShowDistance;
                if (!inRange)
                {
                    shown.Checked = false; // one coming back into range waits for a fresh check
                }
                else if (checks > 0)
                {
                    checks--;
                    _next = (index + 1) % _signs.Count; // the next frame starts after the last one checked
                    shown.Checked = true;
                    shown.InSight = InSight(eye, point);
                }

                var target = inRange && shown.Checked && shown.InSight ? 1 : 0;
                // Behind the camera it would draw mirrored, so it goes at once instead of fading.
                shown.Opacity = screen.z <= 0 ? 0 : Mathf.MoveTowards(shown.Opacity, target, Time.unscaledDeltaTime / FadeSeconds);
                var label = shown.Panel;
                label.style.display = shown.Opacity > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                if (shown.Opacity > 0)
                {
                    var at = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
                    label.style.left = at.x;
                    label.style.top = at.y;
                    label.style.opacity = shown.Opacity;
                    if (!shown.Sign.ScreenSized)
                    {
                        WorldSize(shown, label, panel, camera, screen.z);
                    }
                }
            }
        }

        // Scales a world-sized sign's panel to its text height at depth meters, about its point.
        private static void WorldSize(Shown shown, VisualElement label, IPanel panel, Camera camera, float depth)
        {
            var panelHigh = panel.visualTree.layout.height;
            var perMeter = camera.orthographic
                ? panelHigh / (2 * camera.orthographicSize)
                : panelHigh / (2 * depth * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad / 2));
            var scale = Mathf.Min(shown.Sign.TextHeight * perMeter / WorldLayoutFontSize, MaxWorldScale);
            label.style.scale = new Scale(new Vector3(scale, scale, 1));
            if (shown.HasArrow && !float.IsNaN(label.layout.height)) // NaN until its first layout
            {
                // About the arrow's tip, which is on the point; without an arrow, the panel's middle is (the default).
                label.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Pixels(label.layout.height + ArrowHeight));
            }
        }

        private bool InSight(Vector3 eye, Vector3 point)
        {
            var line = point - eye;
            var length = line.magnitude;
            if (length <= CameraGap + SignGap)
            {
                return true;
            }

            var along = line / length;
            return !_hud.Join.World.IsLineBlocked(Vectors.ToNumerics(eye + along * CameraGap), Vectors.ToNumerics(point - along * SignGap), _walls);
        }

        private sealed class Shown
        {
            public Shown(Sign sign, System.Numerics.Vector3 point, VisualElement panel)
            {
                Sign = sign;
                Point = Vectors.ToUnity(point);
                Panel = panel;
            }

            public Sign Sign { get; }

            /// <summary>The world point it's shown over (a trigger's sign has its position from its volume).</summary>
            public Vector3 Point { get; }
            public VisualElement Panel { get; }
            public bool HasArrow { get; set; }
            public bool Checked { get; set; }
            public bool InSight { get; set; }
            public float Opacity { get; set; }
        }
    }
}
