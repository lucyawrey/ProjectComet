using UnityEditor;
using UnityEngine;

namespace Comet.Unity.Editor
{
    internal static class ColorFields
    {
        private static GUIContent? _resetIcon;

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
                reset = GUILayout.Button(
                    _resetIcon,
                    EditorStyles.iconButton,
                    GUILayout.Width(20),
                    GUILayout.Height(EditorGUIUtility.singleLineHeight)
                );
            }

            EditorGUILayout.EndHorizontal();
            return (color, changed, reset);
        }
    }
}
