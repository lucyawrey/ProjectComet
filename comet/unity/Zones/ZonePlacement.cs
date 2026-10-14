using UnityEngine;

namespace Comet.Unity
{
    /// <summary>An asset placed in a zone opened in the editor: the object's transform is the placement's.</summary>
    [DisallowMultipleComponent]
    public sealed class ZonePlacement : MonoBehaviour
    {
        [Tooltip("The asset's key (asset.cube).")]
        public string asset;
    }
}
