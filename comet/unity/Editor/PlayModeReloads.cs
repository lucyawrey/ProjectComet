using UnityEditor;

namespace Comet.Unity.Editor
{
    /// <summary>
    /// Holds script reloads while in play mode, so changes compile when play stops: a reload during play keeps the game
    /// running without what wasn't serialized (its input actions, connections), and it breaks every frame.
    /// </summary>
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
