using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ShapeLand.Shared.World
{
    /// <summary>ShapeLand's game rules that clients and the server both need.</summary>
    public static class ShapeLandRules
    {
        public const int TickRate = 30;
        public const int MaxNameLength = 16;
        public const int MaxChatLength = 200;

        /// <summary>Seconds a connection has to join before the server drops it; clients reconnect for a later try.</summary>
        public const int JoinDeadlineSeconds = 10;

        /// <summary>
        /// The body colours every player can pick, as 0xRRGGBB. Placeholders: the project lead decides the final sets,
        /// and more colours come from items later.
        /// </summary>
        public static readonly uint[] BodyColours =
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

        /// <summary>The eye colours every player can pick, as 0xRRGGBB. Placeholders, like <see cref="BodyColours"/>.</summary>
        public static readonly uint[] EyeColours =
        {
            0x16161C, // ink
            0xFFFFFF, // white
            0x5D4037, // brown
            0x1565C0, // deep blue
            0x2E7D32, // deep green
        };

        private static readonly Regex NamePattern = new Regex("^[A-Za-z0-9_-]+( [A-Za-z0-9_-]+)*$");

        /// <summary>Trims a name and checks it: letters, digits, '-', '_' and single spaces, at most <see cref="MaxNameLength"/> characters.</summary>
        public static bool TryNormaliseName(string? name, out string normalised)
        {
            normalised = (name ?? "").Trim();
            return normalised.Length > 0 && normalised.Length <= MaxNameLength && NamePattern.IsMatch(normalised);
        }

        /// <summary>Combining marks kept on one character (enough for real scripts and keycap emoji); more stack out of the bubble.</summary>
        public const int MaxMarksPerCharacter = 3;

        /// <summary>
        /// Cleans a chat line and checks its length: drops characters that change how text is laid out or read
        /// rather than being text (control and formatting characters, such as newlines, zero-width characters,
        /// bidirectional overrides and tag characters; line and paragraph separators; private-use characters and
        /// broken surrogate pairs), and combining marks past <see cref="MaxMarksPerCharacter"/> on one character,
        /// then trims it. Clients also show chat as plain text, never markup.
        /// </summary>
        public static bool TryNormaliseChat(string? text, out string normalised)
        {
            text = text ?? "";
            var kept = new StringBuilder(text.Length);
            var marks = 0;
            for (var i = 0; i < text.Length; i++)
            {
                // Characters outside the Basic Multilingual Plane are a surrogate pair, checked as one.
                var length = char.IsSurrogatePair(text, i) ? 2 : 1;
                switch (char.GetUnicodeCategory(text, i))
                {
                    case UnicodeCategory.Control:
                    case UnicodeCategory.Format:
                    case UnicodeCategory.LineSeparator:
                    case UnicodeCategory.ParagraphSeparator:
                    case UnicodeCategory.PrivateUse:
                    case UnicodeCategory.Surrogate:
                        break;
                    case UnicodeCategory.NonSpacingMark:
                    case UnicodeCategory.SpacingCombiningMark:
                    case UnicodeCategory.EnclosingMark:
                        if (++marks <= MaxMarksPerCharacter)
                        {
                            kept.Append(text, i, length);
                        }
                        break;
                    default:
                        marks = 0;
                        kept.Append(text, i, length);
                        break;
                }

                i += length - 1;
            }

            normalised = kept.ToString().Trim();
            return normalised.Length > 0 && normalised.Length <= MaxChatLength;
        }
    }
}
