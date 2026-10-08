using UnityEngine;

namespace ShapeLand.Client
{
    /// <summary>
    /// How falling off and respawning fade, with its numbers in one place (placeholders, adjusted by playing). A
    /// respawn arrives without warning, so the fade starts while falling: your own screen darkens as you fall
    /// towards the world's kill height, is already dark when the server's respawn arrives, and lightens again
    /// after it. Other players' shapes fade out and back in the same way, which leaves a snap-back (made at full
    /// visibility) as a plain jump.
    /// </summary>
    public static class RespawnFade
    {
        /// <summary>
        /// How far above the kill height falling starts to fade, in metres. At the island's falling speeds this
        /// takes about half a second; the island's lowest ground is well above where it starts.
        /// </summary>
        public const float FadeDistance = 18f;

        /// <summary>Seconds to fade back in after a respawn.</summary>
        public const float FadeInSeconds = 0.4f;

        /// <summary>How faded something at height <paramref name="y"/> is from falling: 0 above the fade, 1 at the kill height and below.</summary>
        public static float FromFalling(float y, float killHeight) => Mathf.Clamp01((killHeight + FadeDistance - y) / FadeDistance);

        /// <summary>
        /// The next amount faded (0 clear, 1 gone): it follows falling at once, and eases back at the fade-in
        /// rate once the height no longer calls for it.
        /// </summary>
        public static float Step(float faded, float y, float killHeight, float seconds) =>
            Mathf.Max(FromFalling(y, killHeight), faded - seconds / FadeInSeconds);
    }
}
