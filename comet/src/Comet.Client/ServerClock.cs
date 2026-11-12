using System;

namespace Comet.Client
{
    public sealed class ServerClock
    {
        public const int SampleCount = 10;

        /// <summary>How much faster or slower than real time the estimates may run while catching up (0.05 is 5%).</summary>
        public const double MaxSlew = 0.05;

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

        public bool Synced => _sampleTotal > 0;

        /// <summary>The trusted sample's round trip, in seconds (0 before the first pong).</summary>
        public double RoundTrip { get; private set; }

        public double LastRoundTrip { get; private set; }

        public double ServerTick(double now) => now * TickRate + _serverOffset;

        public double ReceiveTick(double now) => now * TickRate + _receiveOffset;

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

        private static double Center(uint tick) => tick + 0.5;

        private readonly struct Sample
        {
            public Sample(double roundTrip, double arrivalOffset)
            {
                RoundTrip = roundTrip;
                ArrivalOffset = arrivalOffset;
            }

            public double RoundTrip { get; }

            public double ArrivalOffset { get; }
        }
    }
}
