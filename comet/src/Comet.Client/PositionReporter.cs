using System;
using System.Numerics;

namespace Comet.Client
{
    public sealed class PositionReporter
    {
        private const float MovingSpeedSquared = 0.01f;

        private readonly float _sharpTurnCos;
        private bool _wasMoving;
        private Vector2 _reportedDirection;
        private double _lastReport = double.NegativeInfinity;

        public PositionReporter(double interval = 1.0 / 15, float sharpTurnDegrees = 45)
        {
            Interval = interval;
            _sharpTurnCos = MathF.Cos(sharpTurnDegrees * MathF.PI / 180);
        }

        /// <summary>How often the position is reported while moving, in seconds.</summary>
        public double Interval { get; }

        // Step times round, so exactly four steps can come out a hair short of the interval; without this tolerance the report waits two more steps.
        private const double TimeTolerance = 1e-6;

        public bool ShouldReport(Vector3 velocity, double now)
        {
            var moving = velocity.LengthSquared() > MovingSpeedSquared;
            var ground = new Vector2(velocity.X, velocity.Z);
            var direction = ground.LengthSquared() > MovingSpeedSquared ? Vector2.Normalize(ground) : Vector2.Zero;

            var report =
                moving != _wasMoving
                || moving && now - _lastReport >= Interval - TimeTolerance
                || direction != Vector2.Zero && _reportedDirection != Vector2.Zero && Vector2.Dot(direction, _reportedDirection) < _sharpTurnCos;

            _wasMoving = moving;
            if (report)
            {
                _lastReport = now;
                _reportedDirection = direction;
            }

            return report;
        }

        public void Reset()
        {
            _wasMoving = false;
            _reportedDirection = Vector2.Zero;
            _lastReport = double.NegativeInfinity;
        }
    }
}
