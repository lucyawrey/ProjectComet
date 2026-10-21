using System.Collections.Generic;

namespace Comet.Content
{
    /// <summary>Loaded entries of one content type, looked up by id or number. Unknown ids and numbers throw.</summary>
    public sealed class ContentTable<T>
        where T : class, IContentEntry
    {
        private readonly Dictionary<string, T> _byId = new Dictionary<string, T>();
        private readonly Dictionary<int, T> _byNumber = new Dictionary<int, T>();
        private readonly List<T> _all = new List<T>();

        /// <summary>A table of <paramref name="entries"/>; <paramref name="kind"/> names the type in errors (<c>shape</c>).</summary>
        public ContentTable(string kind, IEnumerable<T> entries)
        {
            Kind = kind;
            foreach (var entry in entries)
            {
                if (_byId.ContainsKey(entry.Id))
                {
                    throw new ContentException($"Duplicate {kind} id '{entry.Id}'.");
                }

                if (_byNumber.ContainsKey(entry.Number))
                {
                    throw new ContentException($"Duplicate {kind} number {entry.Number} ('{entry.Id}').");
                }

                _byId.Add(entry.Id, entry);
                _byNumber.Add(entry.Number, entry);
                _all.Add(entry);
            }
        }

        /// <summary>The content type's registry name (<c>shape</c>).</summary>
        public string Kind { get; }

        /// <summary>Every entry, in number order.</summary>
        public IReadOnlyList<T> All => _all;

        public T this[string id] => _byId.TryGetValue(id, out var entry)
            ? entry
            : throw new ContentException($"Unknown {Kind} id '{id}'.");

        public T this[int number] => _byNumber.TryGetValue(number, out var entry)
            ? entry
            : throw new ContentException($"Unknown {Kind} number {number}.");

        /// <summary>The entry with <paramref name="id"/>, if there is one.</summary>
        public bool TryGet(string id, out T entry) => _byId.TryGetValue(id, out entry!);

        /// <summary>The entry with <paramref name="number"/>, if there is one.</summary>
        public bool TryGet(int number, out T entry) => _byNumber.TryGetValue(number, out entry!);
    }
}
