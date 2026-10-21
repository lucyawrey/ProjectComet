using UnityEngine;

namespace Comet.Unity
{
    /// <summary>
    /// What spawn points, teleporters and triggers share in a zone opened in the editor: a id, an invisible volume
    /// standing on the object's position and turned by its Y rotation, a stand time and re-arming, and an optional pad
    /// (a primitive placement as its child, solid in the game). The volume is drawn as a wireframe.
    /// </summary>
    [DisallowMultipleComponent]
    [SelectionBase]
    public abstract class ZoneTrigger : MonoBehaviour
    {
        public enum VolumeKind
        {
            Box,
            Cylinder,
            Sphere,
        }

        public enum FacingMode
        {
            Direction,
            TowardPoint,
            Random,
        }

        public enum RearmMode
        {
            StepOff,
            Repeat,
        }

        [Tooltip("The id, naming its zone (spawn.meadow.gate).")]
        public string id = "";

        [Tooltip("The volume's shape: players are in it when their feet are inside.")]
        public VolumeKind volume = VolumeKind.Box;

        [Tooltip("The volume's size along its own axes, in meters; it stands on the object's position.")]
        public Vector3 size = new Vector3(2, 2, 2);

        [Tooltip("How long a player stands inside before it fires, in seconds (0 fires at once).")]
        [Min(0)] public float standSeconds;

        [Tooltip("Step off: fires again only after stepping out. Repeat: again every stand time while inside.")]
        public RearmMode rearm;

        /// <summary>The kind, as ids start: spawn, teleporter or trigger.</summary>
        public abstract string Kind { get; }

        /// <summary>The wireframe's color.</summary>
        protected abstract Color GizmoColor { get; }

        /// <summary>The pad: a placement that's a direct child, or null.</summary>
        public ZonePlacement? Pad
        {
            get
            {
                foreach (Transform child in transform)
                {
                    if (child.GetComponent<ZonePlacement>() is { } pad)
                    {
                        return pad;
                    }
                }

                return null;
            }
        }

        protected virtual void OnDrawGizmos()
        {
            Gizmos.color = GizmoColor;
            var turn = Quaternion.Euler(0, transform.eulerAngles.y, 0);
            Gizmos.matrix = Matrix4x4.TRS(transform.position + Vector3.up * (size.y / 2), turn, Vector3.one);
            switch (volume)
            {
                case VolumeKind.Box:
                    Gizmos.DrawWireCube(Vector3.zero, size);
                    break;
                case VolumeKind.Sphere:
                    Gizmos.matrix *= Matrix4x4.Scale(size / 2);
                    Gizmos.DrawWireSphere(Vector3.zero, 1);
                    break;
                default:
                    const int segments = 32;
                    var half = size / 2;
                    for (var i = 0; i < segments; i++)
                    {
                        var a = 2 * Mathf.PI * i / segments;
                        var b = 2 * Mathf.PI * (i + 1) / segments;
                        var from = new Vector3(Mathf.Sin(a) * half.x, 0, Mathf.Cos(a) * half.z);
                        var to = new Vector3(Mathf.Sin(b) * half.x, 0, Mathf.Cos(b) * half.z);
                        Gizmos.DrawLine(from - Vector3.up * half.y, to - Vector3.up * half.y);
                        Gizmos.DrawLine(from + Vector3.up * half.y, to + Vector3.up * half.y);
                        if (i % 8 == 0)
                        {
                            Gizmos.DrawLine(from - Vector3.up * half.y, from + Vector3.up * half.y);
                        }
                    }

                    break;
            }

            Gizmos.matrix = Matrix4x4.identity;
        }

        /// <summary>Draws which way arrivals face, for the kinds players arrive at.</summary>
        protected void DrawFacing(FacingMode facing)
        {
            if (facing == FacingMode.Direction)
            {
                Gizmos.color = GizmoColor;
                var from = transform.position + Vector3.up * 0.05f;
                Gizmos.DrawLine(from, from + Quaternion.Euler(0, transform.eulerAngles.y, 0) * Vector3.forward * Mathf.Max(size.z / 2, 1));
            }
        }
    }
}
