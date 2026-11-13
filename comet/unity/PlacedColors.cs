using UnityEngine;

namespace Comet.Unity
{
    public static class PlacedColors
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

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
