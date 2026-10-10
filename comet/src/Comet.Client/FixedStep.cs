using System;

namespace Comet.Client
{
    /// <summary>
    /// Paces a fixed-rate simulation from a variable frame clock: each frame says how many whole steps are due,
    /// and how far the frame sits between the last step and the next one (for drawing between them). Steps fall
    /// on a grid, step n at n / <see cref="StepsPerSecond"/> seconds, so a simulation driven by the server-tick
    /// estimate (<see cref="ClientSession.ServerSeconds"/>) steps exactly on server ticks
    /// (<see cref="ClientSession.TickOfStep"/>).
    /// </summary>
    public sealed class FixedStep
    {
        private bool _started;
        private long _lastStep;
        private double _now;

        /// <param name="stepsPerSecond">The simulation rate.</param>
        /// <param name="maxSteps">The most steps one frame runs; time beyond that (a long hitch) is dropped, so the simulation slows instead of spiralling.</param>
        public FixedStep(int stepsPerSecond, int maxSteps = 15)
        {
            if (stepsPerSecond <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stepsPerSecond));
            }

            StepsPerSecond = stepsPerSecond;
            StepSeconds = 1.0 / stepsPerSecond;
            MaxSteps = maxSteps;
        }

        public int StepsPerSecond { get; }

        public double StepSeconds { get; }

        public int MaxSteps { get; }

        /// <summary>The number of the last step run (or of the grid point the clock started at).</summary>
        public long LastStep => _lastStep;

        /// <summary>From 0 to 1: how far past the last step the frame is, as a fraction of a step.</summary>
        public float Alpha => _started ? (float)Math.Min(1, Math.Max(0, _now * StepsPerSecond - _lastStep)) : 0;

        /// <summary>
        /// Advances to <paramref name="now"/> and returns how many steps to run; the last of them is
        /// <see cref="LastStep"/>. The first call starts the clock and runs none. Time going backwards runs nothing
        /// until it passes the last step again.
        /// </summary>
        public int Advance(double now)
        {
            _now = now;
            var due = (long)Math.Floor(now * StepsPerSecond);
            if (!_started)
            {
                _started = true;
                _lastStep = due;
                return 0;
            }

            if (due <= _lastStep)
            {
                return 0;
            }

            var steps = due - _lastStep;
            _lastStep = due;
            return (int)Math.Min(steps, MaxSteps);
        }

        /// <summary>Starts again at the next <see cref="Advance"/> (after a pause or a teleport).</summary>
        public void Reset()
        {
            _started = false;
        }
    }
}
