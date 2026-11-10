using System;
using System.Linq;
using Comet.Unity;
using ShapeLand.Shared.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    public sealed partial class JoinScreen
    {
        private void StartFresh(bool glide = true)
        {
            _name.value = "";
            _shape = _content.Shapes.All[0];
            _color = _eyeColor = _content.Colors.All[0].Number;
            ShowStartZone(glide);
        }

        private void AddSwatches(VisualElement row, Action<int> pick)
        {
            foreach (var entry in _content.Colors.All)
            {
                var color = entry.Number;
                void Choose()
                {
                    pick(color);
                    Refresh();
                }

                var swatch = new Button(Choose) { userData = color, tooltip = $"#{entry.Rgb:X6}" };
                _choose[swatch] = Choose;
                swatch.AddToClassList("swatch");
                var fill = new VisualElement { pickingMode = PickingMode.Ignore };
                fill.AddToClassList("swatch-fill");
                fill.style.backgroundColor = Conversions.FromRgb(entry.Rgb);
                swatch.Add(fill);
                row.Add(swatch);
            }
        }

        private void Refresh()
        {
            foreach (var button in _shapes.Children())
            {
                button.EnableInClassList("on", button.userData == _shape);
                button.Q<ShapeIcon>().Set(_content.BodyKind((Shape)button.userData), Conversions.FromRgb(_content.ColorRgb(_color)));
            }

            MarkSwatch(_colors, _color);
            MarkSwatch(_eyes, _eyeColor);

            _preview.Show(_view, _shape, _content.ColorRgb(_color), _content.ColorRgb(_eyeColor));

            var best = _content.Shapes.All.Select(shape => Stats(shape, _content.Movement)).ToList();
            var (speed, jump) = Stats(_shape, _content.Movement);
            _stats.Clear();
            var strings = _content.Strings;
            _stats.Add(
                Stat(strings.Get("join.speed"), strings.Format("join.speed_value", ("speed", speed.ToString("0.#"))), speed / best.Max(s => s.Speed))
            );
            _stats.Add(
                Stat(
                    strings.Get("join.jump"),
                    strings.Format("join.jump_value", ("height", jump.ToString("0.0"))),
                    jump / best.Max(s => s.JumpHeight)
                )
            );
        }

        private static void MarkSwatch(VisualElement row, int color)
        {
            foreach (var swatch in row.Children())
            {
                swatch.EnableInClassList("on", (int)swatch.userData == color);
            }
        }

        private static VisualElement Stat(string name, string value, float share)
        {
            var stat = new VisualElement();
            stat.AddToClassList("stat");
            var top = new VisualElement();
            top.AddToClassList("stat-top");
            var label = new Label(name);
            label.AddToClassList("stat-name");
            top.Add(label);
            top.Add(new Label(value));
            var bar = new VisualElement();
            bar.AddToClassList("bar");
            var fill = new VisualElement();
            fill.AddToClassList("bar-fill");
            fill.style.width = Length.Percent(Mathf.Clamp01(share) * 100);
            bar.Add(fill);
            stat.Add(top);
            stat.Add(bar);
            return stat;
        }
    }
}
