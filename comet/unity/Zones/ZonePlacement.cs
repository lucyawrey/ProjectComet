using UnityEngine;

namespace Comet.Unity
{
    /// <summary>An asset placed in a zone opened in the editor: the object's transform is the placement's.</summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class ZonePlacement : MonoBehaviour
    {
        [Tooltip("The asset's key (asset.cube).")]
        public string asset = "";

        [Tooltip("An optional name, unique in the zone, for game code to find it by (a spawn point's marker has the spawn's key).")]
        public string placementName = "";

        [Tooltip("Off: drawn in its asset's color.")]
        public bool ownColor;

        [Tooltip("The placement's own color.")]
        public Color color = Color.gray;

        // The asset's color, set by the editor as it places the object; null for the material's.
        [SerializeField, HideInInspector] private bool hasAssetColor;
        [SerializeField, HideInInspector] private Color assetColor;

        /// <summary>Sets the asset's color, and starts the own color from what the object shows now.</summary>
        public void SetAssetColor(Color? color, Color shown)
        {
            hasAssetColor = color != null;
            assetColor = color ?? default;
            if (!ownColor)
            {
                this.color = shown;
            }

            ApplyColor();
        }

        /// <summary>Draws the object in its color (property blocks aren't saved with the scene).</summary>
        public void ApplyColor() => PlacedColors.Apply(gameObject, ownColor ? color : hasAssetColor ? assetColor : null);

        private void OnEnable() => ApplyColor();

        private void OnValidate() => ApplyColor();
    }
}
