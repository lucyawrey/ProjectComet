using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ShapeLand.Shared.World
{
    /// <summary>ShapeLand's game rules that clients and the server both need.</summary>
    public static class ShapeLandRules
    {
        /// <summary>Server ticks per second.</summary>
        public const int TickRate = 30;
        /// <summary>The shortest name.</summary>
        public const int MinNameLength = 3;

        /// <summary>The longest name.</summary>
        public const int MaxNameLength = 16;
        /// <summary>The longest chat line, in UTF-16 characters after normalizing.</summary>
        public const int MaxChatLength = 200;

        /// <summary>Seconds a connection has to join before the server drops it; clients reconnect for a later try.</summary>
        public const int JoinDeadlineSeconds = 10;

        /// <summary>
        /// Seconds the database has to hand over a joining character before the join fails as busy: well inside the
        /// join deadline and the client's own wait, so the player hears "busy" rather than nothing.
        /// </summary>
        public const int ClaimDeadlineSeconds = 5;

        private static readonly Regex NamePattern = new Regex("^[A-Za-z0-9_-]+$");

        /// <summary>
        /// Trims a name and checks it: <see cref="MinNameLength"/> to <see cref="MaxNameLength"/> ASCII letters, digits,
        /// '-' and '_'. A player's name is their account's username, unique ignoring case; capitals are kept for show.
        /// </summary>
        public static bool TryNormalizeName(string? name, out string normalized)
        {
            normalized = (name ?? "").Trim();
            return normalized.Length >= MinNameLength && normalized.Length <= MaxNameLength && NamePattern.IsMatch(normalized);
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
        public static bool TryNormalizeChat(string? text, out string normalized)
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

            normalized = kept.ToString().Trim();
            return normalized.Length > 0 && normalized.Length <= MaxChatLength;
        }
    }
}
