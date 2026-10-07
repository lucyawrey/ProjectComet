using System;
using UnityEngine.InputSystem;

namespace ShapeLand.Client
{
    /// <summary>
    /// ShapeLand's bindings, all in one place: WASD or the arrows and Space, the mouse turning the camera while
    /// the right button is held; the left stick, the right stick and the south button on a gamepad.
    /// </summary>
    public sealed class ShapeLandControls : IDisposable
    {
        public ShapeLandControls()
        {
            Move = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
            Move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            Move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow").With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            Move.AddBinding("<Gamepad>/leftStick");

            Jump = new InputAction("Jump", InputActionType.Button);
            Jump.AddBinding("<Keyboard>/space");
            Jump.AddBinding("<Gamepad>/buttonSouth");

            OrbitHeld = new InputAction("Orbit held", InputActionType.Button, "<Mouse>/rightButton");
            MouseLook = new InputAction("Mouse look", InputActionType.PassThrough, "<Mouse>/delta", expectedControlType: "Vector2");
            StickLook = new InputAction("Stick look", InputActionType.Value, "<Gamepad>/rightStick", expectedControlType: "Vector2");

            foreach (var action in All)
            {
                action.Enable();
            }
        }

        /// <summary>Movement on the ground, x right and y forward, relative to the camera.</summary>
        public InputAction Move { get; }

        public InputAction Jump { get; }

        /// <summary>While held, the mouse turns the camera.</summary>
        public InputAction OrbitHeld { get; }

        /// <summary>Mouse movement, in pixels this frame.</summary>
        public InputAction MouseLook { get; }

        /// <summary>Camera turning from a stick, -1 to 1 on each axis.</summary>
        public InputAction StickLook { get; }

        private InputAction[] All => new[] { Move, Jump, OrbitHeld, MouseLook, StickLook };

        public void Dispose()
        {
            foreach (var action in All)
            {
                action.Dispose();
            }
        }
    }
}
