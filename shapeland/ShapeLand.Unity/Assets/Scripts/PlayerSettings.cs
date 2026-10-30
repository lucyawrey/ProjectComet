using UnityEngine;

namespace ShapeLand.Client
{
    /// <summary>The ways of turning the camera, each with its own speed setting.</summary>
    public enum CameraInput
    {
        Mouse,
        Stick,
        Touch,
    }

    /// <summary>
    /// The player's settings, remembered between visits (PlayerPrefs), each saved as it changes: on the web only a
    /// save keeps it past closing the tab. The Esc menu's Settings page changes them.
    /// </summary>
    public static class PlayerSettings
    {
        /// <summary>The lowest and highest camera speed, as a multiple of the camera's own turning speeds.</summary>
        public const float MinCameraSpeed = 0.25f, MaxCameraSpeed = 3f;

        private const string ShowThroughWallsKey = "showThroughWalls";

        /// <summary>Whether the player's own shape shows where something hides it; on unless turned off.</summary>
        public static bool ShowThroughWalls
        {
            get => PlayerPrefs.GetInt(ShowThroughWallsKey, 1) == 1;
            set => Save(() => PlayerPrefs.SetInt(ShowThroughWallsKey, value ? 1 : 0));
        }

        /// <summary>How fast <paramref name="input"/> turns the camera, as a multiple of its usual speed (1).</summary>
        public static float CameraSpeed(CameraInput input) =>
            Mathf.Clamp(PlayerPrefs.GetFloat(CameraSpeedKey(input), 1), MinCameraSpeed, MaxCameraSpeed);

        /// <summary>Sets how fast <paramref name="input"/> turns the camera, clamped to the allowed range.</summary>
        public static void SetCameraSpeed(CameraInput input, float speed) =>
            Save(() => PlayerPrefs.SetFloat(CameraSpeedKey(input), Mathf.Clamp(speed, MinCameraSpeed, MaxCameraSpeed)));

        private static string CameraSpeedKey(CameraInput input) => "cameraSpeed" + input;

        private static void Save(System.Action set)
        {
            set();
            PlayerPrefs.Save();
        }
    }
}
