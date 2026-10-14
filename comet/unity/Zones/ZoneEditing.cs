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

        [HideInInspector] public byte[] zoneData = System.Array.Empty<byte>();
    }
}
