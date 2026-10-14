using UnityEngine;

namespace ShapeLand.Client
{
    /// <summary>Conversions between System.Numerics vectors (shared code's) and Unity's.</summary>
    public static class Vectors
    {
        public static Vector3 ToUnity(System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);

        public static System.Numerics.Vector3 ToNumerics(Vector3 v) => new System.Numerics.Vector3(v.x, v.y, v.z);
    }
}
