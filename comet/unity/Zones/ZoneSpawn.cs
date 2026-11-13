using UnityEngine;

namespace Comet.Unity
{
    public sealed class ZoneSpawn : ZoneTrigger
    {
        [Tooltip("Direction: face the way the object turns (its Y rotation). Toward point: face the center. Random.")]
        public FacingMode facing;

        public override string Kind => "spawn";

        protected override Color GizmoColor => CollisionColors.Editing.spawn;

        protected override void OnDrawGizmos()
        {
            base.OnDrawGizmos();
            DrawFacing(facing);
        }
    }
}
