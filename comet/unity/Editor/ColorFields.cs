using UnityEditor;
using UnityEngine;

namespace Comet.Unity.Editor
{
    /// <summary>
    /// A color that's either something's own or a default: one color field, its label bold while it's the thing's own
    /// (like Unity's prefab overrides), with a reset button that goes back to the default.
    /// </summary>
    internal static class ColorFields
    {
        private static GUIContent? _resetIcon;

        /// <summary>The field's color, whether it was edited, and whether reset was pressed.</summary>
        public static (Color Color, bool Changed, bool Reset) WithReset(GUIContent label, Color shown, bool own)
        {
            EditorGUILayout.BeginHorizontal();
            var style = own ? EditorStyles.boldLabel : EditorStyles.label;
            EditorGUILayout.PrefixLabel(label, EditorStyles.colorField, style);
            EditorGUI.BeginChangeCheck();
            var color = EditorGUILayout.ColorField(GUIContent.none, shown, true, false, false);
            var changed = EditorGUI.EndChangeCheck();
            bool reset;
            using (new EditorGUI.DisabledScope(!own))
            {
                _resetIcon ??= new GUIContent(EditorGUIUtility.IconContent("Refresh").image, "Reset to the default color");
                reset = GUILayout.Button(_resetIcon, EditorStyles.iconButton, GUILayout.Width(20), GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }

            EditorGUILayout.EndHorizontal();
            return (color, changed, reset);
        }
    }
}
