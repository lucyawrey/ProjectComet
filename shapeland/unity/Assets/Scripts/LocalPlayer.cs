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
    /// position, and draws it between the last two steps. A snap-back from the server moves the simulation at once
    /// and blends the drawn shape after it; a respawn moves both, behind the screen fade, which starts while
    /// falling (<see cref="RespawnFade"/>).
    /// </summary>
    public sealed class LocalPlayer : MonoBehaviour
    {
        /// <summary>
        /// Motor steps per second. Faster than the server's tick so input waits less (about 8 ms on average
        /// for a step, and as long again drawing between steps); reports are paced separately.
        /// </summary>
        public const int StepsPerSecond = 60;

        /// <summary>How much faster than the shape allows the speed cheat moves (development builds and the editor only), like the cheating bots.</summary>
        public const float SpeedCheat = 2.5f;

        private readonly FixedStep _step = new FixedStep(StepsPerSecond);
        private readonly PositionReporter _reporter = new PositionReporter();
        private readonly CorrectionBlend _blend = new CorrectionBlend();
        private float _cheatedAt = float.NegativeInfinity;
        private ClientSession _session;
        private CollisionWorld _world;
        private Shape _shape;
        private OrbitCamera _camera;
        private ShapeLandControls _controls;
        private MotorState _motor;
        private MotorState _previous;
        private bool _jumpQueued;
        private System.Numerics.Vector3 _drawn;

        /// <summary>Whether the controls move the shape; off while typing in chat.</summary>
        public bool InputEnabled { get; set; } = true;

        /// <summary>The motor's state after the latest step.</summary>
        public MotorState Motor => _motor;

        public int SnapBacks { get; private set; }

        /// <summary>Of <see cref="SnapBacks"/>, those that came within a second of using the speed cheat.</summary>
        public int CheatSnapBacks { get; private set; }

        public int Respawns { get; private set; }

        /// <summary>How far the drawn shape still trails the simulation after a snap-back, in metres.</summary>
        public float BlendDistance => _blend.Offset.Length();

        /// <summary>How dark the screen should be for the respawn fade: 0 clear, 1 black.</summary>
        public float Darkness { get; private set; }

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

            var move = InputEnabled ? CameraRelative(_controls.Move.ReadValue<Vector2>(), _camera.Yaw) : System.Numerics.Vector2.Zero;
            _jumpQueued |= InputEnabled && _controls.Jump.WasPressedThisFrame();

            var now = CometConnection.Now;
            var maxSpeed = _shape.MaxSpeed;
            if (Debug.isDebugBuild && InputEnabled && _controls.SpeedCheat.IsPressed())
            {
                maxSpeed *= SpeedCheat;
                _cheatedAt = Time.unscaledTime;
            }

            // The steps run on the server's clock, so every other one falls on a server tick; only those are
            // reported, stamped with their tick, so others draw exactly the spacing the player moved at.
            var steps = _step.Advance(_session.ServerSeconds(now));
            for (var i = 0; i < steps; i++)
            {
                var stepNumber = _step.LastStep - (steps - 1 - i);
                _previous = _motor;
                PlayerMotor.Step(ref _motor, move, _jumpQueued, (float)_step.StepSeconds, maxSpeed, _shape.JumpVelocity, _world, ShapeLandWorld.Rules);
                _jumpQueued = false;
                if (_session.TickOfStep(stepNumber, StepsPerSecond, out var tick) && _reporter.ShouldReport(_motor.Velocity, now))
                {
                    _session.ReportPosition(tick, _motor.Position, _motor.Velocity, _motor.Facing);
                }
            }

            _blend.Advance(Time.unscaledDeltaTime);
            Draw(_step.Alpha);
            Darkness = RespawnFade.Step(Darkness, _drawn.Y, _world.KillHeight, Time.unscaledDeltaTime);
        }

        private void Draw(float alpha)
        {
            _drawn = System.Numerics.Vector3.Lerp(_previous.Position, _motor.Position, alpha) + _blend.Offset;
            var facing = Mathf.LerpAngle(_previous.Facing * Mathf.Rad2Deg, _motor.Facing * Mathf.Rad2Deg, alpha);
            transform.SetPositionAndRotation(WorldMeshes.ToUnity(_drawn), Quaternion.Euler(0, facing, 0));
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
                // Usually dark already from the fall; if not, cut to black rather than show the jump.
                _blend.Clear();
                Darkness = 1;
                Respawns++;
            }
            else
            {
                _blend.Begin(_drawn, _motor.Position);
                SnapBacks++;
                if (Time.unscaledTime - _cheatedAt < 1)
                {
                    CheatSnapBacks++;
                }
            }

            Draw(1);
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
