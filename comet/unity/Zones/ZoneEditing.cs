using UnityEngine;

namespace Comet.Unity
{
    /// <summary>
    /// The root of a zone opened in the editor (Comet › Zones › Open): which folder it came from, and the parts of the
    /// zone not shown as objects under it (terrain, borders, entrances, transition rooms), kept with the scene so they
    /// survive script reloads. The editor builds and saves it; games never use it.
    /// </summary>
    public sealed class ZoneEditing : MonoBehaviour
    {
        [Tooltip("The zone's folder, relative to the Unity project.")]
        public string folder = "";

        // Kept as base64 text, not a byte array: Unity's undo compares arrays element by element when their length
        // changes, which took seconds for each terrain stroke, while a string is one value.
        [SerializeField, HideInInspector] private string zoneData = "";

        /// <summary>The zone's stored parts, serialized. Set it after <c>Undo.RecordObject</c> to make an undo step.</summary>
        public byte[] ZoneData
        {
            get => System.Convert.FromBase64String(zoneData);
            set => zoneData = System.Convert.ToBase64String(value);
        }

        public bool HasZoneData => zoneData.Length > 0;
    }
}
