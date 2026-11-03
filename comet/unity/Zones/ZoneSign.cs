using UnityEngine;

namespace Comet.Unity
{
    /// <summary>A sign in a zone opened in the editor: text shown over the object's position.</summary>
    [DisallowMultipleComponent]
    public sealed class ZoneSign : MonoBehaviour
    {
        [TextArea(2, 6)]
        [Tooltip("The text, plain; line breaks allowed. At most 200 characters.")]
        public string text = "";

        public Color textColor = Color.white;

        [Range(8, 48)]
        [Tooltip("The text's size, in the game's interface pixels.")]
        public float fontSize = 13;

        [Tooltip("Draw a panel behind the text.")]
        public bool background = true;

        public Color backgroundColor = new Color32(0x12, 0x16, 0x1E, 0xFF);

        [Range(0, 1)]
        public float backgroundOpacity = 0.75f;

        [Tooltip("A little arrow under the panel pointing at the sign's point, as speech bubbles have. Needs the background.")]
        public bool arrow;

        [Range(1, 200)]
        [Tooltip("How far away it's shown, in meters.")]
        public float showDistance = 45;

        private void OnDrawGizmos()
        {
            Gizmos.color = textColor;
            Gizmos.DrawWireSphere(transform.position, 0.15f);
#if UNITY_EDITOR
            // Its text in its colors, so level editors see it without entering play mode.
            var style = new GUIStyle(UnityEditor.EditorStyles.label) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(6, 6, 2, 2), fontSize = Mathf.RoundToInt(fontSize) };
            style.normal.textColor = textColor;
            style.normal.background = background ? Texture2D.whiteTexture : null;
            var saved = GUI.backgroundColor;
            GUI.backgroundColor = new Color(backgroundColor.r, backgroundColor.g, backgroundColor.b, backgroundOpacity);
            UnityEditor.Handles.Label(transform.position, text.Length > 0 ? text : "(empty sign)", style);
            GUI.backgroundColor = saved;
#endif
        }
    }
}
