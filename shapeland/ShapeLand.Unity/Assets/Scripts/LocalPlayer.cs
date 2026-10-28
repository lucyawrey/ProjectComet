using System.Collections.Generic;
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
    /// and blends the drawn shape after it; a teleport or a respawn moves the simulation at once, while the drawn
    /// shape fades out where it stood before it's drawn there (<see cref="ShapeFade"/>), the camera cutting or, for a
    /// near respawn, gliding after it. Placements that turn solid around the player are passed through until the
    /// player is out of them.
    /// </summary>
    public sealed class LocalPlayer : MonoBehaviour
    {
        [Tooltip("After a respawn, how long the camera glides to the spawn before the shape fades in, in seconds.")]
        [SerializeField] private float respawnGlideSeconds = 0.5f;

        [Tooltip("The farthest a respawn glides the camera, in meters across the ground; farther, or into another zone, it cuts.")]
        [SerializeField] private float respawnGlideDistance = 30f;

        /// <summary>
        /// Motor steps per second. Faster than the server's tick so input waits less (about 8 ms on average
        /// for a step, and as long again drawing between steps); reports are paced separately.
        /// </summary>
        public const int StepsPerSecond = 60;

        /// <summary>A multiple of the shape's top speed, for tests that need the server to snap the player back.</summary>
        public float TestSpeedScale { get; set; } = 1;

        private readonly FixedStep _step = new FixedStep(StepsPerSecond);
        private readonly PositionReporter _reporter = new PositionReporter();
        private readonly CorrectionBlend _blend = new CorrectionBlend();
        private ClientSession _session;
        private CollisionWorld _world;
        private MovementRules _rules;
        private Shape _shape;
        private OrbitCamera _camera;
        private ShapeLandControls _controls;
        private MotorState _motor;
        private MotorState _previous;
        private bool _jumpQueued;
        private System.Numerics.Vector3 _drawn;
        private readonly ShapeFade _fade = new ShapeFade();
        private Vector3 _held;
        private CorrectionReason _moveReason;
        private bool _zoneChanged;

        /// <summary>Whether the controls move the shape; off while typing in chat.</summary>
        public bool InputEnabled { get; set; } = true;

        /// <summary>The motor's state after the latest step.</summary>
        public MotorState Motor => _motor;

        /// <summary>The world the player moves in; set anew when the server puts them in another zone.</summary>
        public CollisionWorld World
        {
            get => _world;
            set
            {
                _zoneChanged |= value != _world;
                _world = value;
                PassThrough.Clear();
            }
        }

        /// <summary>The meshes the player passes through: turned on around them, until they're out (as the server keeps them).</summary>
        public PassThrough PassThrough { get; } = new PassThrough();

        /// <summary>How many times a teleporter sent the player somewhere.</summary>
        public int Teleports { get; private set; }

        /// <summary>How many times the server has moved the player back (a correction that wasn't a respawn).</summary>
        public int SnapBacks { get; private set; }

        /// <summary>How many times the player fell out of the world and was respawned.</summary>
        public int Respawns { get; private set; }

        /// <summary>How far the drawn shape still trails the simulation after a snap-back, in meters.</summary>
        public float BlendDistance => _blend.Offset.Length();

        /// <summary>How far the player's shape has faded (<see cref="ShapeFade"/>): 0 fully visible, 1 gone.</summary>
        public float Faded => _fade.Faded;

        /// <summary>Whether the shape is fading out for a move (before a teleport, or held where it stood after one).</summary>
        public bool Departing => _fade.Departing;

        /// <summary>
        /// The pad the player stands on fires in <paramref name="seconds"/>: the shape fades out to be gone by then,
        /// started as if it had begun on time.
        /// </summary>
        public void TeleportSoon(float seconds) => _fade.DepartFrom(Time.unscaledTime + seconds - ShapeFade.FadeOutSeconds);

        /// <summary>The player stepped off the pad before it fired: the shape fades back in.</summary>
        public void TeleportCancelled() => _fade.Cancel();

        /// <summary>Starts the player as <paramref name="shape"/> at <paramref name="position"/>, facing <paramref name="facing"/> radians, moved by <paramref name="controls"/> relative to the camera.</summary>
        public void Begin(ClientSession session, CollisionWorld world, MovementRules rules, Shape shape, Vector3 position, float facing, OrbitCamera orbitCamera, ShapeLandControls controls)
        {
            _rules = rules;
            _session = session;
            _world = world;
            _shape = shape;
            _camera = orbitCamera;
            _controls = controls;
            _motor = new MotorState { Position = Vectors.ToNumerics(position), Grounded = true, Facing = facing };
            _previous = _motor;
            _session.Corrected += OnCorrected;
            Draw(1);
        }

        /// <summary>Moves on as <paramref name="shape"/> from now on, with its speed and jump.</summary>
        public void ChangeShape(Shape shape) => _shape = shape;

        /// <summary>Placements tagged <paramref name="tag"/> flipped: any that turned solid around the player are passed through.</summary>
        public void Flipped(string tag) => PassThrough.Flipped(_world, tag, _motor.Position, _rules.BodyRadius, _rules.BodyHeight);

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

            var move = InputEnabled && !_fade.Holding ? CameraRelative(_controls.Game.Move.ReadValue<Vector2>(), _camera.Yaw) : System.Numerics.Vector2.Zero;
            _jumpQueued |= InputEnabled && !_fade.Holding && _controls.Game.Jump.WasPressedThisFrame();

            var now = CometConnection.Now;
            var maxSpeed = _shape.MaxSpeed * TestSpeedScale;

            // The steps run on the server's clock, so every other one falls on a server tick; only those are
            // reported, stamped with their tick, so others draw exactly the spacing the player moved at.
            var steps = _step.Advance(_session.ServerSeconds(now));
            for (var i = 0; i < steps; i++)
            {
                var stepNumber = _step.LastStep - (steps - 1 - i);
                _previous = _motor;
                PlayerMotor.Step(ref _motor, move, _jumpQueued, (float)_step.StepSeconds, maxSpeed, _shape.JumpVelocity, _world, _rules, PassThrough);
                PassThrough.Prune(_world, _motor.Position, _rules.BodyRadius, _rules.BodyHeight);

                _jumpQueued = false;
                // Paced by the step's own time, not the frame's, so reports stay evenly spaced at any frame rate.
                if (_session.TickOfStep(stepNumber, StepsPerSecond, out var tick) && _reporter.ShouldReport(_motor.Velocity, stepNumber * _step.StepSeconds))
                {
                    _session.ReportPosition(tick, _motor.Position, _motor.Velocity, _motor.Facing);
                }
            }

            _blend.Advance(Time.unscaledDeltaTime);
            Draw(_step.Alpha);
            var time = Time.unscaledTime;
            if (_fade.Step(time, Time.unscaledDeltaTime, _fade.Holding ? _held.y : _drawn.Y, _world.KillHeight))
            {
                Arrived(time);
            }
        }

        // Faded out where it stood, the shape is drawn at its new place: the camera cuts there, or glides there for a
        // near respawn in the same zone, and the shape fades in after.
        private void Arrived(float now)
        {
            Draw(_step.Alpha);
            // Measured across the ground: a fall ends far below wherever it started.
            var across = Vector2.Distance(new Vector2(_held.x, _held.z), new Vector2(transform.position.x, transform.position.z));
            if (_moveReason == CorrectionReason.Respawn && !_zoneChanged && across <= respawnGlideDistance)
            {
                _camera.Glide(_held, respawnGlideSeconds);
                _fade.HideFor(now, respawnGlideSeconds);
            }

            _zoneChanged = false;
        }

        private void Draw(float alpha)
        {
            _drawn = System.Numerics.Vector3.Lerp(_previous.Position, _motor.Position, alpha) + _blend.Offset;
            var facing = Mathf.LerpAngle(_previous.Facing * Mathf.Rad2Deg, _motor.Facing * Mathf.Rad2Deg, alpha);
            if (!_fade.Holding)
            {
                transform.SetPositionAndRotation(Vectors.ToUnity(_drawn), Quaternion.Euler(0, facing, 0));
            }
        }

        private void OnCorrected(PositionCorrection correction)
        {
            if (correction.Reason is CorrectionReason.Respawn or CorrectionReason.Teleport && !_fade.Holding)
            {
                _held = transform.position;
            }

            _motor.Position = new System.Numerics.Vector3(correction.X, correction.Y, correction.Z);
            _motor.Velocity = System.Numerics.Vector3.Zero;
            _motor.Grounded = false;
            _previous = _motor;
            _reporter.Reset();
            if (correction.Reason is CorrectionReason.Respawn or CorrectionReason.Teleport)
            {
                // Usually faded already (from the fall, or the pad's charge); if not, it fades out now, held where it stood.
                _blend.Clear();
                _moveReason = correction.Reason;
                _fade.Moved(Time.unscaledTime);
                if (correction.Reason == CorrectionReason.Respawn)
                {
                    Respawns++;
                }
                else
                {
                    Teleports++;
                }
            }
            else
            {
                _blend.Begin(_drawn, _motor.Position);
                SnapBacks++;
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
