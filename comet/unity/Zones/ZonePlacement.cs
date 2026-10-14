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

        // Drawn by the editor's inspector as one color field with a reset button: editing it gives the placement its
        // own color, resetting goes back to the default (its asset's, else the placed material's).
        [HideInInspector] public bool ownColor;
        [HideInInspector] public Color color = Color.gray;
        [SerializeField, HideInInspector] private bool hasAssetColor;
        [SerializeField, HideInInspector] private Color defaultColor = Color.gray;

        /// <summary>The color it's drawn in without its own.</summary>
        public Color DefaultColor => defaultColor;

        /// <summary>Sets the default: the asset's color, or null for the material's (<paramref name="material"/>).</summary>
        public void SetDefaultColor(Color? asset, Color material)
        {
            hasAssetColor = asset != null;
            defaultColor = asset ?? material;
            if (!ownColor)
            {
                color = defaultColor;
            }

            ApplyColor();
        }

        /// <summary>Draws the object in its color (property blocks aren't saved with the scene).</summary>
        public void ApplyColor() => PlacedColors.Apply(gameObject, ownColor ? color : hasAssetColor ? defaultColor : null);

        private void OnEnable() => ApplyColor();

        private void OnValidate() => ApplyColor();
    }
}
