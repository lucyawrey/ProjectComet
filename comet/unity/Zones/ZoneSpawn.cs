using UnityEngine;

namespace Comet.Unity
{
    /// <summary>A spawn point in a zone opened in the editor: players arrive in its volume. Clicking its pad selects it first.</summary>
    public sealed class ZoneSpawn : ZoneTrigger
    {
        [Tooltip("Direction: face the way the object turns (its Y rotation). Toward point: face the center. Random.")]
        public FacingMode facing;

        public override string Kind => "spawn";

        protected override Color GizmoColor => new Color(0.3f, 0.8f, 1);

        protected override void OnDrawGizmos()
        {
            base.OnDrawGizmos();
            DrawFacing(facing);
        }
    }
}
