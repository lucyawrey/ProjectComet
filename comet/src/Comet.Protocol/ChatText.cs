using System.Globalization;
using System.Text;

namespace Comet.Protocol
{
    public static class ChatText
    {
        /// <summary>Combining marks kept on one character (enough for real scripts and keycap emoji); more stack out of the bubble.</summary>
        public const int MaxMarksPerCharacter = 3;

        /// <summary>
        /// Cleans a chat line: drops characters that change how text is laid out or read rather than being text
        /// (control and formatting characters, such as newlines, zero-width characters, bidirectional overrides and
        /// tag characters; line and paragraph separators; private-use characters and broken surrogate pairs), and
        /// combining marks past <see cref="MaxMarksPerCharacter"/> on one character, then trims it and checks it's
        /// not empty and at most <paramref name="maxLength"/> UTF-16 characters. Clients also show chat as plain
        /// text, never markup.
        /// </summary>
        public static bool TryNormalize(string? text, int maxLength, out string normalized)
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
            return normalized.Length > 0 && normalized.Length <= maxLength;
        }
    }
}
