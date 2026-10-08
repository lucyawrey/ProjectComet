using System;
using System.Collections.Generic;

namespace Comet.Client
{
    /// <summary>
    /// How far behind the receive timeline other entities are drawn, adapted to what arriving states need. One
    /// delay is shared by every entity, so they're all drawn at the same moment.
    /// </summary>
    /// <remarks>
    /// Each state arriving while its entity moves gives a sample: how far the receive tick has run past the
    /// entity's previous stamp. Drawing at least that far behind means the state arrived before the render tick
    /// needed it. That covers the gap between reports, the sender's trip to the server (states carry the
    /// sender's own stamp), the server's tick wait and jitter. The target is the <see cref="Percentile"/> of the
    /// samples from the last <see cref="WindowSeconds"/>, plus <see cref="MarginTicks"/>, and never under
    /// <see cref="FloorTicks"/> (two report intervals, the usual rule). The delay moves towards the target by
    /// running the render tick at most <see cref="GrowRate"/> slow or <see cref="ShrinkRate"/> fast, so drawn
    /// motion never jumps and the render tick never goes back. With no samples (nobody moving) the target stays
    /// where it was. Times are seconds on the caller's clock; delays are in ticks.
    /// </remarks>
    public sealed class InterpolationDelay
    {
        public const double WindowSeconds = 2;
        public const double Percentile = 0.95;
        public const double GrowRate = 0.04;
        public const double ShrinkRate = 0.02;

        // Enough for a crowded view over the window; older samples are dropped first beyond this.
        private const int MaxSamples = 1024;

        // The target is recomputed this often rather than on every sample.
        private const double TargetInterval = 0.25;

        private readonly Queue<Sample> _samples = new Queue<Sample>();
        private double[] _sorted = new double[64];
        private double _lastAdvance = double.NaN;
        private double _nextTarget;

        /// <param name="tickRate">Server ticks per second.</param>
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

        /// <summary>The delay now, in seconds.</summary>
        public double Seconds => Ticks / TickRate;

        /// <summary>The delay being moved towards, in ticks.</summary>
        public double Target { get; private set; }

        /// <summary>A state arrived needing a delay of <paramref name="neededTicks"/> (receive tick at arrival minus the entity's previous stamp).</summary>
        public void AddSample(double neededTicks, double now)
        {
            _samples.Enqueue(new Sample(now, neededTicks));
            if (_samples.Count > MaxSamples)
            {
                _samples.Dequeue();
            }
        }

        /// <summary>Moves the delay towards the target. Call once per update.</summary>
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
            Target = Math.Max(FloorTicks, needed + MarginTicks);
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
