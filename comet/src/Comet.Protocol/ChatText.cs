using System.Globalization;
using System.Text;

namespace Comet.Protocol
{
    public static class ChatText
    {
        public const int MaxMarksPerCharacter = 3;

        /// <summary>Drops unsafe characters and extra marks, then trims; false if empty or over <paramref name="maxLength"/> UTF-16 units.</summary>
        public static bool TryNormalize(string? text, int maxLength, out string normalized)
        {
            text = text ?? "";
            var kept = new StringBuilder(text.Length);
            var marks = 0;
            for (var i = 0; i < text.Length; i++)
            {
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
