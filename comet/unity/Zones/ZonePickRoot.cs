using UnityEngine;

namespace Comet.Unity
{
    /// <summary>
    /// Marks a prop or group copy as what a click on its parts selects in the Scene view. Only those: a primitive's
    /// mesh is its own, so a click on a trigger's pad selects the trigger first.
    /// </summary>
    [SelectionBase]
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class ZonePickRoot : MonoBehaviour { }
}
