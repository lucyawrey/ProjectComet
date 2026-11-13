using System.Collections.Generic;
using Comet.Content;
using Comet.Unity;
using ShapeLand.Shared.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(Hud))]
    public sealed class Signs : MonoBehaviour
    {
        private const int ChecksPerFrame = 8;
        private const float FadeSeconds = 0.15f;

        private const float CameraGap = 0.1f,
            SignGap = 0.3f;

        private const float WorldLayoutFontSize = 13,
            ArrowHeight = 7,
            MaxWorldScale = 20;

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
                    text.style.color = Conversions.FromRgb(sign.TextColor);
                    text.style.fontSize = sign.ScreenSized ? sign.FontSize : WorldLayoutFontSize;
                    panel.Add(text);
                    var back = Conversions.FromRgb(sign.BackgroundColor);
                    back.a = sign.Background ? sign.BackgroundOpacity : 0;
                    panel.style.backgroundColor = back;
                    if (sign.Arrow && sign.Background)
                    {
                        panel.AddToClassList("with-arrow");
                        panel.Add(new PanelArrow(back, "sign-arrow"));
                    }

                    panel.style.display = DisplayStyle.None;
                    _layer.Insert(0, panel);
                    _signs.Add(new Shown(sign, point, panel) { HasArrow = sign.Arrow && sign.Background });
                }
            }

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
                    shown.Checked = false;
                }
                else if (checks > 0)
                {
                    checks--;
                    _next = (index + 1) % _signs.Count;
                    shown.Checked = true;
                    shown.InSight = InSight(eye, point);
                }

                var target = inRange && shown.Checked && shown.InSight ? 1 : 0;
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
            return !_hud.Join.World.IsLineBlocked(
                Conversions.ToNumerics(eye + along * CameraGap),
                Conversions.ToNumerics(point - along * SignGap),
                _walls
            );
        }

        private sealed class Shown
        {
            public Shown(Sign sign, System.Numerics.Vector3 point, VisualElement panel)
            {
                Sign = sign;
                Point = Conversions.ToUnity(point);
                Panel = panel;
            }

            public Sign Sign { get; }

            public Vector3 Point { get; }
            public VisualElement Panel { get; }
            public bool HasArrow { get; set; }
            public bool Checked { get; set; }
            public bool InSight { get; set; }
            public float Opacity { get; set; }
        }
    }
}
