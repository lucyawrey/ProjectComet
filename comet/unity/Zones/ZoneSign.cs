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

        [Tooltip("One size on screen at any distance, like a name tag, rather than sized in the world (smaller further away).")]
        public bool screenSized;

        [Range(0.05f, 5)]
        [Tooltip("A world-sized sign's text size: the height of its font, in meters.")]
        public float textHeight = 0.3f;

        [Range(8, 48)]
        [Tooltip("A screen-sized sign's text size, in the game's interface pixels.")]
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
            DrawLabel(Camera.current, 1);
#endif
        }

#if UNITY_EDITOR
        // A turned-off sign gets no gizmo calls, so the Scene view draws it faded itself: kept, but not in the game.
        [UnityEditor.InitializeOnLoadMethod]
        private static void DrawTurnedOff() =>
            UnityEditor.SceneView.duringSceneGui += view =>
            {
                if (Event.current.type != EventType.Repaint)
                {
                    return;
                }

                // Only the open zones' signs, and only those turned off themselves, which are what's saved as off.
                foreach (var zone in FindObjectsByType<ZoneEditing>(FindObjectsSortMode.None))
                {
                    foreach (var sign in zone.GetComponentsInChildren<ZoneSign>(true))
                    {
                        if (!sign.gameObject.activeSelf)
                        {
                            sign.DrawLabel(view.camera, 0.35f);
                        }
                    }
                }
            };

        // Its text in its colors, so level editors see it without entering play mode.
        private void DrawLabel(Camera? camera, float alpha)
        {
            var size = screenSized ? fontSize : WorldFontSize(camera);
            if (size < 2)
            {
                return; // too small to read from here
            }

            var style = new GUIStyle(UnityEditor.EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(6, 6, 2, 2),
                fontSize = Mathf.RoundToInt(size),
            };
            style.normal.textColor = new Color(textColor.r, textColor.g, textColor.b, alpha);
            style.normal.background = background ? Texture2D.whiteTexture : null;
            var saved = GUI.backgroundColor;
            GUI.backgroundColor = new Color(backgroundColor.r, backgroundColor.g, backgroundColor.b, backgroundOpacity * alpha);
            UnityEditor.Handles.Label(transform.position, text.Length > 0 ? text : "(empty sign)", style);
            GUI.backgroundColor = saved;
        }
#endif

#if UNITY_EDITOR
        // A world-sized sign's font size in the Scene view's points, as it looks from the scene camera.
        private float WorldFontSize(Camera? camera)
        {
            if (camera == null)
            {
                return textHeight * 40;
            }

            var depth = camera.WorldToViewportPoint(transform.position).z;
            if (depth <= 0)
            {
                return 0;
            }

            var pointsHigh = camera.pixelHeight / UnityEditor.EditorGUIUtility.pixelsPerPoint;
            var perMeter = camera.orthographic
                ? pointsHigh / (2 * camera.orthographicSize)
                : pointsHigh / (2 * depth * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad / 2));
            return Mathf.Min(textHeight * perMeter, 200);
        }
#endif
    }
}
