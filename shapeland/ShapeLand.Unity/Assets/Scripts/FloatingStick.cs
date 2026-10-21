using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.OnScreen;

namespace ShapeLand.Client
{
    /// <summary>
    /// A stick that appears where a thumb lands anywhere in its region (the left half of the screen), and moves the
    /// player as the thumb drags from there. An on-screen control: it writes a virtual gamepad's left stick, so the
    /// same Move action reads it as a real gamepad's. One touch at a time; others in the region are ignored.
    /// </summary>
    public sealed class FloatingStick : OnScreenControl, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [InputControl(layout = "Vector2")]
        [SerializeField] private string stickPath = "<Gamepad>/leftStick";

        /// <summary>How far the thumb drags for full speed, in canvas units.</summary>
        public float Range = 60;

        /// <summary>The drawn base ring and knob, placed under the thumb; hidden while not touched.</summary>
        public RectTransform Base;
        public RectTransform Knob;

        private int? _pointer;
        private Vector2 _origin;

        protected override string controlPathInternal
        {
            get => stickPath;
            set => stickPath = value;
        }

        /// <summary>
        /// The stick's value for a thumb <paramref name="offset"/> from where it landed: full at <paramref name="range"/>
        /// and beyond, and nothing inside a small dead zone, from which it rises evenly.
        /// </summary>
        public static Vector2 Value(Vector2 offset, float range, float deadZone = 0.1f)
        {
            var value = Vector2.ClampMagnitude(offset / range, 1);
            var size = value.magnitude;
            return size <= deadZone ? Vector2.zero : value * ((size - deadZone) / (1 - deadZone) / size);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_pointer != null || !Local(eventData, out _origin))
            {
                return;
            }

            _pointer = eventData.pointerId;
            Base.anchoredPosition = Knob.anchoredPosition = _origin;
            Base.gameObject.SetActive(true);
            Knob.gameObject.SetActive(true);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != _pointer || !Local(eventData, out var at))
            {
                return;
            }

            var offset = Vector2.ClampMagnitude(at - _origin, Range);
            Knob.anchoredPosition = _origin + offset;
            SendValueToControl(Value(at - _origin, Range));
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId == _pointer)
            {
                Release();
            }
        }

        protected override void OnDisable()
        {
            Release();
            base.OnDisable();
        }

        private void Release()
        {
            _pointer = null;
            if (Base != null)
            {
                Base.gameObject.SetActive(false);
                Knob.gameObject.SetActive(false);
            }

            if (control != null)
            {
                SendValueToControl(Vector2.zero);
            }
        }

        // The event's position in this region's own coordinates, where the base and knob are placed.
        private bool Local(PointerEventData eventData, out Vector2 position) =>
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, eventData.position, eventData.pressEventCamera, out position);
    }
}
