using UnityEditor;

namespace Comet.Unity.Editor
{
    /// <summary>Holds script reloads in play mode: a reload loses unserialized state (input actions, connections) and breaks every frame.</summary>
    [InitializeOnLoad]
    internal static class PlayModeReloads
    {
        private static bool _held;

        static PlayModeReloads() =>
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && !_held)
                {
                    EditorApplication.LockReloadAssemblies();
                    _held = true;
                }
                else if (state is PlayModeStateChange.ExitingPlayMode or PlayModeStateChange.EnteredEditMode && _held)
                {
                    _held = false;
                    EditorApplication.UnlockReloadAssemblies();
                }
            };
    }
}
