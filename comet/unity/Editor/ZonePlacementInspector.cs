using UnityEditor;
using UnityEngine;

namespace Comet.Unity.Editor
{
    /// <summary>
    /// A placement's Inspector: its fields, and its color as one field with a reset button (<see cref="ColorFields"/>).
    /// With one primitive selected, Unity's scale tool is swapped for a handle that edits its size, so its facets follow
    /// the polygon budget as it's dragged.
    /// </summary>
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

        private bool _resizing;

        private void OnDisable() => Tools.hidden = false;

        private void OnSceneGUI()
        {
            var placement = (ZonePlacement)target;
            var ours = targets.Length == 1 && placement.isPrimitive && Tools.current == Tool.Scale;
            Tools.hidden = ours;
            if (!ours)
            {
                return;
            }

            var t = placement.transform;
            EditorGUI.BeginChangeCheck();
            // Along the primitive's own axes, as its size is (Unity's scale tool does the same).
            var size = Handles.ScaleHandle(placement.size, t.position, t.rotation, HandleUtility.GetHandleSize(t.position));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(placement, "Resize primitive");
                placement.size = new Vector3(Mathf.Max(Mathf.Abs(size.x), 0.01f), Mathf.Max(Mathf.Abs(size.y), 0.01f), Mathf.Max(Mathf.Abs(size.z), 0.01f));
                placement.PreviewResize();
                _resizing = true;
            }

            // Back to the shared mesh, and its collider, once the drag ends.
            if (_resizing && GUIUtility.hotControl == 0)
            {
                _resizing = false;
                placement.Rebuild();
            }
        }
    }
}
