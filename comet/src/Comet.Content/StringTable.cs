using System.Collections.Generic;
using System.Text;
using MessagePack;

namespace Comet.Content
{
    /// <summary>
    /// Interface text in one language, by key (<c>join.name_taken</c>), compiled from a game's string file
    /// (<c>strings/en.toml</c>). Text may hold named placeholders, written <c>{username}</c>: shared ones the game sets
    /// once (<see cref="SetShared"/>) fill in everywhere, and <see cref="Format"/> fills in the rest for one message.
    /// A key can have a version for one platform, written with its suffix (<c>guest_note_web</c>), used in place of
    /// the key's own text when the game sets that variant (<see cref="SetVariant"/>).
    /// A missing key gives the key in brackets rather than throwing, so a typo shows on screen instead of
    /// breaking it; the game's tests check every key it uses.
    /// </summary>
    [MessagePackObject]
    public sealed class StringTable
    {
        /// <summary>The text by key.</summary>
        [Key(0)]
        public Dictionary<string, string> Entries { get; set; } = new Dictionary<string, string>();

        /// <summary>True if there's text for <paramref name="key"/>.</summary>
        public bool Contains(string key) => Entries.ContainsKey(key);

        // Values every text fills in, such as the player's username; set by the game, not compiled.
        private readonly Dictionary<string, string> _shared = new Dictionary<string, string>();

        /// <summary>Raised when a shared value changes, so text already on screen can be filled in again.</summary>
        public event System.Action? SharedChanged;

        // The platform suffix whose versions replace their keys' text ("web"), or null.
        private string? _variant;

        /// <summary>Uses each key's <c>key_{variant}</c> version, where there is one, in place of its own text.</summary>
        public void SetVariant(string? variant) => _variant = variant;

        /// <summary>Sets a value every text's <c>{name}</c> fills in, unless a message gives its own.</summary>
        public void SetShared(string name, string value)
        {
            if (_shared.TryGetValue(name, out var old) && old == value)
            {
                return;
            }

            _shared[name] = value;
            SharedChanged?.Invoke();
        }

        /// <summary>The text for <paramref name="key"/> (its variant's version, if any) with no placeholders filled in, or <c>[key]</c>.</summary>
        public string Raw(string key) =>
            _variant != null && Entries.TryGetValue($"{key}_{_variant}", out var variant) ? variant
            : Entries.TryGetValue(key, out var text) ? text
            : $"[{key}]";

        /// <summary>The text for <paramref name="key"/> with the shared values filled in, or <c>[key]</c> if there's none.</summary>
        public string Get(string key) => Format(key);

        /// <summary>
        /// The text for <paramref name="key"/> with each <c>{name}</c> replaced by its value from <paramref name="values"/>,
        /// else the shared one; a placeholder with neither is left as written.
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
