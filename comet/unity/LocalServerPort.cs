namespace Comet.Unity
{
    /// <summary>
    /// The port of the editor's local game server (the Comet settings' server port), so play mode joins it whatever
    /// port it's on; set by the editor (Comet › Server). Null outside the editor.
    /// </summary>
    public static class LocalServerPort
    {
        /// <summary>The editor session value holding it.</summary>
        public const string Key = "Comet.LocalServer.Port";

        /// <summary>The port, in the editor once the local server's settings are known; null otherwise.</summary>
        public static int? InEditor
        {
            get
            {
#if UNITY_EDITOR
                var port = UnityEditor.SessionState.GetInt(Key, 0);
                return port > 0 ? port : (int?)null;
#else
                return null;
#endif
            }
        }
    }
}
