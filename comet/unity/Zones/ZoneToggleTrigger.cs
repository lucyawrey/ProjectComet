using System;
using System.Collections.Generic;
using UnityEngine;

namespace Comet.Unity
{
    /// <summary>A trigger in a zone opened in the editor: standing in it flips the placements its effects name. Clicking its pad selects it first.</summary>
    public sealed class ZoneToggleTrigger : ZoneTrigger
    {
        [Serializable]
        public sealed class Effect
        {
            [Tooltip("The name of the placements to flip (several may share it; a named group copy flips whole).")]
            public string toggle = "";

            [Tooltip("Seconds until they flip back, or 0 to stay. Firing again before then restarts the wait.")]
            [Min(0)] public float revertSeconds;
        }

        [Tooltip("What firing does: each effect flips its placements on or off.")]
        public List<Effect> effects = new List<Effect>();

        [Tooltip("The color its pad turns toward while a player stands in it.")]
        public Color chargeColor = Color.white;

        public override string Kind => "trigger";

        protected override Color GizmoColor => new Color(1, 0.65f, 0.2f);
    }
}
