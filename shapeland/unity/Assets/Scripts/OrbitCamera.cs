using Comet.Simulation;
using UnityEngine;

namespace ShapeLand.Client
{
    /// <summary>
    /// A third-person camera orbiting the player: the mouse turns it while the right button is held, the right
    /// stick turns it always. It stays above the ground, but doesn't yet avoid blocks. Its numbers are placeholders,
    /// adjusted by playing.
    /// </summary>
    public sealed class OrbitCamera : MonoBehaviour
    {
        [Tooltip("The point looked at, above the target's feet, in metres.")]
        [SerializeField] private float lookHeight = 0.8f;

        [Tooltip("Distance from the look point, in metres.")]
        [SerializeField] private float distance = 6f;

        [Tooltip("Degrees turned per pixel of mouse movement.")]
        [SerializeField] private float mouseDegreesPerPixel = 0.2f;

        [Tooltip("Degrees per second at full stick.")]
        [SerializeField] private Vector2 stickDegreesPerSecond = new Vector2(180, 120);

        [Tooltip("Lowest and highest pitch, in degrees (positive looks down).")]
        [SerializeField] private Vector2 pitchLimits = new Vector2(-10, 70);

        [Tooltip("The least height kept above the ground, in metres.")]
        [SerializeField] private float groundClearance = 0.3f;

        private float _pitch = 20;
        private ShapeLandControls _controls;
        private CollisionWorld _world;

        /// <summary>The camera's heading in degrees, clockwise from +Z seen from above: movement is relative to it.</summary>
        public float Yaw { get; private set; }

        public Transform Target { get; private set; }

        /// <summary>Starts following <paramref name="target"/>, from behind it.</summary>
        public void Follow(Transform target, ShapeLandControls controls, CollisionWorld world)
        {
            Target = target;
            _controls = controls;
            _world = world;
            Yaw = target.eulerAngles.y;
        }

        private void LateUpdate()
        {
            if (Target == null)
            {
                return;
            }

            var turn = _controls.StickLook.ReadValue<Vector2>() * stickDegreesPerSecond * Time.unscaledDeltaTime;
            if (_controls.OrbitHeld.IsPressed())
            {
                turn += _controls.MouseLook.ReadValue<Vector2>() * mouseDegreesPerPixel;
            }

            Yaw = Mathf.Repeat(Yaw + turn.x, 360);
            _pitch = Mathf.Clamp(_pitch - turn.y, pitchLimits.x, pitchLimits.y);

            var rotation = Quaternion.Euler(_pitch, Yaw, 0);
            var look = Target.position + Vector3.up * lookHeight;
            var position = look - rotation * Vector3.forward * distance;
            if (_world != null && _world.TryGetGround(WorldMeshes.ToNumerics(position), 0, position.y + 1000, out var ground) && position.y < ground + groundClearance)
            {
                position.y = ground + groundClearance;
                rotation = Quaternion.LookRotation(look - position);
            }

            transform.SetPositionAndRotation(position, rotation);
        }
    }
}
