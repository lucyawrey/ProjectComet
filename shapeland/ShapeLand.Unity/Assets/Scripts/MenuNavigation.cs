using UnityEngine;
using UnityEngine.InputSystem;

namespace ShapeLand.Client
{
    public sealed class MenuNavigation
    {
        private const float RepeatDelay = 0.4f,
            RepeatInterval = 0.1f;

        private Vector2 _held;
        private float _repeatAt;

        public Vector2Int Step(Vector2 value)
        {
            var held = new Vector2(Mathf.Abs(value.x) > 0.5f ? Mathf.Sign(value.x) : 0, Mathf.Abs(value.y) > 0.5f ? -Mathf.Sign(value.y) : 0);
            if (held.y != 0)
            {
                held.x = 0;
            }

            var now = Time.unscaledTime;
            if (held != _held)
            {
                _held = held;
                _repeatAt = now + RepeatDelay;
                return Vector2Int.RoundToInt(held);
            }

            if (held != Vector2.zero && now >= _repeatAt)
            {
                _repeatAt = now + RepeatInterval;
                return Vector2Int.RoundToInt(held);
            }

            return Vector2Int.zero;
        }

        public Vector2Int StepVertical(Vector2 value) => Step(new Vector2(0, value.y));

        public static Vector2 TypingArrows()
        {
            var keyboard = Keyboard.current;
            return keyboard == null
                ? Vector2.zero
                : new Vector2(
                    0,
                    keyboard.upArrowKey.isPressed ? 1
                        : keyboard.downArrowKey.isPressed ? -1
                        : 0
                );
        }
    }
}
