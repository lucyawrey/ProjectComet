using System.Text.RegularExpressions;

namespace ShapeLand.Shared.World
{
    /// <summary>ShapeLand's game rules that clients and the server both need.</summary>
    public static class ShapeLandRules
    {
        public const int TickRate = 30;
        public const int MaxNameLength = 16;
        public const int MaxChatLength = 200;

        /// <summary>The bright toy-box colours players are given at random, as 0xRRGGBB.</summary>
        public static readonly uint[] Colours =
        {
            0xE53935, // red
            0x1E88E5, // blue
            0xFDD835, // yellow
            0x43A047, // green
            0xFB8C00, // orange
            0x8E24AA, // purple
            0x00ACC1, // cyan
            0xEC407A, // pink
        };

        private static readonly Regex NamePattern = new Regex("^[A-Za-z0-9_-]+( [A-Za-z0-9_-]+)*$");

        /// <summary>Trims a name and checks it: letters, digits, '-', '_' and single spaces, at most <see cref="MaxNameLength"/> characters.</summary>
        public static bool TryNormaliseName(string? name, out string normalised)
        {
            normalised = (name ?? "").Trim();
            return normalised.Length > 0 && normalised.Length <= MaxNameLength && NamePattern.IsMatch(normalised);
        }

        /// <summary>Trims a chat line and checks its length.</summary>
        public static bool TryNormaliseChat(string? text, out string normalised)
        {
            normalised = (text ?? "").Trim();
            return normalised.Length > 0 && normalised.Length <= MaxChatLength;
        }
    }
}
