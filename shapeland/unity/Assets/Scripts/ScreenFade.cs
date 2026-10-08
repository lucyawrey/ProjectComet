using UnityEngine;

namespace ShapeLand.Client
{
    /// <summary>A black overlay over the whole screen, for the respawn fade.</summary>
    public sealed class ScreenFade : MonoBehaviour
    {
        /// <summary>0 clear, 1 black.</summary>
        public float Darkness { get; set; }

        private void OnGUI()
        {
            if (Darkness <= 0 || Event.current.type != EventType.Repaint)
            {
                return;
            }

            GUI.depth = -1000;
            GUI.color = new Color(0, 0, 0, Darkness);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        }
    }
}
