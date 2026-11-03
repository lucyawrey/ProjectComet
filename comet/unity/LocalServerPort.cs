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

        /// <summary>The editor session value holding the local Login server's port.</summary>
        public const string LoginKey = "Comet.LocalServer.LoginPort";

        /// <summary>The local Login server's port, in the editor once the local server's settings are known; null otherwise.</summary>
        public static int? LoginInEditor
        {
            get
            {
#if UNITY_EDITOR
                var port = UnityEditor.SessionState.GetInt(LoginKey, 0);
                return port > 0 ? port : (int?)null;
#else
                return null;
#endif
            }
        }

        /// <summary>
        /// Whether play mode joins with accounts (Comet › Server › Use Accounts, off by default and kept per computer in
        /// the editor's preferences); always true outside the editor, where builds join with accounts.
        /// </summary>
        public static bool UseAccounts
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.EditorPrefs.GetBool(UseAccountsKey, false);
#else
                return true;
#endif
            }
        }

#if UNITY_EDITOR
        /// <summary>The editor preference holding <see cref="UseAccounts"/>, per Unity project (preferences are shared by every project).</summary>
        public static string UseAccountsKey => "Comet.LocalServer.UseAccounts." + System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath);
#endif

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
