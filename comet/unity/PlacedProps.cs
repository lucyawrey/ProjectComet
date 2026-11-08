using Comet.Content;
using UnityEngine;

namespace Comet.Unity
{
    /// <summary>A placed prop's model: its collision objects (named with <see cref="Prop.CollisionSuffix"/>) stay hidden.</summary>
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

        /// <summary>Draws the visible parts in <paramref name="material"/>.</summary>
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
