using System;
using System.Numerics;

namespace Comet.Client
{
    /// <summary>
    /// Decides when the player's own position is reported: every <see cref="Interval"/> while moving, at once
    /// on starting, stopping or turning sharply, and not at all while standing still.
    /// </summary>
    public sealed class PositionReporter
    {
        private const float MovingSpeedSquared = 0.01f;

        private readonly float _sharpTurnCos;
        private bool _wasMoving;
        private Vector2 _reportedDirection;
        private double _lastReport = double.NegativeInfinity;

        /// <param name="interval">Seconds between reports while moving.</param>
        /// <param name="sharpTurnDegrees">A change of ground direction at least this large is reported at once.</param>
        public PositionReporter(double interval = 1.0 / 15, float sharpTurnDegrees = 45)
        {
            Interval = interval;
            _sharpTurnCos = MathF.Cos(sharpTurnDegrees * MathF.PI / 180);
        }

        public double Interval { get; }

        /// <summary>Whether to report at <paramref name="now"/>, given the player's velocity after this step.</summary>
        public bool ShouldReport(Vector3 velocity, double now)
        {
            var moving = velocity.LengthSquared() > MovingSpeedSquared;
            var ground = new Vector2(velocity.X, velocity.Z);
            var direction = ground.LengthSquared() > MovingSpeedSquared ? Vector2.Normalize(ground) : Vector2.Zero;

            var report = moving != _wasMoving
                || moving && now - _lastReport >= Interval
                || direction != Vector2.Zero && _reportedDirection != Vector2.Zero && Vector2.Dot(direction, _reportedDirection) < _sharpTurnCos;

            _wasMoving = moving;
            if (report)
            {
                _lastReport = now;
                _reportedDirection = direction;
            }

            return report;
        }

        /// <summary>Forgets what was last reported, so the next moving step reports at once (after a correction).</summary>
        public void Reset()
        {
            _wasMoving = false;
            _reportedDirection = Vector2.Zero;
            _lastReport = double.NegativeInfinity;
        }
    }
}
