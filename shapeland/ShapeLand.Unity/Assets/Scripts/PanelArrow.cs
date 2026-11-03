using UnityEngine;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    /// <summary>
    /// A little triangle under a panel (a speech bubble, a sign), its tip at the bottom middle: in the given color, or
    /// with none, in the panel's background color as its style resolves it.
    /// Drawn rather than a turned square, which would show its overlap with a see-through panel as a darker piece.
    /// </summary>
    internal sealed class PanelArrow : VisualElement
    {
        private readonly Color? _color;

        public PanelArrow(Color? color, string className)
        {
            _color = color;
            pickingMode = PickingMode.Ignore;
            AddToClassList(className);
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext context)
        {
            var size = contentRect.size;
            var painter = context.painter2D;
            painter.fillColor = _color ?? parent?.resolvedStyle.backgroundColor ?? Color.clear;
            painter.BeginPath();
            painter.MoveTo(Vector2.zero);
            painter.LineTo(new Vector2(size.x, 0));
            painter.LineTo(new Vector2(size.x / 2, size.y));
            painter.ClosePath();
            painter.Fill();
        }
    }
}
