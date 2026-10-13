using UnityEngine;

namespace ShapeLand.Client
{
    /// <summary>A black overlay over the whole screen, for the respawn fade.</summary>
    public sealed class ScreenFade : MonoBehaviour
    {
        private float _darkness;

        /// <summary>0 clear, 1 black. While clear the component is disabled, so IMGUI skips it every frame.</summary>
        public float Darkness
        {
            get => _darkness;
            set
            {
                _darkness = value;
                enabled = value > 0;
            }
        }

        private void Awake() => enabled = _darkness > 0;

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
