using UnityEngine;

namespace ShapeLand.Client
{
    public sealed class ShapeFade
    {
        public const float FadeDistance = 18f;

        public const float FadeOutSeconds = 0.25f;

        public const float FadeInSeconds = 0.4f;

        public const float WaitSeconds = 1f;

        private float? _outSince;
        private bool _moved;
        private float _hiddenUntil;

        public float Faded { get; private set; }

        public static float FromFalling(float y, float killHeight) => Mathf.Clamp01((killHeight + FadeDistance - y) / FadeDistance);

        public void Depart(float now) => _outSince ??= now - Faded * FadeOutSeconds;

        public void DepartFrom(float since) => _outSince ??= since;

        public void Cancel()
        {
            if (!_moved)
            {
                _outSince = null;
            }
        }

        public void Moved(float now)
        {
            Depart(now);
            _moved = true;
        }

        public bool Departing => _outSince != null;

        public bool Holding => _outSince != null && _moved;

        public void StartHidden() => Faded = 1;

        public void HideFor(float now, float seconds) => _hiddenUntil = Mathf.Max(_hiddenUntil, now + seconds);

        public bool Step(float now, float seconds, float y, float killHeight)
        {
            var released = false;
            var outFaded = 0f;
            if (_outSince is float since)
            {
                outFaded = Mathf.Clamp01((now - since) / FadeOutSeconds);
                if (outFaded >= 1 && (_moved || now - since >= FadeOutSeconds + WaitSeconds))
                {
                    released = _moved;
                    _outSince = null;
                    _moved = false;
                }
            }

            var hidden = now < _hiddenUntil ? 1 : 0;
            Faded = Mathf.Max(Mathf.Max(FromFalling(y, killHeight), outFaded), Mathf.Max(hidden, Faded - seconds / FadeInSeconds));
            return released;
        }
    }
}
