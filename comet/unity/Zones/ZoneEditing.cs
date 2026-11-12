using UnityEngine;

namespace Comet.Unity
{
    public sealed class ZoneEditing : MonoBehaviour
    {
        [Tooltip("The zone's folder, relative to the Unity project; for a group being edited, its file.")]
        public string folder = "";

        [Tooltip("The group being edited (group.tree), or empty for a zone. A group's scene has no terrain, and its pivot is the scene's origin.")]
        public string groupId = "";

        public bool IsGroup => !string.IsNullOrEmpty(groupId);

        // Kept as base64 text, not a byte array: Unity's undo compares arrays element by element when their length changes,
        // which took seconds per terrain stroke.

        [SerializeField, HideInInspector]
        private string zoneData = "";

        /// <summary>Set it after <c>Undo.RecordObject</c> to make an undo step.</summary>
        public byte[] ZoneData
        {
            get => System.Convert.FromBase64String(zoneData);
            set => zoneData = System.Convert.ToBase64String(value);
        }

        public bool HasZoneData => zoneData.Length > 0;

        private void OnDrawGizmos()
        {
            if (IsGroup)
            {
                Gizmos.color = Color.white;
                Gizmos.DrawLine(new Vector3(-1, 0, 0), new Vector3(1, 0, 0));
                Gizmos.DrawLine(new Vector3(0, 0, -1), new Vector3(0, 0, 1));
                Gizmos.DrawLine(Vector3.zero, new Vector3(0, 0.5f, 0));
            }
        }
    }
}
