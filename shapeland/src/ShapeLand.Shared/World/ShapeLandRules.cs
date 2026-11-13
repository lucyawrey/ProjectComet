using System.Text.RegularExpressions;
using Comet.Protocol;

namespace ShapeLand.Shared.World
{
    public static class ShapeLandRules
    {
        public const int TickRate = 30;

        public const int MinNameLength = 3;

        public const int MaxNameLength = 16;

        public const int MaxChatLength = 200;

        public const int JoinDeadlineSeconds = 10;

        public const int ClaimDeadlineSeconds = 5;

        private static readonly Regex NamePattern = new Regex("^[A-Za-z0-9_-]+$");

        public static bool TryNormalizeName(string? name, out string normalized)
        {
            normalized = (name ?? "").Trim();
            return normalized.Length >= MinNameLength && normalized.Length <= MaxNameLength && NamePattern.IsMatch(normalized);
        }

        public static bool TryNormalizeChat(string? text, out string normalized) => ChatText.TryNormalize(text, MaxChatLength, out normalized);
    }
}
