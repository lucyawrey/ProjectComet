using System.Collections.Generic;
using Comet.Content;
using ShapeLand.Shared.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    /// <summary>
    /// The signs of the zone shown: each one's text over its point, one readable size at any distance, while it's in
    /// range and in sight of the camera. Behind the name tags and bubbles, in the same layer.
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
                foreach (var sign in chunk.Signs)
                {
                    var panel = new VisualElement { pickingMode = PickingMode.Ignore };
                    panel.AddToClassList("sign");
                    var text = new Label(sign.Text) { pickingMode = PickingMode.Ignore, enableRichText = false };
                    text.style.color = GameView.Color(sign.TextColor);
                    text.style.fontSize = sign.FontSize;
                    panel.Add(text);
                    var back = sign.BackgroundColor is { } rgb ? GameView.Color(rgb) : Color.clear;
                    back.a = sign.BackgroundColor != null ? sign.BackgroundOpacity : 0;
                    panel.style.backgroundColor = back;
                    if (sign.Arrow && sign.BackgroundColor != null)
                    {
                        // Over the point, with the arrow's tip on it, as a speech bubble sits over its speaker.
                        panel.AddToClassList("with-arrow");
                        panel.Add(new Arrow(back));
                    }

                    panel.style.display = DisplayStyle.None;
                    _layer.Insert(0, panel); // first, so the name tags and bubbles draw over it
                    _signs.Add(new Shown(sign, panel));
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
                var point = Vectors.ToUnity(shown.Sign.Position);
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
                shown.Opacity = Mathf.MoveTowards(shown.Opacity, target, Time.unscaledDeltaTime / FadeSeconds);
                var label = shown.Panel;
                label.style.display = shown.Opacity > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                if (shown.Opacity > 0)
                {
                    var at = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
                    label.style.left = at.x;
                    label.style.top = at.y;
                    label.style.opacity = shown.Opacity;
                }
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
            public Shown(Sign sign, VisualElement panel)
            {
                Sign = sign;
                Panel = panel;
            }

            public Sign Sign { get; }
            public VisualElement Panel { get; }
            public bool Checked { get; set; }
            public bool InSight { get; set; }
            public float Opacity { get; set; }
        }

        // A little triangle under a sign's panel, in its background color, its tip at the bottom middle. Drawn rather
        // than a turned square (as the bubbles' tail is), which would show its overlap with a see-through panel.
        private sealed class Arrow : VisualElement
        {
            private readonly Color _color;

            public Arrow(Color color)
            {
                _color = color;
                pickingMode = PickingMode.Ignore;
                AddToClassList("sign-arrow");
                generateVisualContent += Draw;
            }

            private void Draw(MeshGenerationContext context)
            {
                var size = contentRect.size;
                var painter = context.painter2D;
                painter.fillColor = _color;
                painter.BeginPath();
                painter.MoveTo(Vector2.zero);
                painter.LineTo(new Vector2(size.x, 0));
                painter.LineTo(new Vector2(size.x / 2, size.y));
                painter.ClosePath();
                painter.Fill();
            }
        }
    }
}
