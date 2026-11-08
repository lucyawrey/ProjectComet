using System.Text.RegularExpressions;
using Comet.Protocol;

namespace ShapeLand.Shared.World
{
    public static class ShapeLandRules
    {
        public const int TickRate = 30;

        public const int MinNameLength = 3;

        public const int MaxNameLength = 16;

        /// <summary>The longest chat line, in UTF-16 characters after normalizing.</summary>
        public const int MaxChatLength = 200;

        /// <summary>In seconds; clients reconnect for a later try.</summary>
        public const int JoinDeadlineSeconds = 10;

        /// <summary>
        /// In seconds. Well inside the join deadline and the client's own wait, so the player hears "busy" rather than
        /// nothing.
        /// </summary>
        public const int ClaimDeadlineSeconds = 5;

        private static readonly Regex NamePattern = new Regex("^[A-Za-z0-9_-]+$");

        /// <summary>
        /// A player's name is their account's username, unique ignoring case; capitals are kept for show.
        /// </summary>
        public static bool TryNormalizeName(string? name, out string normalized)
        {
            normalized = (name ?? "").Trim();
            return normalized.Length >= MinNameLength && normalized.Length <= MaxNameLength && NamePattern.IsMatch(normalized);
        }

        public static bool TryNormalizeChat(string? text, out string normalized) => ChatText.TryNormalize(text, MaxChatLength, out normalized);
    }
}
