using System.Numerics;
using Comet.Simulation;

namespace Comet.Client
{
    /// <summary>
    /// The player's own movement on a client: one <see cref="PlayerMotor"/> step at a time, the meshes they pass
    /// through, and reporting the steps that fall on a server tick, paced by <see cref="Reporter"/>.
    /// </summary>
    public sealed class OwnMovement
    {
        private readonly int _stepsPerSecond;
        private readonly float _stepSeconds;

        /// <param name="stepsPerSecond">A multiple or a divisor of the server's tick rate.</param>
        public OwnMovement(int stepsPerSecond)
        {
            _stepsPerSecond = stepsPerSecond;
            _stepSeconds = 1f / stepsPerSecond;
        }

        /// <summary>The meshes the player passes through: turned on around them, until they're out (as the server keeps them).</summary>
        public PassThrough PassThrough { get; } = new PassThrough();

        public PositionReporter Reporter { get; } = new PositionReporter();

        /// <summary>
        /// Moves <paramref name="motor"/> by step <paramref name="stepNumber"/> (counted on the server's
        /// clock), and reports the new state to <paramref name="session"/> if the step falls on a server
        /// tick and <see cref="Reporter"/> wants it. Returns whether it reported, at <paramref
        /// name="tick"/>.
        /// </summary>
        public bool Step(
            ClientSession session,
            long stepNumber,
            ref MotorState motor,
            Vector2 move,
            bool jump,
            float maxSpeed,
            float jumpVelocity,
            CollisionWorld world,
            MovementRules rules,
            out uint tick
        )
        {
            PlayerMotor.Step(ref motor, move, jump, _stepSeconds, maxSpeed, jumpVelocity, world, rules, PassThrough);
            PassThrough.Prune(world, motor.Position, rules.BodyRadius, rules.BodyHeight);
            // Paced by the step's own time, not the frame's, so reports stay evenly spaced at any frame rate.
            if (
                session.TickOfStep(stepNumber, _stepsPerSecond, out tick)
                && Reporter.ShouldReport(motor.Velocity, stepNumber / (double)_stepsPerSecond)
            )
            {
                session.ReportPosition(tick, motor.Position, motor.Velocity, motor.Facing);
                return true;
            }

            return false;
        }
    }
}
