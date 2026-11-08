using System;
using System.Collections.Generic;

namespace Comet.Client
{
    /// <summary> How far behind the receive timeline other entities are drawn, adapted to what arriving states
    /// need. One delay is shared by every entity, so they're all drawn at the same moment. </summary> <remarks>
    /// A sample is how far the receive tick has run past an entity's previous stamp when its state arrives. The
    /// target is a <see cref="Percentile"/> of recent samples plus <see cref="MarginTicks"/>, never under <see
    /// cref="FloorTicks"/> (two report intervals), and holds the highest of the last <see cref="HoldSeconds"/>:
    /// over TCP a lost packet stalls every entity at once, and a target that fell as soon as the stall left the
    /// window left the next one short. </remarks>
    public sealed class InterpolationDelay
    {
        /// <summary>How far back samples count toward the target, in seconds.</summary>
        public const double WindowSeconds = 2;

        /// <summary>The share of samples the delay should cover (0.95 covers all but the slowest 5%).</summary>
        public const double Percentile = 0.95;

        /// <summary>How much slower than real time the render tick may run while the delay grows (0.04 is 4%).</summary>
        public const double GrowRate = 0.04;

        /// <summary>How much faster than real time the render tick may run while the delay shrinks.</summary>
        public const double ShrinkRate = 0.02;

        /// <summary>How long a raw target holds before a lower one can replace it, in seconds.</summary>
        public const double HoldSeconds = 5;

        private const int MaxSamples = 1024;

        private const double TargetInterval = 0.25;

        private readonly Queue<Sample> _samples = new Queue<Sample>();
        private readonly Queue<Sample> _raw = new Queue<Sample>(); // raw targets, held for HoldSeconds
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

        /// <summary>The least the delay can be, in ticks.</summary>
        public double FloorTicks { get; }

        /// <summary>Added to the measured percentile, in ticks.</summary>
        public double MarginTicks { get; }

        /// <summary>The delay now, in ticks.</summary>
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

        /// <summary>
        /// Moves the delay toward the target by running the render tick at most <see cref="GrowRate"/> slow or <see cref="ShrinkRate"/> fast,
        /// so drawn motion never jumps and the render tick never goes back. Call once per update.
        /// </summary>
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
