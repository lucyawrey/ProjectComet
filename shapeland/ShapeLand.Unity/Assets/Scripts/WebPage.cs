using UnityEngine;

namespace ShapeLand.Client
{
    public static class WebPage
    {
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

        public static bool IsPhone
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return _isPhone ??= ShapeLand_IsPhone() != 0;
#elif UNITY_EDITOR
                return UnityEngine.Device.SystemInfo.deviceType == DeviceType.Handheld;
#else
                return false;
#endif
            }
        }

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

        public static bool KeyboardUp
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return ShapeLand_KeyboardUp() != 0;
#else
                return false;
#endif
            }
        }

        public static (float Top, float Right, float Bottom, float Left) SafeArea()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return (ShapeLand_SafeAreaInset(0), ShapeLand_SafeAreaInset(1), ShapeLand_SafeAreaInset(2), ShapeLand_SafeAreaInset(3));
#elif UNITY_EDITOR
            // The Device Simulator's screen; its safe area is measured from the bottom left.
            var area = UnityEngine.Device.Screen.safeArea;
            float width = UnityEngine.Device.Screen.width,
                height = UnityEngine.Device.Screen.height;
            return width > 0 && height > 0
                ? ((height - area.yMax) / height, (width - area.xMax) / width, area.yMin / height, area.xMin / width)
                : (0, 0, 0, 0);
#else
            return (0, 0, 0, 0);
#endif
        }

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
        private static extern int ShapeLand_KeyboardUp();

        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern float ShapeLand_SafeAreaInset(int side);

        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern int ShapeLand_Playing();
#endif
    }
}
