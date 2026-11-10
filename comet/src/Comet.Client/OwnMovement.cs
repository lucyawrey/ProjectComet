using System.Numerics;
using Comet.Simulation;

namespace Comet.Client
{
    public sealed class OwnMovement
    {
        private readonly int _stepsPerSecond;
        private readonly float _stepSeconds;

        public OwnMovement(int stepsPerSecond)
        {
            _stepsPerSecond = stepsPerSecond;
            _stepSeconds = 1f / stepsPerSecond;
        }

        public PassThrough PassThrough { get; } = new PassThrough();

        public PositionReporter Reporter { get; } = new PositionReporter();

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
