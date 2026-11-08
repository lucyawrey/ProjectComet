using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Comet.Content;
using Comet.ZoneFiles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Comet.Unity.Editor
{
    public static partial class ZoneEditor
    {
        [MenuItem("Comet/Zones/New Zone…", priority = 0)]
        public static void NewMenu()
        {
            var settings = CometSettings.Find(quiet: false);
            if (settings == null || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var folder = PickNewZoneFolder("New zone", settings);
            if (folder != null)
            {
                ZoneFolder.Write(folder, new Zone { Id = "zone." + Path.GetFileName(folder) });
                Open(folder, settings);
            }
        }

        [MenuItem("Comet/Zones/New Zone…", true)]
        private static bool CanNew() => !EditorApplication.isPlaying;

        [MenuItem("Comet/Zones/Open…", priority = 1)]
        public static void OpenMenu()
        {
            var settings = CometSettings.Find(quiet: false);
            if (settings == null)
            {
                return;
            }

            var picked = EditorUtility.OpenFolderPanel("Open zone", ProjectPath(settings.zonesFolder), "");
            if (string.IsNullOrEmpty(picked) || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Open(picked, settings);
        }

        // Zones open into a scene of their own, which play mode can't do.
        [MenuItem("Comet/Zones/Open…", true)]
        private static bool CanOpen() => !EditorApplication.isPlaying;

        // Newest first: folders relative to the project, kept per project.
        private const int RecentCount = 8;
        private static string RecentKey => "Comet.Zones.Recent." + ProjectRoot;

        internal static List<string> Recent() =>
            EditorPrefs
                .GetString(RecentKey, "")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(f => Directory.Exists(ProjectPath(f)))
                .ToList();

        private static void Remember(string folder)
        {
            var recent = Recent();
            recent.Remove(folder);
            recent.Insert(0, folder);
            EditorPrefs.SetString(RecentKey, string.Join("\n", recent.Take(RecentCount)));
        }

        // A menu can't list items that change, and a pop-up menu can't open from one, so this opens a small window.
        [MenuItem("Comet/Zones/Open Recent…", priority = 2)]
        public static void OpenRecentMenu() => RecentZonesWindow.Show();

        [MenuItem("Comet/Zones/Open Recent…", true)]
        private static bool CanOpenRecent() => !EditorApplication.isPlaying;

        internal static void OpenRecent(string folder)
        {
            var settings = CometSettings.Find(quiet: false);
            if (settings != null && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Open(ProjectPath(folder), settings);
            }
        }

        public static event Action<ZoneEditing>? Saved;

        [MenuItem("Comet/Zones/Save", priority = 3)]
        public static void SaveMenu()
        {
            foreach (var scene in UnityEngine.Object.FindObjectsByType<ZoneEditing>().Select(z => z.gameObject.scene).Distinct())
            {
                EditorSceneManager.SaveScene(scene);
            }
        }

        [MenuItem("Comet/Zones/Save", true)]
        private static bool CanSave() => UnityEngine.Object.FindAnyObjectByType<ZoneEditing>() != null;

        /// <summary>
        /// Saves the opened zone into a new folder as a zone named after it, then opens that. The zone's own files
        /// keep their last save.
        /// </summary>
        [MenuItem("Comet/Zones/Save As…", priority = 4)]
        public static void SaveAsMenu()
        {
            var editing = UnityEngine.Object.FindAnyObjectByType<ZoneEditing>();
            var settings = CometSettings.Find(quiet: false);
            if (editing == null || settings == null)
            {
                return;
            }

            var folder = PickNewZoneFolder("Save zone as", settings);
            if (folder == null)
            {
                return;
            }

            var zone = Gather(editing);
            ZoneRename.Rename(zone, "zone." + Path.GetFileName(folder));
            if (!Write(zone, folder))
            {
                RemoveIfEmpty(folder);
                return;
            }

            if (Open(folder, settings))
            {
                Saved?.Invoke(UnityEngine.Object.FindAnyObjectByType<ZoneEditing>()!);
            }
        }

        [MenuItem("Comet/Zones/Save As…", true)]
        private static bool CanSaveAs() =>
            !EditorApplication.isPlaying && UnityEngine.Object.FindAnyObjectByType<ZoneEditing>() is { IsGroup: false, HasZoneData: true };

        // A save-file dialog, unlike a folder one, lets the name be typed and creates nothing. Null if the pick was
        // canceled or refused (saying why).
        private static string? PickNewZoneFolder(string title, CometSettings settings)
        {
            var zones = ProjectPath(settings.zonesFolder).TrimEnd('/', '\\');
            var picked = EditorUtility.SaveFilePanel(title, zones, "", "");
            if (string.IsNullOrEmpty(picked))
            {
                return null;
            }

            picked = Path.GetFullPath(picked).TrimEnd('/', '\\');
            var name = Path.GetFileName(picked);
            var problem =
                Path.GetDirectoryName(picked) != zones ? $"Zones go directly in {settings.zonesFolder}."
                : !ZoneValidation.IsName(name) ? $"'{name}' isn't a zone name: use lowercase letters, digits and underscores, starting with a letter."
                : Directory.Exists(picked) && Directory.EnumerateFileSystemEntries(picked).Any() ? $"{name} already has files in it."
                : null;
            if (problem == null)
            {
                return picked;
            }

            EditorUtility.DisplayDialog(title, problem, "OK");
            return null;
        }

        private static void RemoveIfEmpty(string folder)
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }

        [MenuItem("Comet/Zones/Discard Changes", priority = 5)]
        public static void DiscardMenu()
        {
            var editing = UnityEngine.Object.FindAnyObjectByType<ZoneEditing>();
            var settings = CometSettings.Find(quiet: false);
            if (editing == null || settings == null)
            {
                return;
            }

            var what = editing.IsGroup ? editing.groupId : Path.GetFileName(editing.folder);
            if (
                !EditorUtility.DisplayDialog(
                    "Discard changes",
                    $"Discard every change to {what} since it was last saved, and read it again from its files? This can't be undone.",
                    "Discard",
                    "Keep"
                )
            )
            {
                return;
            }

            if (editing.IsGroup)
            {
                ZoneGroups.Open(editing.groupId, settings);
            }
            else
            {
                Open(ProjectPath(editing.folder), settings);
            }
        }

        [MenuItem("Comet/Zones/Discard Changes", true)]
        private static bool CanDiscard() => !EditorApplication.isPlaying && UnityEngine.Object.FindAnyObjectByType<ZoneEditing>() != null;
    }
}
