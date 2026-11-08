using UnityEngine;

namespace Comet.Unity
{
    /// <summary>Comet's <c>System.Numerics</c> vectors and 0xRRGGBB colors as Unity's, and back.</summary>
    public static class Conversions
    {
        public static Vector3 ToUnity(System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);

        public static System.Numerics.Vector3 ToNumerics(Vector3 v) => new System.Numerics.Vector3(v.x, v.y, v.z);

        public static Color FromRgb(uint rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

        public static uint ToRgb(Color color)
        {
            Color32 c = color;
            return (uint)(c.r << 16 | c.g << 8 | c.b);
        }
    }
}
