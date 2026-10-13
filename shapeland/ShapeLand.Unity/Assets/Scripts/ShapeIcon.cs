using ShapeLand.Shared.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    /// <summary>
    /// A flat drawing of a shape in a player's colour (a lit face and a shaded side), for buttons and chat lines:
    /// the shape alongside the colour, so players aren't told apart by colour alone.
    /// </summary>
    public sealed class ShapeIcon : VisualElement
    {
        private MeshKind _kind;
        private Color _colour;

        public ShapeIcon(MeshKind kind, Color colour)
        {
            _kind = kind;
            _colour = colour;
            pickingMode = PickingMode.Ignore;
            AddToClassList("shape-icon");
            generateVisualContent += Draw;
        }

        public void Set(MeshKind kind, Color colour)
        {
            _kind = kind;
            _colour = colour;
            MarkDirtyRepaint();
        }

        // Drawn in a 100 x 100 box, like the mockup's icons.
        private void Draw(MeshGenerationContext context)
        {
            var size = Mathf.Min(contentRect.width, contentRect.height);
            if (size <= 0)
            {
                return;
            }

            var painter = context.painter2D;
            var dark = Color.Lerp(_colour, Color.black, 0.3f);
            var light = Color.Lerp(_colour, Color.white, 0.25f);
            void Polygon(Color fill, params float[] points)
            {
                painter.fillColor = fill;
                painter.BeginPath();
                painter.MoveTo(new Vector2(points[0], points[1]) * size / 100);
                for (var i = 2; i < points.Length; i += 2)
                {
                    painter.LineTo(new Vector2(points[i], points[i + 1]) * size / 100);
                }

                painter.ClosePath();
                painter.Fill();
            }

            switch (_kind)
            {
                case MeshKind.Cube:
                    Polygon(light, 18, 36, 34, 20, 84, 20, 68, 36);
                    Polygon(dark, 68, 36, 84, 20, 84, 70, 68, 86);
                    Polygon(_colour, 18, 36, 68, 36, 68, 86, 18, 86);
                    break;
                case MeshKind.Diamond:
                    Polygon(_colour, 50, 6, 18, 50, 50, 94);
                    Polygon(dark, 50, 6, 82, 50, 50, 94);
                    break;
                default:
                    Polygon(_colour, 50, 10, 14, 86, 64, 86);
                    Polygon(dark, 50, 10, 64, 86, 86, 76);
                    break;
            }
        }
    }
}
