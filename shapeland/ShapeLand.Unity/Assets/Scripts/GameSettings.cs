using UnityEngine;

namespace ShapeLand.Client
{
    /// <summary>
    /// The kind of input the player is using: the Esc menu shows its controls and prompts, and each turns the camera
    /// at its own speed (the mouse, the right stick or a drag).
    /// </summary>
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
        /// <summary>The lowest and highest camera speed, as a multiple of the camera's own turning speeds.</summary>
        public const float MinCameraSpeed = 0.25f, MaxCameraSpeed = 3f;

        private const string ShowThroughWallsKey = "showThroughWalls";
        private const string MenuTipSeenKey = "menuTipSeen";
        private const string AutoJoinKey = "autoJoin";

        /// <summary>Whether the player's own shape shows where something hides it; on unless turned off.</summary>
        public static bool ShowThroughWalls
        {
            get => PlayerPrefs.GetInt(ShowThroughWallsKey, 1) == 1;
            set => Save(() => PlayerPrefs.SetInt(ShowThroughWallsKey, value ? 1 : 0));
        }

        /// <summary>Whether a signed-in player goes straight into the game on launch; off shows the join card first.</summary>
        public static bool AutoJoin
        {
            get => PlayerPrefs.GetInt(AutoJoinKey, 1) == 1;
            set => Save(() => PlayerPrefs.SetInt(AutoJoinKey, value ? 1 : 0));
        }

        /// <summary>
        /// Whether the player has opened the Esc menu or hovered a HUD button's tip; until then the menu button's tip
        /// stays up.
        /// </summary>
        public static bool MenuTipSeen
        {
            get => PlayerPrefs.GetInt(MenuTipSeenKey, 0) == 1;
            set => Save(() => PlayerPrefs.SetInt(MenuTipSeenKey, value ? 1 : 0));
        }

        /// <summary>How fast <paramref name="input"/> turns the camera, as a multiple of its usual speed (1).</summary>
        public static float CameraSpeed(InputKind input) =>
            Mathf.Clamp(PlayerPrefs.GetFloat(CameraSpeedKey(input), 1), MinCameraSpeed, MaxCameraSpeed);

        /// <summary>Sets how fast <paramref name="input"/> turns the camera, clamped to the allowed range.</summary>
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
