using UnityEngine;

namespace Comet.Unity.Editor
{
    /// <summary>
    /// A game's settings for the zone editor: where its zones are and how to draw them. Each game's Unity project has
    /// one (Create › Comet › Zone Editor Settings); the zone editor finds it.
    /// </summary>
    [CreateAssetMenu(menuName = "Comet/Zone Editor Settings")]
    public sealed class ZoneEditorSettings : ScriptableObject
    {
        [Tooltip("The game's zones folder, relative to the Unity project (each zone is a folder in it).")]
        public string zonesFolder = "../content/zones";

        [Tooltip("The models for asset keys.")]
        public AssetLibrary assets;

        [Tooltip("Terrain's material: Comet/Vertex Color Lit, since its colors are vertex colors.")]
        public Material terrainMaterial;

        [Tooltip("If set, placed objects are drawn in it instead of their models' own materials.")]
        public Material placedMaterial;

        [Tooltip("The color of terrain no one has painted.")]
        public Color groundColor = new Color32(0x73, 0x9e, 0x4d, 0xff);

        [Tooltip("The color of terrain's skirts: the walls and bottoms where its ground ends.")]
        public Color skirtColor = new Color32(0x7a, 0x5c, 0x3e, 0xff);

        [Tooltip("The game's body height, in meters: the terrain tools warn where two pieces' ground is closer than this.")]
        public float bodyHeight = 1;
    }
}
