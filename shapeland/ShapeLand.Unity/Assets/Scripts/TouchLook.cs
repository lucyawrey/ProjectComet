using UnityEngine;
using UnityEngine.EventSystems;

namespace ShapeLand.Client
{
    /// <summary>
    /// Turns the camera as a touch drags across its region (the right half of the screen), like dragging with the
    /// right mouse button held. One touch at a time.
    /// </summary>
    public sealed class TouchLook : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        /// <summary>How far the camera turns for a drag the screen's full height, in degrees.</summary>
        public float DegreesPerScreenHeight = 180;

        public OrbitCamera Camera;

        private int? _pointer;

        /// <summary>The camera's turn in degrees (yaw, pitch) for a drag of <paramref name="pixels"/> on a screen <paramref name="screenHeight"/> pixels tall.</summary>
        public static Vector2 Turn(Vector2 pixels, float screenHeight, float degreesPerScreenHeight) =>
            screenHeight > 0 ? pixels / screenHeight * degreesPerScreenHeight : Vector2.zero;

        public void OnPointerDown(PointerEventData eventData) => _pointer ??= eventData.pointerId;

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId == _pointer && Camera != null)
            {
                Camera.Turn(Turn(eventData.delta, Screen.height, DegreesPerScreenHeight));
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId == _pointer)
            {
                _pointer = null;
            }
        }

        private void OnDisable() => _pointer = null;
    }
}
