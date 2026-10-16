namespace ShapeLand.Client
{
    /// <summary>
    /// Reloading the web page to pick up new content (Plugins/WebGL/ShapeLandPage.jslib). The page remembers, for
    /// this tab only, that it reloaded to rejoin, so the reloaded game joins again by itself. Elsewhere (the editor,
    /// desktop builds) there's no page to reload.
    /// </summary>
    public static class WebPage
    {
        /// <summary>Reloads the page, to rejoin once it's back; false where there's no page.</summary>
        public static bool Reload()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            ShapeLand_ReloadToRejoin();
            return true;
#else
            return false;
#endif
        }

        /// <summary>True, once, after the page reloaded to rejoin.</summary>
        public static bool TakeRejoin()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return ShapeLand_TakeRejoin() != 0;
#else
            return false;
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void ShapeLand_ReloadToRejoin();

        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern int ShapeLand_TakeRejoin();
#endif
    }
}
