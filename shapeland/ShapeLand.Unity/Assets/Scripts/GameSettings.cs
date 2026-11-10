using UnityEngine;

namespace ShapeLand.Client
{
    public enum InputKind
    {
        KeyboardMouse,
        Gamepad,
        Touch,
    }

    /// <summary>
    /// The player's settings, remembered between visits (PlayerPrefs), each saved as it changes: on the web only a
    /// save keeps it past closing the tab. The Esc menu's Settings page changes them.
    /// </summary>
    public static class GameSettings
    {
        public const float MinCameraSpeed = 0.25f,
            MaxCameraSpeed = 3f;

        private const string ShowThroughWallsKey = "showThroughWalls";
        private const string MenuTipSeenKey = "menuTipSeen";
        private const string AutoJoinKey = "autoJoin";

        public static bool ShowThroughWalls
        {
            get => PlayerPrefs.GetInt(ShowThroughWallsKey, 1) == 1;
            set => Save(() => PlayerPrefs.SetInt(ShowThroughWallsKey, value ? 1 : 0));
        }

        public static bool AutoJoin
        {
            get => PlayerPrefs.GetInt(AutoJoinKey, 1) == 1;
            set => Save(() => PlayerPrefs.SetInt(AutoJoinKey, value ? 1 : 0));
        }

        public static bool MenuTipSeen
        {
            get => PlayerPrefs.GetInt(MenuTipSeenKey, 0) == 1;
            set => Save(() => PlayerPrefs.SetInt(MenuTipSeenKey, value ? 1 : 0));
        }

        public static float CameraSpeed(InputKind input) =>
            Mathf.Clamp(PlayerPrefs.GetFloat(CameraSpeedKey(input), 1), MinCameraSpeed, MaxCameraSpeed);

        public static void SetCameraSpeed(InputKind input, float speed) =>
            Save(() => PlayerPrefs.SetFloat(CameraSpeedKey(input), Mathf.Clamp(speed, MinCameraSpeed, MaxCameraSpeed)));

        private static string CameraSpeedKey(InputKind input) => "cameraSpeed" + input;

        private static void Save(System.Action set)
        {
            set();
            PlayerPrefs.Save();
        }
    }
}
