using System.Collections.Generic;
using System.Text;
using MessagePack;

namespace Comet.Content
{
    /// <summary>
    /// Interface text in one language, by key (<c>join.name_taken</c>), compiled from a game's string file
    /// (<c>strings/en.toml</c>). Text may hold named placeholders, written <c>{name}</c>, filled in by <see cref="Format"/>.
    /// A missing key gives the key in brackets rather than throwing, so a typo shows on screen instead of
    /// breaking it; the game's tests check every key it uses.
    /// </summary>
    [MessagePackObject]
    public sealed class StringTable
    {
        /// <summary>The text by key.</summary>
        [Key(0)] public Dictionary<string, string> Entries { get; set; } = new Dictionary<string, string>();

        /// <summary>True if there's text for <paramref name="key"/>.</summary>
        public bool Contains(string key) => Entries.ContainsKey(key);

        /// <summary>The text for <paramref name="key"/>, or <c>[key]</c> if there's none.</summary>
        public string Get(string key) => Entries.TryGetValue(key, out var text) ? text : $"[{key}]";

        /// <summary>The text for <paramref name="key"/> with each <c>{name}</c> replaced by its value.</summary>
        public string Format(string key, params (string Name, object Value)[] values)
        {
            var text = Get(key);
            if (values.Length == 0)
            {
                return text;
            }

            // One pass over the text, so a value that itself holds a {name} is left as it is.
            var result = new StringBuilder(text.Length);
            var at = 0;
            while (at < text.Length)
            {
                var open = text.IndexOf('{', at);
                var close = open < 0 ? -1 : text.IndexOf('}', open + 1);
                if (close < 0)
                {
                    break;
                }

                result.Append(text, at, open - at);
                if (TryFind(values, text.Substring(open + 1, close - open - 1), out var value))
                {
                    result.Append(value?.ToString() ?? "");
                }
                else
                {
                    result.Append(text, open, close - open + 1);
                }

                at = close + 1;
            }

            return result.Append(text, at, text.Length - at).ToString();
        }

        private static bool TryFind((string Name, object Value)[] values, string name, out object value)
        {
            foreach (var pair in values)
            {
                if (pair.Name == name)
                {
                    value = pair.Value;
                    return true;
                }
            }

            value = null!;
            return false;
        }
    }
}
