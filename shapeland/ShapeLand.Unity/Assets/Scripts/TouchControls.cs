using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    /// <summary>
    /// Touch controls for phones, in the HUD (Hud.uxml's <c>touch</c> layer): a stick that appears wherever a thumb
    /// lands on the left half of the screen, a camera drag on the right half, and a jump button. The stick and Jump
    /// drive a virtual gamepad of their own, so the game's actions read them as they read a real one. They show while
    /// the player can move and the screen has been touched (from the start on a phone page), and hide when a keyboard or
    /// real gamepad is used away from a phone; which of these was used last is <see cref="Input"/>. Each control
    /// follows one touch at a time; Jump keeps inside the screen's safe area (<c>touch-safe</c>, set by the HUD). The HUD's
    /// buttons are the HUD's own (<see cref="Hud"/>), shown larger while touch controls are in use.
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
        private readonly VisualElement _hud;
        private readonly OrbitCamera _camera;
        // The virtual gamepad the stick and Jump drive, added once touch controls can appear (so a desktop never has one).
        private Gamepad _pad;
        private GamepadState _state;
        private int? _stickPointer;
        private int? _lookPointer;
        private int? _jumpPointer;
        private Vector2 _origin;
        private bool _touched;

        /// <summary>The kind of input used last; the HUD carries it as <c>touch-input</c> or <c>pad-input</c> (neither for keys and mouse).</summary>
        public InputKind Input { get; private set; }

        /// <summary>The controls in <paramref name="hud"/>, turning <paramref name="camera"/>.</summary>
        public TouchControls(VisualElement hud, OrbitCamera camera)
        {
            _hud = hud;
            _camera = camera;
            SetInput(WebPage.IsPhone ? InputKind.Touch : InputKind.KeyboardMouse);
            _layer = hud.Q("touch");
            _stickArea = hud.Q("touch-stick");
            _base = hud.Q("stick-base");
            _knob = hud.Q("stick-knob");
            _look = hud.Q("touch-look");
            _jump = hud.Q("jump-button");

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

        /// <summary>
        /// Shows or hides the controls for this frame; <paramref name="allowed"/> is whether the player can move now,
        /// <paramref name="inMenu"/> whether the Esc menu is open.
        /// </summary>
        public void Update(bool allowed, bool inMenu)
        {
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                SetInput(InputKind.Touch);
            }
            // Touch gives way only where it can't be an on-screen keyboard's keys: not while typing or on the look card.
            else if (!WebPage.IsPhone && (!_touched || allowed || inMenu))
            {
                // The mouse alone changes nothing: it goes with keys or a gamepad.
                if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
                {
                    SetInput(InputKind.KeyboardMouse);
                }
                else if (RealGamepadUsed())
                {
                    SetInput(InputKind.Gamepad);
                }
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

        public void Dispose()
        {
            if (_pad != null)
            {
                InputSystem.RemoveDevice(_pad);
                _pad = null;
            }
        }

        // Touch controls in use or not: the HUD's .touch-input class swaps in their stand-ins (Send, the footer without
        // F3) and moves things out of their way; .pad-input swaps in the gamepad's prompts.
        private void SetInput(InputKind input)
        {
            Input = input;
            _touched = input == InputKind.Touch;
            _hud.EnableInClassList("touch-input", _touched);
            _hud.EnableInClassList("pad-input", input == InputKind.Gamepad);
            if (_touched && _pad == null)
            {
                _pad = InputSystem.AddDevice<Gamepad>("Touch controls");
            }
        }

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
                _camera.Turn(DragTurn(new Vector2(delta.x, -delta.y), _layer.resolvedStyle.height,
                    DegreesPerScreenHeight * GameSettings.CameraSpeed(InputKind.Touch)));
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

        private void Send()
        {
            if (_pad != null)
            {
                InputSystem.QueueStateEvent(_pad, _state);
            }
        }

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
                if (gamepad != _pad && (gamepad.buttonSouth.wasPressedThisFrame || gamepad.buttonEast.wasPressedThisFrame
                    || gamepad.startButton.wasPressedThisFrame || gamepad.selectButton.wasPressedThisFrame
                    || gamepad.leftShoulder.wasPressedThisFrame || gamepad.rightShoulder.wasPressedThisFrame
                    || gamepad.dpad.ReadValue().sqrMagnitude > 0.25f
                    || gamepad.leftStick.ReadValue().sqrMagnitude > 0.25f || gamepad.rightStick.ReadValue().sqrMagnitude > 0.25f))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
