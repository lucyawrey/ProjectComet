using UnityEngine;

namespace Comet.Unity
{
    /// <summary>Draws a placed prop in a color, over its material's own, without making a copy of the material.</summary>
    public static class PlacedColors
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        /// <summary>A 0xRRGGBB color as Unity's.</summary>
        public static Color FromRgb(uint rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

        /// <summary>A Unity color as 0xRRGGBB.</summary>
        public static uint ToRgb(Color color)
        {
            Color32 c = color;
            return (uint)(c.r << 16 | c.g << 8 | c.b);
        }

        /// <summary>Colors the visible renderers of a placed prop, or with null, puts back its material's color.</summary>
        public static void Apply(GameObject thing, Color? color)
        {
            MaterialPropertyBlock? block = null;
            if (color is { } c)
            {
                block = new MaterialPropertyBlock();
                block.SetColor(BaseColor, c);
            }

            foreach (var renderer in thing.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.name.EndsWith(Comet.Content.Prop.CollisionSuffix, System.StringComparison.Ordinal))
                {
                    renderer.SetPropertyBlock(block);
                }
            }
        }
    }
}
