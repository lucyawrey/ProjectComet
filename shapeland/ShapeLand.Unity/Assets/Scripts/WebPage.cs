using UnityEngine;

namespace ShapeLand.Client
{
    /// <summary>
    /// What the web page says about where the game runs, and reloading it to pick up new content
    /// (Plugins/WebGL/ShapeLandPage.jslib). The page remembers, for this tab only, that it reloaded to rejoin, so the
    /// reloaded game joins again by itself. Desktop builds have no page. In the editor, Unity's Device Simulator set
    /// to a phone stands in for a phone page, to lay out the phone interface without a web build.
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

#if UNITY_WEBGL && !UNITY_EDITOR
        private static bool? _isPhone;
#endif

        /// <summary>Whether the page laid itself out for a phone: the game fills the screen, played by touch.</summary>
        public static bool IsPhone
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return _isPhone ??= ShapeLand_IsPhone() != 0; // a page doesn't stop being one
#elif UNITY_EDITOR
                return UnityEngine.Device.SystemInfo.deviceType == DeviceType.Handheld;
#else
                return false;
#endif
            }
        }

        /// <summary>Whether the page is playing, past its "Tap to play" card on a phone; true where there's no page.</summary>
        public static bool Playing
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return ShapeLand_Playing() != 0;
#else
                return true;
#endif
            }
        }

        /// <summary>
        /// The screen's safe area insets (what notches, rounded corners and the home bar cover), for the HUD to keep
        /// clear of: as shares of the screen's height (top, bottom) and width (left, right); zero where there's no page.
        /// </summary>
        public static (float Top, float Right, float Bottom, float Left) SafeArea()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return (ShapeLand_SafeAreaInset(0), ShapeLand_SafeAreaInset(1), ShapeLand_SafeAreaInset(2), ShapeLand_SafeAreaInset(3));
#elif UNITY_EDITOR
            // The Device Simulator's screen; its safe area is measured from the bottom left.
            var area = UnityEngine.Device.Screen.safeArea;
            float width = UnityEngine.Device.Screen.width, height = UnityEngine.Device.Screen.height;
            return width > 0 && height > 0 ? ((height - area.yMax) / height, (width - area.xMax) / width, area.yMin / height, area.xMin / width) : (0, 0, 0, 0);
#else
            return (0, 0, 0, 0);
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

        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern int ShapeLand_IsPhone();

        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern float ShapeLand_SafeAreaInset(int side);

        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern int ShapeLand_Playing();
#endif
    }
}
