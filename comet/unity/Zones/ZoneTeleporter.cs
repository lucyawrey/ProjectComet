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

        public override string Kind => "teleporter";

        protected override Color GizmoColor => new Color(0.75f, 0.55f, 1);

        protected override void OnDrawGizmos()
        {
            base.OnDrawGizmos();
            DrawFacing(facing);
        }
    }
}
