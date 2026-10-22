using UnityEngine;

namespace ShapeLand.Client
{
    /// <summary>
    /// How one drawn shape fades out of a place and back in at another, with the numbers in one place (placeholders,
    /// adjusted by playing). Falling fades a shape as it nears the world's kill height, so it's already gone when the
    /// respawn arrives. A teleport, or a move it wasn't faded for, fades it out first: it's held where it stood
    /// (<see cref="Holding"/>) until faded, then drawn at its new place, hidden for a moment if the caller asks
    /// (while the camera glides there), and fades back in. A snap-back isn't one of these: it stays a plain jump.
    /// </summary>
    public sealed class ShapeFade
    {
        /// <summary>
        /// How far above the kill height falling starts to fade, in meters. At the island's falling speeds this
        /// takes about half a second; the island's lowest ground is well above where it starts.
        /// </summary>
        public const float FadeDistance = 18f;

        /// <summary>Seconds to fade out before a teleport.</summary>
        public const float FadeOutSeconds = 0.25f;

        /// <summary>Seconds to fade back in after arriving.</summary>
        public const float FadeInSeconds = 0.4f;

        /// <summary>
        /// How long a fade-out started ahead of a move (the player's own pad finishing its charge) waits for it once
        /// faded, in seconds, before fading back in where it is: the server didn't send them after all.
        /// </summary>
        public const float WaitSeconds = 1f;

        private float? _outSince;
        private bool _moved;
        private float _hiddenUntil;

        /// <summary>How far the shape has faded: 0 fully visible, 1 gone.</summary>
        public float Faded { get; private set; }

        /// <summary>How faded something at height <paramref name="y"/> is from falling: 0 above the fade, 1 at the kill height and below.</summary>
        public static float FromFalling(float y, float killHeight) => Mathf.Clamp01((killHeight + FadeDistance - y) / FadeDistance);

        /// <summary>Starts fading out, from however faded it is now, for a move expected soon.</summary>
        public void Depart(float now) => _outSince ??= now - Faded * FadeOutSeconds;

        /// <summary>The move came: the shape stays where it was drawn until it has faded out (at once if it already has).</summary>
        public void Moved(float now)
        {
            Depart(now);
            _moved = true;
        }

        /// <summary>Whether the shape is fading out for a move, or held after it.</summary>
        public bool Departing => _outSince != null;

        /// <summary>Whether the shape is held at where it stood before the move, fading out.</summary>
        public bool Holding => _outSince != null && _moved;

        /// <summary>Starts hidden (a shape arriving into view), to fade in.</summary>
        public void StartHidden() => Faded = 1;

        /// <summary>Keeps the shape hidden for <paramref name="seconds"/> more before it fades in.</summary>
        public void HideFor(float now, float seconds) => _hiddenUntil = Mathf.Max(_hiddenUntil, now + seconds);

        /// <summary>
        /// Advances to <paramref name="now"/>, <paramref name="seconds"/> after the last step, at height
        /// <paramref name="y"/> above a kill height of <paramref name="killHeight"/>. Returns true on the step the
        /// hold ends, when the shape is drawn at its new place.
        /// </summary>
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
