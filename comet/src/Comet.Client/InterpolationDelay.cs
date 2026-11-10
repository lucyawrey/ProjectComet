using System;
using System.Collections.Generic;

namespace Comet.Client
{
    public sealed class InterpolationDelay
    {
        public const double WindowSeconds = 2;

        /// <summary>The share of samples the delay should cover (0.95 covers all but the slowest 5%).</summary>
        public const double Percentile = 0.95;

        /// <summary>How much slower than real time the render tick may run while the delay grows (0.04 is 4%).</summary>
        public const double GrowRate = 0.04;

        public const double ShrinkRate = 0.02;

        public const double HoldSeconds = 5;

        private const int MaxSamples = 1024;

        private const double TargetInterval = 0.25;

        private readonly Queue<Sample> _samples = new Queue<Sample>();
        private readonly Queue<Sample> _raw = new Queue<Sample>();
        private double[] _sorted = new double[64];
        private double _lastAdvance = double.NaN;
        private double _nextTarget;

        /// <param name="reportInterval">How often moving entities are updated, in seconds.</param>
        public InterpolationDelay(int tickRate, double reportInterval = 1.0 / 15)
        {
            if (tickRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tickRate));
            }

            TickRate = tickRate;
            FloorTicks = 2 * reportInterval * tickRate;
            MarginTicks = 1;
            Ticks = Target = FloorTicks;
        }

        public int TickRate { get; }

        public double FloorTicks { get; }

        public double MarginTicks { get; }

        public double Ticks { get; private set; }

        public double Seconds => Ticks / TickRate;

        /// <summary>The delay being moved toward, in ticks.</summary>
        public double Target { get; private set; }

        /// <summary>
        /// A state arrived needing a delay of <paramref name="neededTicks"/> (receive tick at arrival minus
        /// the entity's previous stamp).
        /// </summary>
        public void AddSample(double neededTicks, double now)
        {
            _samples.Enqueue(new Sample(now, neededTicks));
            if (_samples.Count > MaxSamples)
            {
                _samples.Dequeue();
            }
        }

        public void Advance(double now)
        {
            if (now >= _nextTarget)
            {
                _nextTarget = now + TargetInterval;
                UpdateTarget(now);
            }

            var elapsed = double.IsNaN(_lastAdvance) ? 0 : Math.Max(0, now - _lastAdvance);
            _lastAdvance = now;
            var difference = Target - Ticks;
            var maxStep = elapsed * TickRate * (difference > 0 ? GrowRate : ShrinkRate);
            Ticks += Math.Max(-maxStep, Math.Min(maxStep, difference));
        }

        private void UpdateTarget(double now)
        {
            while (_samples.Count > 0 && _samples.Peek().Time < now - WindowSeconds)
            {
                _samples.Dequeue();
            }

            if (_samples.Count == 0)
            {
                return;
            }

            if (_sorted.Length < _samples.Count)
            {
                _sorted = new double[Math.Max(_samples.Count, _sorted.Length * 2)];
            }

            var count = 0;
            foreach (var sample in _samples)
            {
                _sorted[count++] = sample.NeededTicks;
            }

            Array.Sort(_sorted, 0, count);
            var needed = _sorted[Math.Min(count - 1, (int)(Percentile * count))];
            _raw.Enqueue(new Sample(now, Math.Max(FloorTicks, needed + MarginTicks)));
            while (_raw.Peek().Time < now - HoldSeconds)
            {
                _raw.Dequeue();
            }

            var target = 0.0;
            foreach (var raw in _raw)
            {
                target = Math.Max(target, raw.NeededTicks);
            }

            Target = target;
        }

        private readonly struct Sample
        {
            public Sample(double time, double neededTicks)
            {
                Time = time;
                NeededTicks = neededTicks;
            }

            public double Time { get; }

            public double NeededTicks { get; }
        }
    }
}
