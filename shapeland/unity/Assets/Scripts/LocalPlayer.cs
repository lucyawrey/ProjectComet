using Comet.Client;
using Comet.Protocol.Messages;
using Comet.Simulation;
using Comet.Unity;
using ShapeLand.Shared.Content;
using ShapeLand.Shared.World;
using UnityEngine;

namespace ShapeLand.Client
{
    /// <summary>
    /// The player's own shape: moves it with the shared <see cref="PlayerMotor"/> on a fixed step, reports its
    /// position, and draws it between the last two steps. A correction from the server moves it at once (the
    /// blend comes later).
    /// </summary>
    public sealed class LocalPlayer : MonoBehaviour
    {
        /// <summary>
        /// Motor steps per second. Faster than the server's tick so input waits less (about 8 ms on average
        /// for a step, and as long again drawing between steps); reports are paced separately.
        /// </summary>
        public const int StepsPerSecond = 60;

        private readonly FixedStep _step = new FixedStep(StepsPerSecond);
        private readonly PositionReporter _reporter = new PositionReporter();
        private ClientSession _session;
        private CollisionWorld _world;
        private Shape _shape;
        private OrbitCamera _camera;
        private ShapeLandControls _controls;
        private MotorState _motor;
        private MotorState _previous;
        private bool _jumpQueued;

        /// <summary>The motor's state after the latest step.</summary>
        public MotorState Motor => _motor;

        public int SnapBacks { get; private set; }

        public int Respawns { get; private set; }

        public void Begin(ClientSession session, CollisionWorld world, Shape shape, Vector3 position, float facing, OrbitCamera orbitCamera, ShapeLandControls controls)
        {
            _session = session;
            _world = world;
            _shape = shape;
            _camera = orbitCamera;
            _controls = controls;
            _motor = new MotorState { Position = WorldMeshes.ToNumerics(position), Grounded = true, Facing = facing };
            _previous = _motor;
            _session.Corrected += OnCorrected;
            Draw(1);
        }

        /// <summary>
        /// Turns stick or key input (x right, y forward) into a direction on the ground (X, Z) relative to a
        /// camera heading <paramref name="yawDegrees"/> clockwise from +Z.
        /// </summary>
        public static System.Numerics.Vector2 CameraRelative(Vector2 input, float yawDegrees)
        {
            var yaw = yawDegrees * Mathf.Deg2Rad;
            var forward = new System.Numerics.Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
            var right = new System.Numerics.Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw));
            return right * input.x + forward * input.y;
        }

        private void Update()
        {
            if (_session == null)
            {
                return;
            }

            var move = CameraRelative(_controls.Move.ReadValue<Vector2>(), _camera.Yaw);
            _jumpQueued |= _controls.Jump.WasPressedThisFrame();

            var now = CometConnection.Now;
            var steps = _step.Advance(now);
            for (var i = 0; i < steps; i++)
            {
                _previous = _motor;
                PlayerMotor.Step(ref _motor, move, _jumpQueued, (float)_step.StepSeconds, _shape.MaxSpeed, _shape.JumpVelocity, _world, ShapeLandWorld.Rules);
                _jumpQueued = false;
                if (_reporter.ShouldReport(_motor.Velocity, now))
                {
                    _session.ReportPosition(_motor.Position, _motor.Velocity, _motor.Facing);
                }
            }

            Draw(_step.Alpha);
        }

        private void Draw(float alpha)
        {
            var position = System.Numerics.Vector3.Lerp(_previous.Position, _motor.Position, alpha);
            var facing = Mathf.LerpAngle(_previous.Facing * Mathf.Rad2Deg, _motor.Facing * Mathf.Rad2Deg, alpha);
            transform.SetPositionAndRotation(WorldMeshes.ToUnity(position), Quaternion.Euler(0, facing, 0));
        }

        private void OnCorrected(PositionCorrection correction)
        {
            _motor.Position = new System.Numerics.Vector3(correction.X, correction.Y, correction.Z);
            _motor.Velocity = System.Numerics.Vector3.Zero;
            _motor.Grounded = false;
            _previous = _motor;
            _reporter.Reset();
            if (correction.Reason == CorrectionReason.Respawn)
            {
                Respawns++;
            }
            else
            {
                SnapBacks++;
            }
        }

        private void OnDestroy()
        {
            if (_session != null)
            {
                _session.Corrected -= OnCorrected;
            }
        }
    }
}
