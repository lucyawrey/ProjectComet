using System;

namespace Comet.Client
{
    /// <summary>
    /// Estimates the server's tick from pings. Two timelines come out of it: <see cref="ServerTick"/>, the
    /// tick the server is on now (for stamping inputs), and <see cref="ReceiveTick"/>, the tick of frames
    /// arriving now (half a round trip behind), which interpolation draws behind.
    /// </summary>
    /// <remarks>
    /// The lowest round trip among the last <see cref="SampleCount"/> pongs is trusted (it waited in the fewest queues); the server is
    /// about half a round trip past its frame's stamp. Estimates slew toward it (at most <see cref="MaxSlew"/>) so drawing doesn't stutter,
    /// and jump past <see cref="JumpSeconds"/>. Times are seconds on the caller's monotonic clock.
    /// </remarks>
    public sealed class ServerClock
    {
        public const int SampleCount = 10;

        /// <summary>How much faster or slower than real time the estimates may run while catching up (0.05 is 5%).</summary>
        public const double MaxSlew = 0.05;

        /// <summary>Off by more than this, in seconds, the estimates jump instead of slewing.</summary>
        public const double JumpSeconds = 0.25;

        private readonly Sample[] _samples = new Sample[SampleCount];
        private int _sampleTotal;
        private double _serverOffset;
        private double _receiveOffset;
        private double _serverTarget;
        private double _receiveTarget;
        private double _lastAdvance;
        private bool _started;

        public ServerClock(int tickRate)
        {
            if (tickRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tickRate));
            }

            TickRate = tickRate;
        }

        public int TickRate { get; }

        /// <summary>True once a pong has arrived; before that the estimate comes from frame headers alone.</summary>
        public bool Synced => _sampleTotal > 0;

        /// <summary>The trusted sample's round trip, in seconds (0 before the first pong).</summary>
        public double RoundTrip { get; private set; }

        /// <summary>The latest pong's round trip, in seconds (0 before the first pong).</summary>
        public double LastRoundTrip { get; private set; }

        /// <summary>The tick the server is on at <paramref name="now"/>, with a fraction.</summary>
        public double ServerTick(double now) => now * TickRate + _serverOffset;

        /// <summary>The tick of frames arriving at <paramref name="now"/>, with a fraction.</summary>
        public double ReceiveTick(double now) => now * TickRate + _receiveOffset;

        /// <summary>A frame arrived. Until the first pong, this sets a rough estimate (no round trip known yet).</summary>
        public void OnFrame(uint tick, double now)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _lastAdvance = now;
            _serverOffset = _receiveOffset = _serverTarget = _receiveTarget = Center(tick) - now * TickRate;
        }

        public void OnPong(uint tick, double sentAt, double now)
        {
            var roundTrip = Math.Max(0, now - sentAt);
            LastRoundTrip = roundTrip;
            _samples[_sampleTotal % SampleCount] = new Sample(roundTrip, Center(tick) - now * TickRate);
            _sampleTotal++;

            var best = _samples[0];
            for (var i = 1; i < Math.Min(_sampleTotal, SampleCount); i++)
            {
                if (_samples[i].RoundTrip < best.RoundTrip)
                {
                    best = _samples[i];
                }
            }

            RoundTrip = best.RoundTrip;
            _receiveTarget = best.ArrivalOffset;
            _serverTarget = best.ArrivalOffset + best.RoundTrip / 2 * TickRate;
            if (_sampleTotal == 1 || !_started)
            {
                _started = true;
                _lastAdvance = now;
                _serverOffset = _serverTarget;
                _receiveOffset = _receiveTarget;
            }
        }

        public void Advance(double now)
        {
            var elapsed = Math.Max(0, now - _lastAdvance);
            _lastAdvance = now;
            var maxStep = elapsed * TickRate * MaxSlew;
            var jump = JumpSeconds * TickRate;
            _serverOffset = Approach(_serverOffset, _serverTarget, maxStep, jump);
            _receiveOffset = Approach(_receiveOffset, _receiveTarget, maxStep, jump);
        }

        private static double Approach(double current, double target, double maxStep, double jump)
        {
            var difference = target - current;
            if (Math.Abs(difference) > jump)
            {
                return target;
            }

            return current + Math.Max(-maxStep, Math.Min(maxStep, difference));
        }

        // A frame stamped with tick T is sent while the server is somewhere in tick T, so assume its middle.
        private static double Center(uint tick) => tick + 0.5;

        private readonly struct Sample
        {
            public Sample(double roundTrip, double arrivalOffset)
            {
                RoundTrip = roundTrip;
                ArrivalOffset = arrivalOffset;
            }

            public double RoundTrip { get; }

            /// <summary>The receive-tick offset this sample implies: the frame's tick against its arrival time.</summary>
            public double ArrivalOffset { get; }
        }
    }
}
