using UnityEngine;

namespace Comet.Unity
{
    public sealed class ZoneTeleporter : ZoneTrigger
    {
        [Tooltip("How players arriving here face. Direction: the way the object turns (its Y rotation).")]
        public FacingMode facing;

        [Tooltip("The teleporter it sends players to, in any zone (teleporter.cave.to_meadow).")]
        public string target = "";

        [Tooltip("The color its pad turns toward while a player stands in it.")]
        public Color chargeColor = Color.white;

        public override string Kind => "teleporter";

        protected override Color GizmoColor => CollisionColors.Editing.teleporter;

        protected override void OnDrawGizmos()
        {
            base.OnDrawGizmos();
            DrawFacing(facing);
        }
    }
}
