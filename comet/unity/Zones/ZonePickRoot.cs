using UnityEngine;

namespace Comet.Unity
{
    /// <summary>
    /// Marks a prop or a group copy as what a click on its meshes or parts selects in the Scene view. Kept by
    /// <see cref="ZonePlacement"/> itself, and only on those: a primitive's mesh is its own, so a click on a trigger's
    /// pad goes on to the trigger first, as with a prefab's parts.
    /// </summary>
    [SelectionBase]
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class ZonePickRoot : MonoBehaviour
    {
    }
}
