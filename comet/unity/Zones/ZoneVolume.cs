using UnityEngine;

namespace Comet.Unity
{
    /// <summary>A collision volume in a zone opened in the editor: a box of the object's position, rotation and size.</summary>
    [DisallowMultipleComponent]
    public sealed class ZoneVolume : MonoBehaviour
    {
        public enum VolumeKind
        {
            Wall,
            Water,
        }

        public VolumeKind kind;

        [Tooltip("The box's full size along its own axes, in meters.")]
        public Vector3 size = Vector3.one;

        private void OnDrawGizmos()
        {
            Gizmos.color = kind == VolumeKind.Water ? new Color(0.2f, 0.5f, 1, 0.8f) : new Color(1, 0.3f, 0.3f, 0.8f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
    }
}
