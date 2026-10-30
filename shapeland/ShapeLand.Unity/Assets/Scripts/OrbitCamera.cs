using Comet.Simulation;
using UnityEngine;

namespace ShapeLand.Client
{
    /// <summary>
    /// A third-person camera orbiting the player: the mouse turns it while the right button is held, the right
    /// stick turns it always. It stays above the ground but keeps its distance through blocks and props, which are
    /// culled from inside; the player's own shape shows through what hides it (<see cref="ShapeBody.ShowThroughWalls"/>).
    /// Its numbers are placeholders, adjusted by playing; the values that count are the ones on the camera in the
    /// Game scene.
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

        [Tooltip("Before joining: distance, pitch and turning speed (degrees per second) of the slow orbit around the island.")]
        [SerializeField] private Vector3 showcase = new Vector3(30, 25, 6);

        [Tooltip("Before joining: about how long the orbit takes to glide to another shape's spawn in the same zone, in seconds.")]
        [SerializeField] private float showcaseGlideSeconds = 0.4f;

        private float _pitch = 20;
        private ShapeLandControls _controls;
        private Vector2 _touchTurn;
        private CollisionWorld _world;
        private Vector3? _showcaseCenter;
        private Vector3 _shownCenter;
        private Vector3 _glideVelocity;
        private Vector3 _followGlideFrom;
        private float _followGlideStart;
        private float _followGlideSeconds;

        /// <summary>The camera's heading in degrees, clockwise from +Z seen from above: movement is relative to it.</summary>
        public float Yaw { get; private set; }

        /// <summary>What the camera follows, or null while it orbits the island.</summary>
        public Transform Target { get; private set; }

        /// <summary>Turns the camera by <paramref name="degrees"/> (yaw, pitch) on its next update: a touch drag (<see cref="TouchControls"/>).</summary>
        public void Turn(Vector2 degrees) => _touchTurn += degrees;

        /// <summary>
        /// Until <see cref="Follow"/>, orbits slowly around <paramref name="center"/> (behind the join screen), gliding
        /// there from the last center if <paramref name="glide"/>.
        /// </summary>
        public void Showcase(Vector3 center, bool glide = false)
        {
            if (!glide || _showcaseCenter == null)
            {
                _shownCenter = center;
                _glideVelocity = Vector3.zero;
            }

            _showcaseCenter = center;
        }

        /// <summary>
        /// Glides what the camera looks at from <paramref name="from"/> to the target over <paramref name="seconds"/>,
        /// easing in and out, rather than cutting there (after a near respawn).
        /// </summary>
        public void Glide(Vector3 from, float seconds)
        {
            _followGlideFrom = from;
            _followGlideStart = Time.unscaledTime;
            _followGlideSeconds = seconds;
        }

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
                    _shownCenter = Vector3.SmoothDamp(_shownCenter, center, ref _glideVelocity, showcaseGlideSeconds, Mathf.Infinity, Time.unscaledDeltaTime);
                    var middle = _shownCenter + Vector3.up * 2;
                    transform.SetPositionAndRotation(middle - orbit * Vector3.forward * showcase.x, orbit);
                }

                return;
            }

            var stick = _controls.Game.StickLook.ReadValue<Vector2>() * stickDegreesPerSecond * Time.unscaledDeltaTime;
            var turn = stick * GameSettings.CameraSpeed(CameraInput.Stick) + _touchTurn;
            _touchTurn = Vector2.zero;
            if (_controls.Game.OrbitHeld.IsPressed())
            {
                turn += _controls.Game.MouseLook.ReadValue<Vector2>() * mouseDegreesPerPixel * GameSettings.CameraSpeed(CameraInput.Mouse);
            }

            Yaw = Mathf.Repeat(Yaw + turn.x, 360);
            _pitch = Mathf.Clamp(_pitch - turn.y, pitchLimits.x, pitchLimits.y);

            var rotation = Quaternion.Euler(_pitch, Yaw, 0);
            var followed = Target.position;
            var glided = _followGlideSeconds > 0 ? (Time.unscaledTime - _followGlideStart) / _followGlideSeconds : 1;
            if (glided < 1)
            {
                followed = Vector3.Lerp(_followGlideFrom, followed, Mathf.SmoothStep(0, 1, glided));
            }

            var look = followed + Vector3.up * lookHeight;
            var position = look + rotation * Vector3.back * distance;
            if (_world != null && _world.TryGetTerrainHeight(position.x, position.z, position.y + 1000, out var ground) && position.y < ground + groundClearance)
            {
                position.y = ground + groundClearance;
                rotation = Quaternion.LookRotation(look - position);
            }

            transform.SetPositionAndRotation(position, rotation);
        }
    }
}
