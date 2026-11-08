using UnityEngine;

namespace Comet.Unity
{
    /// <summary>Draws a placed prop in a color, over its material's own, without making a copy of the material.</summary>
    public static class PlacedColors
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

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
                if (!PlacedProps.IsCollision(renderer))
                {
                    renderer.SetPropertyBlock(block);
                }
            }
        }
    }
}
