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

            var result = new StringBuilder(text);
            foreach (var (name, value) in values)
            {
                result.Replace("{" + name + "}", value?.ToString() ?? "");
            }

            return result.ToString();
        }
    }
}
