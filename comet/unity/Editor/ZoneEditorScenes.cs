using System;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Comet.Unity.Editor
{
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
                if (
                    state == PlayModeStateChange.ExitingEditMode
                    && SceneManager.GetActiveScene().name.StartsWith("InitTestScene", StringComparison.Ordinal)
                )
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

            ObjectChangeEvents.changesPublished += FreshTriggerIds;

            // The asset database isn't ready while scripts load. This runs after every script reload, which can lose
            // the terrain preview.
            EditorApplication.delayCall += () =>
            {
                SetPlayScene(CometSettings.Find(quiet: true));
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    for (var i = 0; i < SceneManager.sceneCount; i++)
                    {
                        Redraw(SceneManager.GetSceneAt(i));
                    }
                }
            };
        }

        internal static bool Quiet { get; set; }

        public static void SetPlayScene(CometSettings? settings)
        {
            if (settings != null)
            {
                EditorSceneManager.playModeStartScene = settings.playScene;
            }
        }

        private static void FreshTriggerIds(ref ObjectChangeEventStream stream)
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

                foreach (var spawn in root.GetComponentsInChildren<ZoneTrigger>(true))
                {
                    var zone = spawn.GetComponentInParent<ZoneEditing>(true);
                    if (zone == null)
                    {
                        continue;
                    }

                    var others = zone.GetComponentsInChildren<ZoneTrigger>(true).Where(s => s != spawn).Select(s => s.id).ToHashSet();
                    if (!others.Contains(spawn.id))
                    {
                        continue;
                    }

                    var stem = Regex.Replace(spawn.id, "_[0-9]+$", "");
                    var n = 2;
                    while (others.Contains($"{stem}_{n}"))
                    {
                        n++;
                    }

                    Undo.RecordObject(spawn, "Fresh id");
                    Undo.RecordObject(spawn.gameObject, "Fresh id");
                    spawn.id = $"{stem}_{n}";
                    spawn.gameObject.name = spawn.id;
                    if (spawn.Pad is { } pad)
                    {
                        Undo.RecordObject(pad.gameObject, "Fresh id");
                        pad.gameObject.name = $"{spawn.id} pad";
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
                foreach (var editing in root.GetComponentsInChildren<ZoneEditing>(true))
                {
                    yield return editing;
                }
            }
        }
    }
}
