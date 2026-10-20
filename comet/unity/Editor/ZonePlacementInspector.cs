using System.Collections.Generic;
using System.Linq;
using Comet.Content;
using UnityEditor;
using UnityEngine;

namespace Comet.Unity.Editor
{
    /// <summary>
    /// A placement's Inspector: its fields, and its color as one field with a reset button (<see cref="ColorFields"/>).
    /// (With one primitive selected, the scale tool is <see cref="PrimitiveSizeTool"/>.)
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
                hidden.AddRange(new[] { nameof(ZonePlacement.prop), nameof(ZonePlacement.isPrimitive), nameof(ZonePlacement.placementName) });
            }

            // Only a group's parts have a role: in a group's own scene, or under a copy edited in place.
            if (targets.Cast<ZonePlacement>().Any(p => p.IsLoose && p.GetComponentInParent<ZoneEditing>() is not { IsGroup: true }))
            {
                hidden.Add(nameof(ZonePlacement.role));
            }

            DrawPropertiesExcluding(serializedObject, hidden.ToArray());
            if (targets.Length == 1 && placement.IsGroup)
            {
                serializedObject.ApplyModifiedProperties();
                RoleColors(placement);
                GroupButtons(placement);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            var own = serializedObject.FindProperty(nameof(ZonePlacement.ownColor));
            var color = serializedObject.FindProperty(nameof(ZonePlacement.color));
            EditorGUI.showMixedValue = own.hasMultipleDifferentValues || color.hasMultipleDifferentValues;
            var (picked, changed, reset) = ColorFields.WithReset(
                new UnityEngine.GUIContent("Color", "The placement's color. Reset: its prop's, else the game's (Comet Settings)."),
                placement.ownColor ? placement.color : placement.DefaultColor, own.boolValue || own.hasMultipleDifferentValues);
            EditorGUI.showMixedValue = false;
            if (changed)
            {
                own.boolValue = true;
                color.colorValue = picked;
            }

            serializedObject.ApplyModifiedProperties();
            if (targets.Length == 1 && placement.IsLoose && placement.GetComponentInParent<ZoneSpawn>() == null)
            {
                SwapButtons(placement);
            }

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

        // Swapping in place, as blockout becomes a finished level: a primitive for a prop, a prop for a box.
        private static void SwapButtons(ZonePlacement placed)
        {
            var settings = CometSettings.Find(quiet: true);
            if (settings == null || settings.props == null)
            {
                return;
            }

            if (placed.isPrimitive)
            {
                var keys = settings.props.Entries.Select(e => e.key).Where(k => !string.IsNullOrEmpty(k)).OrderBy(k => k, System.StringComparer.Ordinal).ToList();
                using (new EditorGUI.DisabledScope(keys.Count == 0))
                {
                    var label = new GUIContent("Swap for prop…", keys.Count == 0 ? "The game has no props yet." : "Replace it with a prop scaled to its size, standing where it stands.");
                    if (GUILayout.Button(label))
                    {
                        var menu = new GenericMenu();
                        foreach (var key in keys)
                        {
                            menu.AddItem(new GUIContent(key), false, () => ZoneSwap.ForProp(placed, key, settings));
                        }

                        menu.ShowAsContext();
                    }
                }
            }
            else if (settings.props.TryGet(placed.prop, out _) && GUILayout.Button(new GUIContent("Swap for box", "Replace it with a box filling its bounds, standing where it stands.")))
            {
                ZoneSwap.ForBox(placed, settings);
                GUIUtility.ExitGUI();
            }
        }

        // A copy's colors by role: a row for each role its group's parts play, showing the group's own color until
        // set; reset goes back to it. Roles the group no longer has are listed for removal.
        private static void RoleColors(ZonePlacement copy)
        {
            if (copy.EditingInPlace || ZoneGroups.Find(copy.group) is null)
            {
                return;
            }

            var settings = CometSettings.Find(quiet: true);
            var parts = Groups.Expand(new Placement { Group = copy.group }, ZoneGroups.Find);
            var roles = Groups.Roles(copy.group, ZoneGroups.Find);
            if (roles.Count == 0 && copy.roleColors.Count == 0)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(new GUIContent("Colors by role", "Recolors this copy's parts that play each role. Give a group's parts roles in the group itself."), EditorStyles.boldLabel);
            foreach (var role in roles.Concat(copy.roleColors.Select(c => c.role).Where(r => !roles.Contains(r))).ToList())
            {
                var index = copy.roleColors.FindIndex(c => c.role == role);
                var own = index >= 0;
                var part = parts.FirstOrDefault(p => p.Role == role);
                var groupColor = part?.Color is { } rgb ? PlacedColors.FromRgb(rgb) : settings != null ? settings.placedColor : Color.gray;
                var label = roles.Contains(role) ? role : $"{role} (not in {copy.group})";
                var (picked, changed, reset) = ColorFields.WithReset(new GUIContent(label, $"The color of the parts playing {role}. Reset: the group's own."), own ? copy.roleColors[index].color : groupColor, own);
                if (changed)
                {
                    Undo.RecordObject(copy, $"Color {role}");
                    var entry = new ZonePlacement.RoleColor { role = role, color = picked };
                    if (own)
                    {
                        copy.roleColors[index] = entry;
                    }
                    else
                    {
                        copy.roleColors.Add(entry);
                    }
                }
                else if (reset)
                {
                    Undo.RecordObject(copy, $"Reset {role} color");
                    copy.roleColors.RemoveAt(index);
                }

                if (changed || reset)
                {
                    EditorUtility.SetDirty(copy);
                    ZonePlacement.GroupDrawer?.Invoke(copy);
                }
            }
        }

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
    }
}
