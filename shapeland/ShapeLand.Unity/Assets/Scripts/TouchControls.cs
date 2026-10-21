using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    /// <summary>
    /// Touch controls for phones, in the HUD (Hud.uxml's <c>touch</c> layer): a stick that appears wherever a thumb
    /// lands on the left half of the screen, a camera drag on the right half, a jump button, and Change Look, Return to
    /// Spawn, chat and network stats buttons. The stick and Jump drive a virtual gamepad of their own, so the game's actions read
    /// them as they read a real one. They show while the player can move and the screen has been touched (from the
    /// start on a phone page), and hide when a keyboard or a real gamepad is used away from a phone. Each control
    /// follows one touch at a time; the buttons and Jump keep inside the screen's safe area (<c>touch-safe</c>, set by the HUD).
    /// </summary>
    public sealed class TouchControls : IDisposable
    {
        /// <summary>How far the thumb drags for full speed, in panel pixels.</summary>
        public const float StickRange = 60;

        /// <summary>How far the camera turns for a drag the screen's full height, in degrees.</summary>
        public const float DegreesPerScreenHeight = 180;

        private readonly VisualElement _layer;
        private readonly VisualElement _stickArea;
        private readonly VisualElement _base;
        private readonly VisualElement _knob;
        private readonly VisualElement _look;
        private readonly VisualElement _jump;
        private readonly OrbitCamera _camera;
        private readonly Gamepad _pad;
        private GamepadState _state;
        private int? _stickPointer;
        private int? _lookPointer;
        private int? _jumpPointer;
        private Vector2 _origin;
        private bool _touched;

        /// <summary>
        /// The controls in <paramref name="hud"/>, turning <paramref name="camera"/>; the buttons call
        /// <paramref name="look"/>, <paramref name="returnToSpawn"/>, <paramref name="chat"/> and <paramref name="stats"/>.
        /// </summary>
        public TouchControls(VisualElement hud, OrbitCamera camera, Action look, Action returnToSpawn, Action chat, Action stats)
        {
            _camera = camera;
            _touched = WebPage.IsPhone;
            hud.EnableInClassList("phone", WebPage.IsPhone);
            _pad = InputSystem.AddDevice<Gamepad>("Touch controls");
            _layer = hud.Q("touch");
            _stickArea = hud.Q("touch-stick");
            _base = hud.Q("stick-base");
            _knob = hud.Q("stick-knob");
            _look = hud.Q("touch-look");
            _jump = hud.Q("jump-button");
            hud.Q<Button>("touch-look-button").clicked += look;
            hud.Q<Button>("touch-return-button").clicked += returnToSpawn;
            hud.Q<Button>("touch-chat-button").clicked += chat;
            hud.Q<Button>("touch-stats-button").clicked += stats;

            _stickArea.RegisterCallback<PointerDownEvent>(OnStickDown);
            _stickArea.RegisterCallback<PointerMoveEvent>(OnStickMove);
            _stickArea.RegisterCallback<PointerUpEvent>(e => EndStick(e.pointerId));
            _stickArea.RegisterCallback<PointerCancelEvent>(e => EndStick(e.pointerId));
            _look.RegisterCallback<PointerDownEvent>(e => Begin(_look, ref _lookPointer, e.pointerId));
            _look.RegisterCallback<PointerMoveEvent>(OnLookMove);
            _look.RegisterCallback<PointerUpEvent>(e => End(_look, ref _lookPointer, e.pointerId));
            _look.RegisterCallback<PointerCancelEvent>(e => End(_look, ref _lookPointer, e.pointerId));
            _jump.RegisterCallback<PointerDownEvent>(e =>
            {
                if (Begin(_jump, ref _jumpPointer, e.pointerId))
                {
                    SetJump(true);
                }
            });
            _jump.RegisterCallback<PointerUpEvent>(e => EndJump(e.pointerId));
            _jump.RegisterCallback<PointerCancelEvent>(e => EndJump(e.pointerId));
        }

        /// <summary>
        /// The stick's value for a thumb <paramref name="offset"/> from where it landed (x right, y up): full at
        /// <paramref name="range"/> and beyond, and nothing inside a small dead zone, from which it rises evenly.
        /// </summary>
        public static Vector2 StickValue(Vector2 offset, float range, float deadZone = 0.1f)
        {
            var value = Vector2.ClampMagnitude(offset / range, 1);
            var size = value.magnitude;
            return size <= deadZone ? Vector2.zero : value * ((size - deadZone) / (1 - deadZone) / size);
        }

        /// <summary>The camera's turn in degrees (yaw, pitch) for a drag of <paramref name="distance"/> across a screen <paramref name="height"/> tall.</summary>
        public static Vector2 DragTurn(Vector2 distance, float height, float degreesPerScreenHeight = DegreesPerScreenHeight) =>
            height > 0 ? distance / height * degreesPerScreenHeight : Vector2.zero;

        /// <summary>Shows or hides the controls for this frame; <paramref name="allowed"/> is whether the player can move now.</summary>
        public void Update(bool allowed)
        {
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                _touched = true;
            }
            else if (!WebPage.IsPhone && (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame || RealGamepadUsed()))
            {
                _touched = false;
            }

            var show = _touched && allowed;
            if (show == _layer.ClassListContains("hidden"))
            {
                _layer.EnableInClassList("hidden", !show);
                if (!show)
                {
                    Release();
                }
            }
        }

        public void Dispose() => InputSystem.RemoveDevice(_pad);

        private void OnStickDown(PointerDownEvent e)
        {
            if (!Begin(_stickArea, ref _stickPointer, e.pointerId))
            {
                return;
            }

            _origin = _stickArea.WorldToLocal(e.position);
            var size = _base.resolvedStyle.width > 0 ? _base.resolvedStyle.width : 144;
            _base.style.left = _origin.x - size / 2;
            _base.style.top = _origin.y - size / 2;
            _knob.style.translate = new Translate(0, 0);
            _base.RemoveFromClassList("hidden");
        }

        private void OnStickMove(PointerMoveEvent e)
        {
            if (e.pointerId != _stickPointer)
            {
                return;
            }

            var offset = (Vector2)_stickArea.WorldToLocal(e.position) - _origin;
            var shown = Vector2.ClampMagnitude(offset, StickRange);
            _knob.style.translate = new Translate(shown.x, shown.y);
            // The panel's y runs down; the stick's runs up.
            _state.leftStick = StickValue(new Vector2(offset.x, -offset.y), StickRange);
            Send();
        }

        private void EndStick(int pointer)
        {
            if (End(_stickArea, ref _stickPointer, pointer))
            {
                _base.AddToClassList("hidden");
                _state.leftStick = Vector2.zero;
                Send();
            }
        }

        private void OnLookMove(PointerMoveEvent e)
        {
            if (e.pointerId == _lookPointer && _camera != null)
            {
                // Like the right mouse button's drag: up turns the camera as a mouse moved up does.
                var delta = (Vector2)e.deltaPosition;
                _camera.Turn(DragTurn(new Vector2(delta.x, -delta.y), _layer.resolvedStyle.height));
            }
        }

        private void EndJump(int pointer)
        {
            if (End(_jump, ref _jumpPointer, pointer))
            {
                SetJump(false);
            }
        }

        private void SetJump(bool down)
        {
            _jump.EnableInClassList("pressed", down);
            _state = _state.WithButton(GamepadButton.South, down);
            Send();
        }

        // Lets go of every control: on hiding, so nothing stays held.
        private void Release()
        {
            EndStick(_stickPointer ?? -1);
            End(_look, ref _lookPointer, _lookPointer ?? -1);
            EndJump(_jumpPointer ?? -1);
        }

        private void Send() => InputSystem.QueueStateEvent(_pad, _state);

        // Takes the touch for a control that has none (capturing it, so it keeps the drag past the control's edge).
        private static bool Begin(VisualElement control, ref int? held, int pointer)
        {
            if (held != null)
            {
                return false;
            }

            held = pointer;
            control.CapturePointer(pointer);
            return true;
        }

        private static bool End(VisualElement control, ref int? held, int pointer)
        {
            if (held != pointer)
            {
                return false;
            }

            if (control.HasPointerCapture(pointer))
            {
                control.ReleasePointer(pointer);
            }

            held = null;
            return true;
        }

        // A real gamepad's button or stick, not the virtual one the touch controls drive.
        private bool RealGamepadUsed()
        {
            foreach (var gamepad in Gamepad.all)
            {
                if (gamepad != _pad && (gamepad.buttonSouth.wasPressedThisFrame || gamepad.startButton.wasPressedThisFrame
                    || gamepad.leftStick.ReadValue().sqrMagnitude > 0.25f || gamepad.rightStick.ReadValue().sqrMagnitude > 0.25f))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
