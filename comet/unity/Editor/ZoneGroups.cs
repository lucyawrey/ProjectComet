using System.Collections.Generic;
using System.IO;
using System.Linq;
using Comet.Content;
using Comet.ZoneFiles;
using MessagePack;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Group = Comet.Content.Group;

namespace Comet.Unity.Editor
{
    /// <summary>
    /// Groups in the editor: the game's group files (Comet Settings' groups folder), copies drawn with their parts,
    /// saving a selection as a new group, unpacking a copy into loose placements, and editing a group in a scene of
    /// its own (its pivot at the origin), whose saving updates every copy in the open scenes.
    /// </summary>
    [InitializeOnLoad]
    public static class ZoneGroups
    {
        private const string PartsName = "Parts (preview)";
        private const string ReturnKey = "Comet.ZoneGroups.ReturnTo";

        // The group files, read on first use and again when the project changes or a group is saved.
        private static List<(Group Group, string File)>? _groups;

        static ZoneGroups()
        {
            // Objects can't be made during a scene's load: draw once it's done.
            ZonePlacement.GroupDrawer = copy => EditorApplication.delayCall += () => Draw(copy);
            ZonePlacement.MaterialFor = blockout => CometSettings.Find(quiet: true) is { } settings ? PlacedMaterial(settings, blockout) : null;
            EditorApplication.projectChanged += () => _groups = null;
        }

        /// <summary>Goes up each time the groups are read again, so pictures of them can be redrawn.</summary>
        public static int Version { get; private set; }

        /// <summary>Every group, by key.</summary>
        public static IReadOnlyList<Group> All => Load().Select(g => g.Group).ToList();

        /// <summary>The group with <paramref name="key"/>, or null.</summary>
        public static Group? Find(string key) => Load().FirstOrDefault(g => g.Group.Id == key).Group;

        /// <summary>The zone a group's scene returns to, if it was opened from one.</summary>
        public static string ReturnFolder => SessionState.GetString(ReturnKey, "");

        private static List<(Group Group, string File)> Load()
        {
            if (_groups != null)
            {
                return _groups;
            }

            var settings = CometSettings.Find(quiet: true);
            var errors = new List<ZoneFileError>();
            Version++;
            _groups = settings == null ? new List<(Group, string)>() : GroupFile.ReadFolder(ZoneEditor.ProjectPath(settings.groupsFolder), errors);
            foreach (var error in errors)
            {
                Debug.LogError($"Group file {error}");
            }

            return _groups;
        }

        /// <summary>Reads the group files again and redraws every copy in the open scenes.</summary>
        public static void Reload()
        {
            _groups = null;
            foreach (var copy in Object.FindObjectsByType<ZonePlacement>().Where(p => p.IsGroup))
            {
                Draw(copy);
            }

            SceneView.RepaintAll();
        }

        /// <summary>A new copy of the group <paramref name="key"/> under <paramref name="parent"/>, with its parts drawn.</summary>
        public static GameObject CreateCopy(string key, Transform parent)
        {
            var thing = new GameObject();
            thing.transform.SetParent(parent, false);
            thing.name = GameObjectUtility.GetUniqueNameForSibling(parent, key);
            var placement = thing.AddComponent<ZonePlacement>();
            placement.group = key;
            Draw(placement);
            return thing;
        }

        /// <summary>The meshes a group draws, in its own space, each with its color (null for a part that has none).</summary>
        public static List<(Mesh Mesh, Matrix4x4 Matrix, Color Color)> Meshes(string key, CometSettings settings)
        {
            var meshes = new List<(Mesh, Matrix4x4, Color)>();
            foreach (var part in Groups.Expand(new Placement { Group = key }, Find))
            {
                var r = part.Rotation;
                var color = PartColor(part, settings);
                if (part.Primitive != null)
                {
                    meshes.Add((PrimitiveMeshes.For(part, settings.Budget), Matrix4x4.TRS(ToUnity(part.Position), Quaternion.Euler(r.X, r.Y, r.Z), Vector3.one), color));
                }
                else if (settings.props != null && settings.props.Model(part.Prop) is { } model)
                {
                    var matrix = Matrix4x4.TRS(ToUnity(part.Position), Quaternion.Euler(r.X, r.Y, r.Z), ToUnity(part.Scale)) * model.transform.worldToLocalMatrix;
                    foreach (var filter in model.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh != null && !f.name.EndsWith(Prop.CollisionSuffix, System.StringComparison.Ordinal)))
                    {
                        meshes.Add((filter.sharedMesh, matrix * filter.transform.localToWorldMatrix, color));
                    }
                }
            }

            return meshes;
        }

        /// <summary>The material placed objects are drawn in: the blockout one for blockout, if the settings name one.</summary>
        public static Material? PlacedMaterial(CometSettings settings, bool blockout) =>
            blockout && settings.blockoutMaterial != null ? settings.blockoutMaterial : settings.placedMaterial;

        // A part's color: its own, else its prop's, else the game's.
        private static Color PartColor(Placement part, CometSettings settings) =>
            part.Color is { } own ? PlacedColors.FromRgb(own)
            : part.Primitive == null && settings.props != null && settings.props.TryGet(part.Prop, out var entry) && entry.hasColor ? entry.color
            : settings.placedColor;

        /// <summary>Draws a copy's parts again now (after a change to it, such as its blockout flag).</summary>
        public static void Redraw(ZonePlacement copy) => Draw(copy);

        // Draws a copy's parts under it as uneditable objects the scene doesn't save, built again each time.
        private static void Draw(ZonePlacement copy)
        {
            if (copy == null || copy.EditingInPlace)
            {
                return;
            }

            var old = copy.transform.Find(PartsName);
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }

            var settings = CometSettings.Find(quiet: true);
            if (settings == null || !copy.IsGroup)
            {
                return;
            }

            var root = new GameObject(PartsName).transform;
            root.SetParent(copy.transform, false);
            var material = settings.placedMaterial != null ? settings.placedMaterial : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
            var parts = Groups.Expand(new Placement { Group = copy.group, Colors = ZoneEditor.PlacementOf(copy).Colors, Blockout = copy.blockout }, Find);
            if (parts.Count == 0)
            {
                Debug.LogWarning($"{copy.name}: there's no group '{copy.group}', or it has no parts.", copy);
            }

            foreach (var part in parts)
            {
                GameObject thing;
                var r = part.Rotation;
                if (part.Primitive is { } primitive)
                {
                    thing = new GameObject(PrimitiveKinds.Name(primitive.Kind));
                    var mesh = PrimitiveMeshes.For(part, settings.Budget);
                    thing.AddComponent<MeshFilter>().sharedMesh = mesh;
                    thing.AddComponent<MeshRenderer>().sharedMaterial = PlacedMaterial(settings, part.Blockout) ?? material;
                    thing.AddComponent<MeshCollider>().sharedMesh = mesh; // for Unity's surface snapping
                    thing.transform.SetParent(root, false);
                    thing.transform.SetLocalPositionAndRotation(ToUnity(part.Position), Quaternion.Euler(r.X, r.Y, r.Z));
                }
                else
                {
                    var model = settings.props != null ? settings.props.Model(part.Prop) : null;
                    thing = model != null ? Object.Instantiate(model, root) : GameObject.CreatePrimitive(PrimitiveType.Cube);
                    thing.name = part.Prop;
                    thing.transform.SetParent(root, false);
                    thing.transform.SetLocalPositionAndRotation(ToUnity(part.Position), Quaternion.Euler(r.X, r.Y, r.Z));
                    thing.transform.localScale = ToUnity(part.Scale);
                    foreach (var renderer in thing.GetComponentsInChildren<Renderer>())
                    {
                        renderer.enabled = !renderer.name.EndsWith(Prop.CollisionSuffix, System.StringComparison.Ordinal);
                        renderer.sharedMaterial = PlacedMaterial(settings, part.Blockout) ?? material;
                    }
                }

                PlacedColors.Apply(thing, PartColor(part, settings));
            }

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
            }
        }

        /// <summary>
        /// Saves the selected placements of an opened zone (or group) as the new group <paramref name="key"/> and
        /// puts a copy of it in their place, its pivot at the bottom center of what was selected. Spawn markers stay.
        /// </summary>
        public static bool SaveSelection(ZoneEditing editing, string key, CometSettings settings)
        {
            var selected = Selection.gameObjects.Select(g => g.GetComponent<ZonePlacement>())
                .Where(p => p != null && p.IsLoose && p.GetComponentInParent<ZoneSpawn>() == null && p.GetComponentInParent<ZoneEditing>() == editing)
                .ToList();
            if (selected.Count == 0)
            {
                EditorUtility.DisplayDialog("Save as group", "Select the placed objects to make into a group first (spawn markers can't be in one).", "OK");
                return false;
            }

            if (!ZoneValidation.IsKey(key, "group") || Find(key) != null)
            {
                EditorUtility.DisplayDialog("Save as group", Find(key) != null ? $"There's already a group '{key}'." : "Pick a new key: 'group.' then lowercase letters, digits and underscores.", "OK");
                return false;
            }

            var renderers = selected.SelectMany(p => p.GetComponentsInChildren<Renderer>()).Where(r => r.enabled).ToList();
            var bounds = renderers.Count > 0 ? renderers[0].bounds : new Bounds(selected[0].transform.position, Vector3.zero);
            foreach (var r in renderers.Skip(1))
            {
                bounds.Encapsulate(r.bounds);
            }

            var pivot = new Vector3(Round(bounds.center.x), Round(bounds.min.y), Round(bounds.center.z));
            var group = new Group
            {
                Id = key,
                Parts = selected.Select(p =>
                {
                    var part = ZoneEditor.PlacementOf(p);
                    part.Position -= ToNumerics(pivot);
                    part.Name = null;
                    return part;
                }).ToArray(),
            };
            var file = GroupFile.FileFor(key);
            if (!Check(group, file, settings))
            {
                return false;
            }

            GroupFile.Write(Path.Combine(ZoneEditor.ProjectPath(settings.groupsFolder), file), group);
            _groups = null;

            Undo.SetCurrentGroupName($"Save as {key}");
            var undoGroup = Undo.GetCurrentGroup();
            var parent = selected[0].transform.parent;
            foreach (var p in selected)
            {
                Undo.DestroyObjectImmediate(p.gameObject);
            }

            var copy = CreateCopy(key, parent);
            copy.transform.position = pivot;
            Undo.RegisterCreatedObjectUndo(copy, $"Save as {key}");
            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeGameObject = copy;
            Debug.Log($"Saved {selected.Count} objects as {key} ({settings.groupsFolder}/{file}).");
            return true;
        }

        /// <summary>Replaces a copy with its group's parts as loose placements, one level only (nested groups stay copies).</summary>
        public static void Unpack(ZonePlacement copy, CometSettings settings)
        {
            if (Find(copy.group) is not { } group)
            {
                return;
            }

            Undo.SetCurrentGroupName($"Unpack {copy.group}");
            var undoGroup = Undo.GetCurrentGroup();
            var parent = copy.transform.parent;
            var made = Groups.Place(ZoneEditor.PlacementOf(copy), group).Select(part =>
            {
                var thing = ZoneEditor.CreatePlaced(part, parent, settings);
                Undo.RegisterCreatedObjectUndo(thing, $"Unpack {copy.group}");
                return thing;
            }).ToArray();
            Undo.DestroyObjectImmediate(copy.gameObject);
            Undo.CollapseUndoOperations(undoGroup);
            Selection.objects = made.Cast<Object>().ToArray();
        }

        /// <summary>Asks to save the open scenes' changes, then opens a group (<see cref="Open"/>), after the current GUI pass.</summary>
        public static void OpenLater(string key, CometSettings settings) => EditorApplication.delayCall += () =>
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Open(key, settings);
            }
        };

        /// <summary>Opens a group in a scene of its own, its pivot at the origin; saving it updates every copy.</summary>
        public static bool Open(string key, CometSettings settings)
        {
            var entry = Load().FirstOrDefault(g => g.Group.Id == key);
            if (entry.Group == null)
            {
                return false;
            }

            // Back to the zone it was opened from, if it was.
            if (Object.FindAnyObjectByType<ZoneEditing>() is { IsGroup: false } zone)
            {
                SessionState.SetString(ReturnKey, zone.folder);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            ZoneTerrainEditing.Forget();
            settings = CometSettings.Find(quiet: true) ?? settings;
            ZonePlacement.Budget = settings.Budget;
            var root = new GameObject($"Group {key}");
            var editing = root.AddComponent<ZoneEditing>();
            editing.groupKey = key;
            editing.folder = ZoneEditor.RelativeToProject(Path.Combine(ZoneEditor.ProjectPath(settings.groupsFolder), entry.File));
            editing.ZoneData = MessagePackSerializer.Serialize(new Zone { Id = "zone.group" }, ZoneEditor.Serializer);
            var placed = ZoneEditor.Child(root.transform, "Placed");
            foreach (var part in entry.Group.Parts)
            {
                ZoneEditor.CreatePlaced(part, placed, settings);
            }

            Directory.CreateDirectory(ZoneEditor.ProjectPath(settings.scenesFolder));
            ZoneEditorScenes.Quiet = true;
            try
            {
                EditorSceneManager.SaveScene(scene, $"{settings.scenesFolder}/{key.Replace('.', '_')}.unity");
            }
            finally
            {
                ZoneEditorScenes.Quiet = false;
            }

            Selection.activeGameObject = root;
            return true;
        }

        /// <summary>Writes a group being edited back to its file and redraws its copies; false (with the reasons shown) if it doesn't check out.</summary>
        public static bool Save(ZoneEditing editing)
        {
            var settings = CometSettings.Find(quiet: true);
            if (settings == null)
            {
                return false;
            }

            if (editing.GetComponentsInChildren<ZoneSpawn>().Length > 0)
            {
                Debug.LogWarning($"{editing.groupKey}: groups have no spawn points; the ones in this scene aren't saved.");
            }

            var group = new Group
            {
                Id = editing.groupKey,
                Parts = editing.GetComponentsInChildren<ZonePlacement>().Where(p => p.IsLoose && p.GetComponentInParent<ZoneSpawn>() == null).Select(p => ZoneEditor.PlacementOf(p)).ToArray(),
            };
            var path = ZoneEditor.ProjectPath(editing.folder);
            if (!Check(group, Path.GetRelativePath(ZoneEditor.ProjectPath(settings.groupsFolder), path).Replace('\\', '/'), settings))
            {
                return false;
            }

            GroupFile.Write(path, group);
            Reload();
            Debug.Log($"Saved {group.Id} to {editing.folder}.");
            return true;
        }

        /// <summary>The copy whose group is being edited in place, if one is.</summary>
        public static ZonePlacement? InPlace => Object.FindObjectsByType<ZonePlacement>().FirstOrDefault(p => p.EditingInPlace);

        /// <summary>
        /// Starts editing a copy's group in place: its parts become placements under it, edited like any other, and
        /// what's placed meanwhile goes into it. <see cref="SaveInPlace"/> writes them to the group; <see cref="CancelInPlace"/> drops them.
        /// </summary>
        public static void EditInPlace(ZonePlacement copy, CometSettings settings)
        {
            if (InPlace is { } other)
            {
                EditorUtility.DisplayDialog("Edit in place", $"{other.group} is being edited in place already: save or cancel that first.", "OK");
                return;
            }

            if (Find(copy.group) is not { } group)
            {
                return;
            }

            Undo.SetCurrentGroupName($"Edit {copy.group} in place");
            var undoGroup = Undo.GetCurrentGroup();
            Undo.RecordObject(copy, $"Edit {copy.group} in place");
            copy.EditingInPlace = true;
            var preview = copy.transform.Find(PartsName);
            if (preview != null)
            {
                Object.DestroyImmediate(preview.gameObject);
            }

            var made = new List<GameObject>();
            foreach (var part in group.Parts)
            {
                // Made at the part's place in the group, then put there under the copy, so the copy's transform applies.
                var thing = ZoneEditor.CreatePlaced(part, copy.transform, settings);
                var r = part.Rotation;
                thing.transform.SetLocalPositionAndRotation(ToUnity(part.Position), Quaternion.Euler(r.X, r.Y, r.Z));
                var placement = thing.GetComponent<ZonePlacement>();
                thing.transform.localScale = placement.isPrimitive && !placement.IsGroup ? Vector3.one : ToUnity(part.Scale);
                Undo.RegisterCreatedObjectUndo(thing, $"Edit {copy.group} in place");
                made.Add(thing);
            }

            Undo.CollapseUndoOperations(undoGroup);
            Selection.objects = made.Cast<Object>().ToArray();
            SceneView.RepaintAll();
        }

        /// <summary>Writes the parts of the copy being edited in place to its group, then shows every copy with them.</summary>
        public static bool SaveInPlace(CometSettings settings)
        {
            if (InPlace is not { } copy || Load().FirstOrDefault(g => g.Group.Id == copy.group) is not { Group: not null } entry)
            {
                return false;
            }

            var parts = copy.GetComponentsInChildren<ZonePlacement>().Where(p => p != copy && p.transform.parent.GetComponentInParent<ZonePlacement>() == copy).ToList();
            var group = new Group { Id = copy.group, Parts = parts.Select(p => ZoneEditor.PlacementOf(p, copy.transform)).ToArray() };
            if (!Check(group, entry.File, settings))
            {
                return false;
            }

            GroupFile.Write(Path.Combine(ZoneEditor.ProjectPath(settings.groupsFolder), entry.File), group);
            EndInPlace(copy, $"Save {copy.group}");
            Debug.Log($"Saved {group.Id} ({settings.groupsFolder}/{entry.File}).");
            return true;
        }

        /// <summary>Stops editing in place, dropping the changes: the copy shows its group as saved.</summary>
        public static void CancelInPlace()
        {
            if (InPlace is { } copy)
            {
                EndInPlace(copy, $"Cancel editing {copy.group}");
            }
        }

        private static void EndInPlace(ZonePlacement copy, string undoName)
        {
            Undo.SetCurrentGroupName(undoName);
            var undoGroup = Undo.GetCurrentGroup();
            foreach (var part in copy.GetComponentsInChildren<ZonePlacement>().Where(p => p != copy && p.transform.parent.GetComponentInParent<ZonePlacement>() == copy).ToList())
            {
                Undo.DestroyObjectImmediate(part.gameObject);
            }

            Undo.RecordObject(copy, undoName);
            copy.EditingInPlace = false;
            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeGameObject = copy.gameObject;
            Reload();
        }

        // The bar shown in the Scene view while a group is edited in place.
        [InitializeOnLoadMethod]
        private static void InPlaceBar() => SceneView.duringSceneGui += view =>
        {
            if (Event.current.type != EventType.Layout && Event.current.type != EventType.Repaint && Event.current.type != EventType.MouseDown && Event.current.type != EventType.MouseUp)
            {
                return;
            }

            if (InPlace is not { } copy || CometSettings.Find(quiet: true) is not { } settings)
            {
                return;
            }

            Handles.BeginGUI();
            var width = 420f;
            GUILayout.BeginArea(new Rect((view.position.width - width) / 2, 8, width, 30), EditorStyles.helpBox);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Editing {copy.group} in place", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Save group", GUILayout.Width(100)))
            {
                SaveInPlace(settings);
            }

            if (GUILayout.Button("Cancel", GUILayout.Width(70)))
            {
                CancelInPlace();
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            Handles.EndGUI();
        };

        // Checks a new or changed group against the others: the problems in it, or in a loop it would make.
        /// <summary>
        /// Deletes the group <paramref name="key"/>'s file, after asking. Copies in the open zone or group are unpacked
        /// into loose placements and that scene saved first, if the person agrees; any other use (another zone, another
        /// group, a game content file naming it) stops the delete and is listed. Its id stays reserved. Not undoable
        /// (version control keeps the file).
        /// </summary>
        public static void Delete(string key, ZoneEditing editing, CometSettings settings)
        {
            if (Load().FirstOrDefault(g => g.Group.Id == key) is not { Group: not null } entry)
            {
                return;
            }

            if (InPlace != null)
            {
                EditorUtility.DisplayDialog("Delete group", "Finish editing a group in place first (Save group or Cancel).", "OK");
                return;
            }

            var here = editing.GetComponentsInChildren<ZonePlacement>().Where(p => p.IsLoose && p.group == key).ToList();
            var openFile = editing.IsGroup ? Load().FirstOrDefault(g => g.Group.Id == editing.groupKey).File : null;
            var elsewhere = Uses(key, settings, openZone: editing.IsGroup ? null : ZoneEditor.ProjectPath(editing.folder), openGroupFile: openFile);
            if (elsewhere.Count > 0)
            {
                EditorUtility.DisplayDialog("Delete group", $"{key} is still used, so it wasn't deleted:\n\n{string.Join("\n", elsewhere.Take(12))}"
                    + (elsewhere.Count > 12 ? $"\n…and {elsewhere.Count - 12} more" : "") + "\n\nRemove or unpack those first.", "OK");
                return;
            }

            // The open scene's saved files may use it too (copies removed but not saved yet): saving fixes those.
            var saved = editing.IsGroup
                ? Find(editing.groupKey)?.Parts.Any(p => p.Group == key) == true
                : ZoneFolder.Read(ZoneEditor.ProjectPath(editing.folder), new List<ZoneFileError>())?.Chunks.Any(c => c.Placements.Any(p => p.Group == key)) == true;
            var what = editing.IsGroup ? $"the group {editing.groupKey}" : "this zone";
            var message = $"Delete {key}? Its file ({entry.File}) is deleted; this can't be undone here. Its id stays reserved, never reused.";
            if (here.Count > 0)
            {
                message += $"\n\n{here.Count} {(here.Count == 1 ? "copy" : "copies")} in {what} will be unpacked into loose placements, and {what} saved.";
            }
            else if (saved)
            {
                message += $"\n\n{what} is saved first: its saved files still use {key}.";
            }

            if (!EditorUtility.DisplayDialog("Delete group", message, "Delete", "Cancel"))
            {
                return;
            }

            if (here.Count > 0 || saved)
            {
                foreach (var copy in here)
                {
                    Unpack(copy, settings);
                }

                if (!(editing.IsGroup ? Save(editing) : ZoneEditor.Save(editing)))
                {
                    Debug.LogWarning($"{key} wasn't deleted: {what} didn't save.");
                    return;
                }
            }

            File.Delete(Path.Combine(ZoneEditor.ProjectPath(settings.groupsFolder), entry.File));
            Reload();
            Debug.Log($"Deleted {key} ({entry.File}).");
        }

        // Where a group is used, outside the scene that's open: zone files, other groups' parts, and the game's other
        // content files that name its key (such as a look).
        private static List<string> Uses(string key, CometSettings settings, string? openZone, string? openGroupFile)
        {
            var uses = new List<string>();
            foreach (var (group, file) in Load().Where(g => g.Group.Id != key && g.File != openGroupFile))
            {
                if (group.Parts.Any(p => p.Group == key))
                {
                    uses.Add($"the group {group.Id} ({file})");
                }
            }

            var zones = ZoneEditor.ProjectPath(settings.zonesFolder);
            if (Directory.Exists(zones))
            {
                foreach (var folder in Directory.GetDirectories(zones).Where(f => openZone == null || Path.GetFullPath(f) != Path.GetFullPath(openZone)))
                {
                    var zone = ZoneFolder.Read(folder, new List<ZoneFileError>());
                    var count = zone?.Chunks.Sum(c => c.Placements.Count(p => p.Group == key)) ?? 0;
                    if (count > 0)
                    {
                        uses.Add($"the zone {Path.GetFileName(folder)} ({count} {(count == 1 ? "copy" : "copies")})");
                    }
                }
            }

            // The game's other content (the folder holding the groups folder), by a search for the quoted key.
            var groups = ZoneEditor.ProjectPath(settings.groupsFolder) + Path.DirectorySeparatorChar;
            var content = Path.GetDirectoryName(groups.TrimEnd(Path.DirectorySeparatorChar));
            if (content != null && Directory.Exists(content))
            {
                foreach (var file in Directory.EnumerateFiles(content, "*.toml", SearchOption.AllDirectories)
                    .Where(f => !f.StartsWith(groups, System.StringComparison.Ordinal) && !f.StartsWith(zones + Path.DirectorySeparatorChar, System.StringComparison.Ordinal)))
                {
                    if (File.ReadAllText(file).Contains($"\"{key}\""))
                    {
                        uses.Add(Path.GetRelativePath(content, file).Replace('\\', '/'));
                    }
                }
            }

            return uses;
        }

        private static bool Check(Group group, string file, CometSettings settings)
        {
            var groups = Load().Where(g => g.Group.Id != group.Id).Append((group, file)).ToList();
            var errors = ZoneValidation.CheckGroups(groups, key => settings.props != null ? ZoneEditor.PropFor(settings.props, key) : null)
                .Where(e => e.File == file || e.Message.Contains(group.Id))
                .ToList();
            if (errors.Count > 0)
            {
                ZoneEditor.Report($"Didn't save {group.Id}", errors);
                return false;
            }

            return true;
        }

        private static float Round(float value) => Mathf.Round(value * 1000) / 1000;

        private static Vector3 ToUnity(System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);

        private static System.Numerics.Vector3 ToNumerics(Vector3 v) => new System.Numerics.Vector3(v.x, v.y, v.z);
    }
}
