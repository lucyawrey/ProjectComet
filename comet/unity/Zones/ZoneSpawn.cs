using UnityEngine;

namespace Comet.Unity
{
    /// <summary>A spawn point in a zone opened in the editor, at the object's position. Drawn as a ring of its radius.
    /// Clicking its marker in the Scene view selects the spawn point first.</summary>
    [DisallowMultipleComponent]
    [SelectionBase]
    public sealed class ZoneSpawn : MonoBehaviour
    {
        public enum FacingMode
        {
            Direction,
            TowardPoint,
            Random,
        }

        [Tooltip("The spawn's key (spawn.meadow_gate), unique across zones.")]
        public string key = "";

        [Tooltip("How far from the point arrivals scatter, in meters.")]
        [Min(0)] public float radius = 2;

        [Tooltip("Direction: face the object's forward (its Y rotation). Toward point: face the center. Random.")]
        public FacingMode facing;

        [Tooltip("The zone's default spawn (one per zone).")]
        public bool isDefault;

        private void OnDrawGizmos()
        {
            Gizmos.color = isDefault ? new Color(1, 0.8f, 0.2f) : new Color(0.3f, 0.8f, 1);
            const int segments = 32;
            var center = transform.position;
            for (var i = 0; i < segments; i++)
            {
                var a = 2 * Mathf.PI * i / segments;
                var b = 2 * Mathf.PI * (i + 1) / segments;
                Gizmos.DrawLine(center + radius * new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)), center + radius * new Vector3(Mathf.Sin(b), 0, Mathf.Cos(b)));
            }

            if (facing == FacingMode.Direction)
            {
                Gizmos.DrawLine(center, center + transform.forward * Mathf.Max(radius, 1));
            }
        }
    }
}
