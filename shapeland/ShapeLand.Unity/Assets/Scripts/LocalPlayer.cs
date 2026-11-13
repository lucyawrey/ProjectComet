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
    public sealed class LocalPlayer : MonoBehaviour
    {
        [Tooltip("After a respawn, how long the camera glides to the spawn before the shape fades in, in seconds.")]
        [SerializeField]
        private float respawnGlideSeconds = 0.5f;

        [Tooltip("The farthest a respawn glides the camera, in meters across the ground; farther, or into another zone, it cuts.")]
        [SerializeField]
        private float respawnGlideDistance = 30f;

        public const int StepsPerSecond = 60;

        public float TestSpeedScale { get; set; } = 1;

        private readonly FixedStep _step = new FixedStep(StepsPerSecond);
        private readonly OwnMovement _movement = new OwnMovement(StepsPerSecond);
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

        public bool InputEnabled { get; set; } = true;

        public MotorState Motor => _motor;

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

        public PassThrough PassThrough => _movement.PassThrough;

        public int Teleports { get; private set; }

        public int SnapBacks { get; private set; }

        public int Respawns { get; private set; }

        public float BlendDistance => _blend.Offset.Length();

        public float Faded => _fade.Faded;

        public bool Departing => _fade.Departing;

        public void TeleportSoon(float seconds) => _fade.DepartFrom(Time.unscaledTime + seconds - ShapeFade.FadeOutSeconds);

        public void TeleportCancelled() => _fade.Cancel();

        public void Begin(
            ClientSession session,
            CollisionWorld world,
            MovementRules rules,
            Shape shape,
            Vector3 position,
            float facing,
            OrbitCamera orbitCamera,
            ShapeLandControls controls
        )
        {
            _rules = rules;
            _session = session;
            _world = world;
            _shape = shape;
            _camera = orbitCamera;
            _controls = controls;
            _motor = new MotorState
            {
                Position = Conversions.ToNumerics(position),
                Grounded = true,
                Facing = facing,
            };
            _previous = _motor;
            _session.Corrected += OnCorrected;
            Draw(1);
        }

        public void ChangeShape(Shape shape) => _shape = shape;

        public void Flipped(string tag) => PassThrough.Flipped(_world, tag, _motor.Position, _rules.BodyRadius, _rules.BodyHeight);

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

            var move =
                InputEnabled && !_fade.Holding ? CameraRelative(_controls.Game.Move.ReadValue<Vector2>(), _camera.Yaw) : System.Numerics.Vector2.Zero;
            _jumpQueued |= InputEnabled && !_fade.Holding && _controls.Game.Jump.WasPressedThisFrame();

            var now = CometConnection.Now;
            var maxSpeed = _shape.MaxSpeed * TestSpeedScale;

            // The steps run on the server's clock so every other one falls on a server tick; only those are reported, stamped with their tick.
            var steps = _step.Advance(_session.ServerSeconds(now));
            for (var i = 0; i < steps; i++)
            {
                var stepNumber = _step.LastStep - (steps - 1 - i);
                _previous = _motor;
                _movement.Step(_session, stepNumber, ref _motor, move, _jumpQueued, maxSpeed, _shape.JumpVelocity, _world, _rules, out _);
                _jumpQueued = false;
            }

            _blend.Advance(Time.unscaledDeltaTime);
            Draw(_step.Alpha);
            var time = Time.unscaledTime;
            if (_fade.Step(time, Time.unscaledDeltaTime, _fade.Holding ? _held.y : _drawn.Y, _world.KillHeight))
            {
                Arrived(time);
            }
        }

        private void Arrived(float now)
        {
            Draw(_step.Alpha);
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
                transform.SetPositionAndRotation(Conversions.ToUnity(_drawn), Quaternion.Euler(0, facing, 0));
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
            _movement.Reporter.Reset();
            if (correction.Reason is CorrectionReason.Respawn or CorrectionReason.Teleport)
            {
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
