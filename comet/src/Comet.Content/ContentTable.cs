using System.Collections.Generic;

namespace Comet.Content
{
    /// <summary>Loaded entries of one content type, looked up by key or number. Unknown keys and numbers throw.</summary>
    public sealed class ContentTable<T>
        where T : class, IContentEntry
    {
        private readonly Dictionary<string, T> _byKey = new Dictionary<string, T>();
        private readonly Dictionary<int, T> _byNumber = new Dictionary<int, T>();
        private readonly List<T> _all = new List<T>();

        public ContentTable(string kind, IEnumerable<T> entries)
        {
            Kind = kind;
            foreach (var entry in entries)
            {
                if (_byKey.ContainsKey(entry.Id))
                {
                    throw new ContentException($"Duplicate {kind} key '{entry.Id}'.");
                }

                if (_byNumber.ContainsKey(entry.Number))
                {
                    throw new ContentException($"Duplicate {kind} number {entry.Number} ('{entry.Id}').");
                }

                _byKey.Add(entry.Id, entry);
                _byNumber.Add(entry.Number, entry);
                _all.Add(entry);
            }
        }

        /// <summary>The content type's registry name (<c>shape</c>).</summary>
        public string Kind { get; }

        /// <summary>Every entry, in number order.</summary>
        public IReadOnlyList<T> All => _all;

        public T this[string key] => _byKey.TryGetValue(key, out var entry)
            ? entry
            : throw new ContentException($"Unknown {Kind} key '{key}'.");

        public T this[int number] => _byNumber.TryGetValue(number, out var entry)
            ? entry
            : throw new ContentException($"Unknown {Kind} number {number}.");

        public bool TryGet(string key, out T entry) => _byKey.TryGetValue(key, out entry!);

        public bool TryGet(int number, out T entry) => _byNumber.TryGetValue(number, out entry!);
    }
}
