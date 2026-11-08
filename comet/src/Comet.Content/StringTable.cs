using System.Collections.Generic;
using System.Text;
using MessagePack;

namespace Comet.Content
{
    /// <summary>
    /// Interface text by key, with <c>{name}</c> placeholders. A missing key gives <c>[key]</c> rather than throwing, so a typo shows on screen.
    /// A key's <c>_variant</c> version (<c>guest_note_web</c>) replaces its text when the game sets that variant.
    /// </summary>
    [MessagePackObject]
    public sealed class StringTable
    {
        [Key(0)]
        public Dictionary<string, string> Entries { get; set; } = new Dictionary<string, string>();

        public bool Contains(string key) => Entries.ContainsKey(key);

        private readonly Dictionary<string, string> _shared = new Dictionary<string, string>();

        public event System.Action? SharedChanged;

        private string? _variant;

        public void SetVariant(string? variant) => _variant = variant;

        /// <summary>Sets a value that fills <c>{name}</c> in every text, unless a message gives its own.</summary>
        public void SetShared(string name, string value)
        {
            if (_shared.TryGetValue(name, out var old) && old == value)
            {
                return;
            }

            _shared[name] = value;
            SharedChanged?.Invoke();
        }

        /// <summary>The text with no placeholders filled in, or <c>[key]</c>.</summary>
        public string Raw(string key) =>
            _variant != null && Entries.TryGetValue($"{key}_{_variant}", out var variant) ? variant
            : Entries.TryGetValue(key, out var text) ? text
            : $"[{key}]";

        /// <summary>The text with the shared values filled in, or <c>[key]</c>.</summary>
        public string Get(string key) => Format(key);

        /// <summary>
        /// The text with each <c>{name}</c> replaced from <paramref name="values"/>, else the shared value; one with neither is left as written.
        /// </summary>
        public string Format(string key, params (string Name, object Value)[] values)
        {
            var text = Raw(key);
            if (values.Length == 0 && _shared.Count == 0)
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
                var name = text.Substring(open + 1, close - open - 1);
                if (TryFind(values, name, out var value))
                {
                    result.Append(value?.ToString() ?? "");
                }
                else if (_shared.TryGetValue(name, out var shared))
                {
                    result.Append(shared);
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
