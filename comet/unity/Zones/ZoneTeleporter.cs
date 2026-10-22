using UnityEngine;

namespace Comet.Unity
{
    /// <summary>A teleporter in a zone opened in the editor: standing in it sends players to its target. Clicking its pad selects it first.</summary>
    public sealed class ZoneTeleporter : ZoneTrigger
    {
        [Tooltip("How players arriving here face. Direction: the way the object turns (its Y rotation).")]
        public FacingMode facing;

        [Tooltip("The teleporter it sends players to, in any zone (teleporter.cave.to_meadow).")]
        public string target = "teleporter.";

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
