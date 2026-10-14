using UnityEditor;

namespace Comet.Unity.Editor
{
    /// <summary>A placement's Inspector: its fields, and its color as one field with a reset button (<see cref="ColorFields"/>).</summary>
    [CustomEditor(typeof(ZonePlacement))]
    [CanEditMultipleObjects]
    internal sealed class ZonePlacementInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            var placement = (ZonePlacement)target;
            var own = serializedObject.FindProperty(nameof(ZonePlacement.ownColor));
            var color = serializedObject.FindProperty(nameof(ZonePlacement.color));
            EditorGUI.showMixedValue = own.hasMultipleDifferentValues || color.hasMultipleDifferentValues;
            var (picked, changed, reset) = ColorFields.WithReset(
                new UnityEngine.GUIContent("Color", "The placement's color. Reset: its asset's, else the game's (Comet Settings)."),
                placement.ownColor ? placement.color : placement.DefaultColor, own.boolValue || own.hasMultipleDifferentValues);
            EditorGUI.showMixedValue = false;
            if (changed)
            {
                own.boolValue = true;
                color.colorValue = picked;
            }

            serializedObject.ApplyModifiedProperties();
            if (reset)
            {
                foreach (var t in targets)
                {
                    var p = (ZonePlacement)t;
                    Undo.RecordObject(p, "Reset color");
                    p.ownColor = false;
                    p.color = p.DefaultColor;
                    p.ApplyColor();
                    EditorUtility.SetDirty(p);
                }
            }
        }
    }
}
