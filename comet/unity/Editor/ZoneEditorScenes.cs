using System;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Comet.Unity.Editor
{
    /// <summary>
    /// Ties the zone editor to Unity's own scene workflow: saving a zone's working scene (File › Save, Ctrl+S) saves
    /// the zone's files, a zone's terrain preview is drawn again when its scene opens or play mode ends, and play mode
    /// always starts in the game's own scene (the settings' Play Scene), so it plays the zone from content. A spawn
    /// point that's duplicated or pasted gets a fresh key, and isn't the default.
    /// </summary>
    [InitializeOnLoad]
    public static class ZoneEditorScenes
    {
        static ZoneEditorScenes()
        {
            EditorSceneManager.sceneSaved += Saved;
            EditorSceneManager.sceneOpened += (scene, _) => Redraw(scene);
            EditorApplication.playModeStateChanged += state =>
            {
                // The test runner enters play mode from a scene of its own, which must stay the one that loads.
                if (state == PlayModeStateChange.ExitingEditMode && SceneManager.GetActiveScene().name.StartsWith("InitTestScene", StringComparison.Ordinal))
                {
                    EditorSceneManager.playModeStartScene = null;
                }

                if (state == PlayModeStateChange.EnteredEditMode)
                {
                    SetPlayScene(CometSettings.Find(quiet: true));
                    for (var i = 0; i < SceneManager.sceneCount; i++)
                    {
                        Redraw(SceneManager.GetSceneAt(i));
                    }
                }
            };

            ObjectChangeEvents.changesPublished += FreshTriggerKeys;

            // The asset database isn't ready while scripts load.
            EditorApplication.delayCall += () => SetPlayScene(CometSettings.Find(quiet: true));
        }

        /// <summary>While true, saving a scene doesn't save its zone (as when a zone is opened and its scene first saved).</summary>
        internal static bool Quiet { get; set; }

        /// <summary>Makes <paramref name="settings"/>' Play Scene the one play mode starts in.</summary>
        public static void SetPlayScene(CometSettings? settings)
        {
            if (settings != null)
            {
                EditorSceneManager.playModeStartScene = settings.playScene;
            }
        }

        // A new spawn point, teleporter or trigger (duplicated or pasted) whose key another already has gets the first free one: key_2, key_3…
        private static void FreshTriggerKeys(ref ObjectChangeEventStream stream)
        {
            for (var i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.CreateGameObjectHierarchy)
                {
                    continue;
                }

                stream.GetCreateGameObjectHierarchyEvent(i, out var created);
                if (!(EditorUtility.EntityIdToObject(created.entityId) is GameObject root))
                {
                    continue;
                }

                foreach (var spawn in root.GetComponentsInChildren<ZoneTrigger>())
                {
                    var zone = spawn.GetComponentInParent<ZoneEditing>();
                    if (zone == null)
                    {
                        continue;
                    }

                    var others = zone.GetComponentsInChildren<ZoneTrigger>().Where(s => s != spawn).Select(s => s.key).ToHashSet();
                    if (!others.Contains(spawn.key))
                    {
                        continue;
                    }

                    var stem = Regex.Replace(spawn.key, "_[0-9]+$", "");
                    var n = 2;
                    while (others.Contains($"{stem}_{n}"))
                    {
                        n++;
                    }

                    Undo.RecordObject(spawn, "Fresh key");
                    Undo.RecordObject(spawn.gameObject, "Fresh key");
                    spawn.key = $"{stem}_{n}";
                    spawn.gameObject.name = spawn.key;
                    foreach (var marker in spawn.GetComponentsInChildren<ZonePlacement>())
                    {
                        Undo.RecordObject(marker, "Fresh spawn key");
                        Undo.RecordObject(marker.gameObject, "Fresh spawn key");
                        marker.placementName = spawn.key;
                        marker.gameObject.name = $"{spawn.key} marker";
                    }
                }
            }
        }

        private static void Saved(Scene scene)
        {
            if (Quiet || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            foreach (var editing in Zones(scene))
            {
                ZoneEditor.Save(editing);
            }
        }

        private static void Redraw(Scene scene)
        {
            foreach (var editing in Zones(scene))
            {
                ZoneEditor.RedrawIfMissing(editing);
            }
        }

        private static System.Collections.Generic.IEnumerable<ZoneEditing> Zones(Scene scene)
        {
            if (!scene.isLoaded)
            {
                yield break;
            }

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var editing in root.GetComponentsInChildren<ZoneEditing>())
                {
                    yield return editing;
                }
            }
        }
    }
}
