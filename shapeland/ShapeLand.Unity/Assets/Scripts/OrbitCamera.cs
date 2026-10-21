using Comet.Simulation;
using UnityEngine;

namespace ShapeLand.Client
{
    /// <summary>
    /// A third-person camera orbiting the player: the mouse turns it while the right button is held, the right
    /// stick turns it always. It stays above the ground, and moves in front of a block that would come between it
    /// and the player, easing back out once the view is clear. Its numbers are placeholders, adjusted by playing;
    /// the values that count are the ones on the camera in the Game scene.
    /// </summary>
    public sealed class OrbitCamera : MonoBehaviour
    {
        [Tooltip("The point looked at, above the target's feet, in meters.")]
        [SerializeField] private float lookHeight = 0.8f;

        [Tooltip("Distance from the look point, in meters.")]
        [SerializeField] private float distance = 6f;

        [Tooltip("Degrees turned per pixel of mouse movement.")]
        [SerializeField] private float mouseDegreesPerPixel = 0.2f;

        [Tooltip("Degrees per second at full stick.")]
        [SerializeField] private Vector2 stickDegreesPerSecond = new Vector2(180, 120);

        [Tooltip("Lowest and highest pitch, in degrees (positive looks down).")]
        [SerializeField] private Vector2 pitchLimits = new Vector2(-10, 70);

        [Tooltip("The least height kept above the ground, in meters.")]
        [SerializeField] private float groundClearance = 0.3f;

        [Tooltip("The least distance kept from blocks, in meters: more than the near clip plane reaches, so their sides aren't cut open.")]
        [SerializeField] private float blockClearance = 0.2f;

        [Tooltip("How fast the camera moves back out after a block stops being in the way, in meters per second.")]
        [SerializeField] private float zoomOutSpeed = 6f;

        [Tooltip("Before joining: distance, pitch and turning speed (degrees per second) of the slow orbit around the island.")]
        [SerializeField] private Vector3 showcase = new Vector3(30, 25, 6);

        private float _pitch = 20;
        private float _distance;
        private ShapeLandControls _controls;
        private Vector2 _touchTurn;
        private CollisionWorld _world;
        private Vector3? _showcaseCenter;

        /// <summary>The camera's heading in degrees, clockwise from +Z seen from above: movement is relative to it.</summary>
        public float Yaw { get; private set; }

        /// <summary>What the camera follows, or null while it orbits the island.</summary>
        public Transform Target { get; private set; }

        /// <summary>Turns the camera by <paramref name="degrees"/> (yaw, pitch) on its next update: a touch drag (<see cref="TouchControls"/>).</summary>
        public void Turn(Vector2 degrees) => _touchTurn += degrees;

        /// <summary>Until <see cref="Follow"/>, orbits slowly around <paramref name="center"/> (behind the join screen).</summary>
        public void Showcase(Vector3 center) => _showcaseCenter = center;

        /// <summary>The world the camera keeps out of, when the followed player goes to another zone.</summary>
        public CollisionWorld World
        {
            set => _world = value;
        }

        /// <summary>Stops following and goes back to the showcase orbit (after the connection is lost).</summary>
        public void Release() => Target = null;

        /// <summary>Starts following <paramref name="target"/>, from behind it.</summary>
        public void Follow(Transform target, ShapeLandControls controls, CollisionWorld world)
        {
            Target = target;
            _controls = controls;
            _world = world;
            Yaw = target.eulerAngles.y;
            _distance = distance;
        }

        private void LateUpdate()
        {
            if (Target == null)
            {
                _touchTurn = Vector2.zero; // a drag without a target turns nothing, rather than all at once later
                if (_showcaseCenter is Vector3 center)
                {
                    Yaw = Mathf.Repeat(Yaw + showcase.z * Time.unscaledDeltaTime, 360);
                    var orbit = Quaternion.Euler(showcase.y, Yaw, 0);
                    var middle = center + Vector3.up * 2;
                    transform.SetPositionAndRotation(middle - orbit * Vector3.forward * showcase.x, orbit);
                }

                return;
            }

            var turn = _controls.Game.StickLook.ReadValue<Vector2>() * stickDegreesPerSecond * Time.unscaledDeltaTime + _touchTurn;
            _touchTurn = Vector2.zero;
            if (_controls.Game.OrbitHeld.IsPressed())
            {
                turn += _controls.Game.MouseLook.ReadValue<Vector2>() * mouseDegreesPerPixel;
            }

            Yaw = Mathf.Repeat(Yaw + turn.x, 360);
            _pitch = Mathf.Clamp(_pitch - turn.y, pitchLimits.x, pitchLimits.y);

            var rotation = Quaternion.Euler(_pitch, Yaw, 0);
            var look = Target.position + Vector3.up * lookHeight;
            // Pull in at once when a block is in the way, so it never shows from inside; ease back out.
            var back = rotation * Vector3.back;
            var wanted = distance;
            if (_world != null && _world.Raycast(Vectors.ToNumerics(look), Vectors.ToNumerics(back), distance, blockClearance, out var hit))
            {
                wanted = hit;
            }

            _distance = wanted < _distance ? wanted : Mathf.MoveTowards(_distance, wanted, zoomOutSpeed * Time.unscaledDeltaTime);
            var position = look + back * _distance;
            if (_world != null && _world.TryGetGround(Vectors.ToNumerics(position), 0, position.y + 1000, out var ground) && position.y < ground + groundClearance)
            {
                position.y = ground + groundClearance;
                rotation = Quaternion.LookRotation(look - position);
            }

            transform.SetPositionAndRotation(position, rotation);
        }
    }
}
