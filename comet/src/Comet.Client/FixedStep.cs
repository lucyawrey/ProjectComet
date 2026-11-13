using System;

namespace Comet.Client
{
    public sealed class FixedStep
    {
        private bool _started;
        private long _lastStep;
        private double _now;

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

        public long LastStep => _lastStep;

        /// <summary>From 0 to 1: how far past the last step the frame is, as a fraction of a step.</summary>
        public float Alpha => _started ? (float)Math.Min(1, Math.Max(0, _now * StepsPerSecond - _lastStep)) : 0;

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

        public void Reset()
        {
            _started = false;
        }
    }
}
