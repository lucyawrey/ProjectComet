using Comet.Content;

namespace Comet.Simulation
{
    public sealed class TriggerCharge
    {
        private object? _inside;
        private TriggerVolume? _volume;
        private float _since;
        private bool _fired;

        public bool Step(object? inside, TriggerVolume? volume, float seconds)
        {
            if (inside != _inside)
            {
                _inside = inside;
                _volume = volume;
                _since = seconds;
                _fired = false;
            }

            if (_inside == null || _volume == null || _fired || seconds - _since < _volume.StandSeconds)
            {
                return false;
            }

            if (_volume.Rearm == Rearm.Repeat)
            {
                _since = seconds;
            }
            else
            {
                _fired = true;
            }

            return true;
        }

        public void Arrived(object inside, TriggerVolume volume)
        {
            _inside = inside;
            _volume = volume;
            _fired = true;
        }

        public void Clear()
        {
            _inside = null;
            _volume = null;
        }

        /// <summary>How far toward firing at <paramref name="seconds"/>, 0 to 1; 0 when standing in nothing.</summary>
        public float Progress(float seconds)
        {
            if (_inside == null || _volume == null || _fired)
            {
                return 0;
            }

            return _volume.StandSeconds <= 0 ? 1 : System.Math.Min(1, (seconds - _since) / _volume.StandSeconds);
        }

        public object? Inside => _inside;

        public bool Fired => _inside != null && _fired;
    }
}
