using System.Collections.Generic;
using System.Text;
using MessagePack;

namespace Comet.Content
{
    /// <summary>A missing key gives <c>[key]</c>, not an exception; <c>key_variant</c> replaces <c>key</c> when the game sets that variant.</summary>
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

        public void SetShared(string name, string value)
        {
            if (_shared.TryGetValue(name, out var old) && old == value)
            {
                return;
            }

            _shared[name] = value;
            SharedChanged?.Invoke();
        }

        public string Raw(string key) =>
            _variant != null && Entries.TryGetValue($"{key}_{_variant}", out var variant) ? variant
            : Entries.TryGetValue(key, out var text) ? text
            : $"[{key}]";

        public string Get(string key) => Format(key);

        public string Format(string key, params (string Name, object Value)[] values)
        {
            var text = Raw(key);
            if (values.Length == 0 && _shared.Count == 0)
            {
                return text;
            }

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
