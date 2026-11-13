using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

namespace ShapeLand.Client
{
    public sealed class TouchControls : IDisposable
    {
        public const float StickRange = 60;

        public const float DegreesPerScreenHeight = 180;

        private readonly VisualElement _layer;
        private readonly VisualElement _stickArea;
        private readonly VisualElement _base;
        private readonly VisualElement _knob;
        private readonly VisualElement _look;
        private readonly VisualElement _jump;
        private readonly VisualElement _hud;
        private readonly OrbitCamera _camera;

        private Gamepad _pad;
        private GamepadState _state;
        private int? _stickPointer;
        private int? _lookPointer;
        private int? _jumpPointer;
        private Vector2 _origin;
        private bool _touched;

        public InputKind Input { get; private set; }

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

        public static Vector2 StickValue(Vector2 offset, float range, float deadZone = 0.1f)
        {
            var value = Vector2.ClampMagnitude(offset / range, 1);
            var size = value.magnitude;
            return size <= deadZone ? Vector2.zero : value * ((size - deadZone) / (1 - deadZone) / size);
        }

        public static Vector2 DragTurn(Vector2 distance, float height, float degreesPerScreenHeight = DegreesPerScreenHeight) =>
            height > 0 ? distance / height * degreesPerScreenHeight : Vector2.zero;

        public void Update(bool allowed, bool inMenu)
        {
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                SetInput(InputKind.Touch);
            }
            else if (!WebPage.IsPhone && (!_touched || allowed || inMenu))
            {
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
                var delta = (Vector2)e.deltaPosition;
                _camera.Turn(
                    DragTurn(
                        new Vector2(delta.x, -delta.y),
                        _layer.resolvedStyle.height,
                        DegreesPerScreenHeight * GameSettings.CameraSpeed(InputKind.Touch)
                    )
                );
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

        private bool RealGamepadUsed()
        {
            foreach (var gamepad in Gamepad.all)
            {
                if (
                    gamepad != _pad
                    && (
                        gamepad.buttonSouth.wasPressedThisFrame
                        || gamepad.buttonEast.wasPressedThisFrame
                        || gamepad.startButton.wasPressedThisFrame
                        || gamepad.selectButton.wasPressedThisFrame
                        || gamepad.leftShoulder.wasPressedThisFrame
                        || gamepad.rightShoulder.wasPressedThisFrame
                        || gamepad.dpad.ReadValue().sqrMagnitude > 0.25f
                        || gamepad.leftStick.ReadValue().sqrMagnitude > 0.25f
                        || gamepad.rightStick.ReadValue().sqrMagnitude > 0.25f
                    )
                )
                {
                    return true;
                }
            }

            return false;
        }
    }
}
