using Comet.Content;
using UnityEngine;

namespace Comet.Unity
{
    /// <summary>
    /// Shared by spawn points, teleporters and triggers in a zone opened in the editor: an invisible volume at the
    /// object's position, an optional pad (a primitive child, solid in the game) and an optional sign (a sign child).
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
        [Min(0)]
        public float standSeconds;

        [Tooltip("Step off: fires again only after stepping out. Repeat: again every stand time while inside.")]
        public RearmMode rearm;

        public abstract string Kind { get; }

        protected abstract Color GizmoColor { get; }

        public PrimitiveKind Shape => (PrimitiveKind)System.Enum.Parse(typeof(PrimitiveKind), volume.ToString());

        // The wireframe, kept until the shape, size or polygon budget changes.
        private Vector3[]? _edges;
        private (VolumeKind, Vector3, float, int, int) _edgesFor;

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

        public ZoneSign? Sign
        {
            get
            {
                foreach (Transform child in transform)
                {
                    if (child.GetComponent<ZoneSign>() is { } sign)
                    {
                        return sign;
                    }
                }

                return null;
            }
        }

        protected virtual void OnDrawGizmos()
        {
            var budget = ZonePlacement.Budget;
            var key = (volume, size, budget.FacetLength, budget.MinSides, budget.MaxSides);
            if (_edges == null || key != _edgesFor)
            {
                _edges = CollisionView.VolumeEdges(Shape, size, budget);
                _edgesFor = key;
            }

            Gizmos.color = GizmoColor;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, Quaternion.Euler(0, transform.eulerAngles.y, 0), Vector3.one);
            Gizmos.DrawLineList(_edges);
            Gizmos.matrix = Matrix4x4.identity;
        }

        protected void DrawFacing(FacingMode facing)
        {
            if (facing == FacingMode.Direction)
            {
                Gizmos.color = GizmoColor;
                Gizmos.matrix = Matrix4x4.TRS(transform.position, Quaternion.Euler(0, transform.eulerAngles.y, 0), Vector3.one);
                Gizmos.DrawLineList(CollisionView.FacingEdge(size));
                Gizmos.matrix = Matrix4x4.identity;
            }
        }
    }
}
