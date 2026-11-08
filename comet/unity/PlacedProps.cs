using Comet.Content;
using UnityEngine;

namespace Comet.Unity
{
    /// <summary>A placed prop's model; objects named <c>Prop.CollisionSuffix</c> stay hidden.</summary>
    public static class PlacedProps
    {
        public static bool IsCollision(Object thing) => thing.name.EndsWith(Prop.CollisionSuffix, System.StringComparison.Ordinal);

        public static void HideCollision(GameObject thing)
        {
            foreach (var renderer in thing.GetComponentsInChildren<Renderer>(true))
            {
                if (IsCollision(renderer))
                {
                    renderer.enabled = false;
                }
            }
        }

        public static void SetMaterial(GameObject thing, Material material)
        {
            foreach (var renderer in thing.GetComponentsInChildren<Renderer>(true))
            {
                if (!IsCollision(renderer) && renderer.sharedMaterial != material)
                {
                    renderer.sharedMaterial = material;
                }
            }
        }
    }
}
