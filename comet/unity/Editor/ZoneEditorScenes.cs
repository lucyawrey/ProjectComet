using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Comet.Unity.Editor
{
    /// <summary>
    /// Ties the zone editor to Unity's own scene workflow: saving a zone's working scene (File › Save, Ctrl+S) saves
    /// the zone's files, a zone's terrain preview is drawn again when its scene opens or play mode ends, and play mode
    /// always starts in the game's own scene (the settings' Play Scene), so it plays the zone from content.
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
                if (state == PlayModeStateChange.EnteredEditMode)
                {
                    for (var i = 0; i < SceneManager.sceneCount; i++)
                    {
                        Redraw(SceneManager.GetSceneAt(i));
                    }
                }
            };

            // The asset database isn't ready while scripts load.
            EditorApplication.delayCall += () => SetPlayScene(CometSettings.Find(quiet: true));
        }

        /// <summary>While true, saving a scene doesn't save its zone (as when a zone is opened and its scene first saved).</summary>
        internal static bool Quiet { get; set; }

        /// <summary>Makes <paramref name="settings"/>' Play Scene the one play mode starts in.</summary>
        public static void SetPlayScene(CometSettings settings)
        {
            if (settings != null)
            {
                EditorSceneManager.playModeStartScene = settings.playScene;
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
