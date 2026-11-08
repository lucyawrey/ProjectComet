namespace Comet.Unity
{
    /// <summary>
    /// The port of the editor's local game server, so play mode joins it whatever port it's on. Set by the editor;
    /// null outside it.
    /// </summary>
    public static class LocalServerPort
    {
        public const string Key = "Comet.LocalServer.Port";

        public const string LoginKey = "Comet.LocalServer.LoginPort";

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
        /// Whether play mode joins with accounts: an editor preference per computer, off by default. True in builds.
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
        public static string UseAccountsKey => "Comet.LocalServer.UseAccounts." + System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath);

        /// <summary>The file the editor's server keeps its characters in without accounts: one per checkout.</summary>
        public static string DevCharactersFile =>
            System.IO.Path.Combine(
                CheckoutRoot(System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath)!),
                "artifacts",
                "dev-characters.json"
            );

        public static string CheckoutRoot(string folder)
        {
            for (var at = folder; at != null; at = System.IO.Path.GetDirectoryName(at))
            {
                var git = System.IO.Path.Combine(at, ".git");
                if (System.IO.Directory.Exists(git) || System.IO.File.Exists(git))
                {
                    return at;
                }
            }

            return folder;
        }
#endif

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
