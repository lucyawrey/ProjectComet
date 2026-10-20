using System.Collections.Generic;
using System.Linq;
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
            var placement = (ZonePlacement)target;

            // Only what this kind of placement uses: a primitive's values for primitives, no color for group copies.
            var hidden = new List<string> { "m_Script" };
            if (targets.Cast<ZonePlacement>().Any(p => !p.isPrimitive || p.IsGroup))
            {
                hidden.AddRange(new[] { nameof(ZonePlacement.kind), nameof(ZonePlacement.size), nameof(ZonePlacement.sides), nameof(ZonePlacement.bevel), nameof(ZonePlacement.steps), nameof(ZonePlacement.opening), nameof(ZonePlacement.thickness) });
            }

            if (targets.Cast<ZonePlacement>().All(p => !p.IsGroup))
            {
                hidden.Add(nameof(ZonePlacement.group));
            }
            else
            {
                hidden.AddRange(new[] { nameof(ZonePlacement.asset), nameof(ZonePlacement.isPrimitive), nameof(ZonePlacement.placementName) });
            }

            DrawPropertiesExcluding(serializedObject, hidden.ToArray());
            if (targets.Length == 1 && placement.IsGroup)
            {
                GroupButtons(placement);
                serializedObject.ApplyModifiedProperties();
                return;
            }

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

        // A copy's own actions: edit its group (here, in place, or in a scene of its own), or unpack it into loose placements.
        private static void GroupButtons(ZonePlacement copy)
        {
            var settings = CometSettings.Find(quiet: true);
            if (settings == null)
            {
                return;
            }

            if (copy.EditingInPlace)
            {
                EditorGUILayout.HelpBox($"Editing {copy.group} in place: its parts are placed under this copy. Save the group to change every copy.", MessageType.Info);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Save group"))
                {
                    ZoneGroups.SaveInPlace(settings);
                    GUIUtility.ExitGUI();
                }

                if (GUILayout.Button("Cancel"))
                {
                    ZoneGroups.CancelInPlace();
                    GUIUtility.ExitGUI();
                }

                EditorGUILayout.EndHorizontal();
                return;
            }

            EditorGUILayout.HelpBox("A copy looks like its group. Edit the group to change every copy; unpack this one to change it alone.", MessageType.None);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Edit in place"))
            {
                ZoneGroups.EditInPlace(copy, settings);
                GUIUtility.ExitGUI();
            }

            if (GUILayout.Button("Open group"))
            {
                // After this GUI pass: opening replaces the scene the Inspector is drawing.
                ZoneGroups.OpenLater(copy.group, settings);
            }

            if (GUILayout.Button("Unpack"))
            {
                ZoneGroups.Unpack(copy, settings);
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.EndHorizontal();
        }

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
