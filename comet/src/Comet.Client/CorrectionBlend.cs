using System;
using System.Numerics;

namespace Comet.Client
{
    /// <summary>
    /// Smooths a snap-back for the player's own shape. The simulation moves to the corrected position at once,
    /// so its next reports pass the server's checks; only the drawn position lags, by an offset that eases to
    /// zero over <see cref="Duration"/>. A respawn isn't blended (the client fades instead).
    /// </summary>
    public sealed class CorrectionBlend
    {
        private Vector3 _start;
        private double _elapsed;

        /// <param name="duration">How long the drawn position takes to catch up, in seconds.</param>
        public CorrectionBlend(double duration = 0.15)
        {
            if (duration <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }

            Duration = duration;
            _elapsed = duration;
        }

        public double Duration { get; }

        /// <summary>Add this to the simulated position to get the drawn one.</summary>
        public Vector3 Offset { get; private set; }

        public bool Active => _elapsed < Duration;

        /// <summary>
        /// Starts a blend from where the shape is drawn now to the corrected simulated position. A correction
        /// during a blend starts from the current drawn position, so the shape never jumps.
        /// </summary>
        public void Begin(Vector3 drawnPosition, Vector3 correctedPosition)
        {
            _start = drawnPosition - correctedPosition;
            _elapsed = 0;
            Offset = _start;
        }

        /// <summary>Ends any blend, for a respawn or teleport.</summary>
        public void Clear()
        {
            _elapsed = Duration;
            Offset = Vector3.Zero;
        }

        public void Advance(double seconds)
        {
            if (!Active)
            {
                return;
            }

            _elapsed = Math.Min(Duration, _elapsed + Math.Max(0, seconds));
            var t = (float)(_elapsed / Duration);
            var eased = t * t * (3 - 2 * t); // smoothstep: starts and ends gently
            Offset = _start * (1 - eased);
        }
    }
}
