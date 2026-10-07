using System;

namespace Comet.Client
{
    /// <summary>
    /// Paces a fixed-rate simulation from a variable frame clock: each frame says how many whole steps are due,
    /// and how far the frame sits between the last step and the next one (for drawing between them).
    /// </summary>
    public sealed class FixedStep
    {
        private double _last = double.NaN;
        private double _owed;

        /// <param name="stepsPerSecond">The simulation rate.</param>
        /// <param name="maxSteps">The most steps one frame runs; time beyond that (a long hitch) is dropped, so the simulation slows instead of spiralling.</param>
        public FixedStep(int stepsPerSecond, int maxSteps = 15)
        {
            if (stepsPerSecond <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stepsPerSecond));
            }

            StepSeconds = 1.0 / stepsPerSecond;
            MaxSteps = maxSteps;
        }

        public double StepSeconds { get; }

        public int MaxSteps { get; }

        /// <summary>From 0 to 1: how far past the last step the frame is, as a fraction of a step.</summary>
        public float Alpha => (float)(_owed / StepSeconds);

        /// <summary>Advances to <paramref name="now"/> and returns how many steps to run. The first call starts the clock and runs none.</summary>
        public int Advance(double now)
        {
            if (double.IsNaN(_last))
            {
                _last = now;
                return 0;
            }

            _owed += Math.Max(0, now - _last);
            _last = now;
            var steps = (int)Math.Floor(_owed / StepSeconds);
            if (steps > MaxSteps)
            {
                steps = MaxSteps;
                _owed = 0;
            }
            else
            {
                _owed -= steps * StepSeconds;
            }

            return steps;
        }

        /// <summary>Starts again at the next <see cref="Advance"/> (after a pause or a teleport).</summary>
        public void Reset()
        {
            _last = double.NaN;
            _owed = 0;
        }
    }
}
