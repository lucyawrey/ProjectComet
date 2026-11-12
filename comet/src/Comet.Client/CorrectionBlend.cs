using System;
using System.Numerics;

namespace Comet.Client
{
    public sealed class CorrectionBlend
    {
        private Vector3 _start;
        private double _elapsed;

        public CorrectionBlend(double duration = 0.15)
        {
            if (duration <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }

            Duration = duration;
            _elapsed = duration;
        }

        /// <summary>How long a correction takes to ease out, in seconds.</summary>
        public double Duration { get; }

        public Vector3 Offset { get; private set; }

        public bool Active => _elapsed < Duration;

        public void Begin(Vector3 drawnPosition, Vector3 correctedPosition)
        {
            _start = drawnPosition - correctedPosition;
            _elapsed = 0;
            Offset = _start;
        }

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
            var eased = t * t * (3 - 2 * t);
            Offset = _start * (1 - eased);
        }
    }
}
