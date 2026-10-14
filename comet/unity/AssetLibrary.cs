using System;
using System.Collections.Generic;
using UnityEngine;

namespace Comet.Unity
{
    /// <summary>
    /// The model for each asset key (<c>asset.cube</c>), so the client and the zone editor can place what zones list. A
    /// game fills it in from its content's asset entries (in ShapeLand, the editor's ContentSync), never by hand.
    /// </summary>
    [CreateAssetMenu(menuName = "Comet/Asset Library")]
    public sealed class AssetLibrary : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string key;
            public GameObject model;

            [Tooltip("Placements may only scale it the same on every axis.")]
            public bool uniformScale;

            [Tooltip("The box around its model, in its own space: the zone editor checks how far placements reach with it.")]
            public Bounds bounds;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        // Positions in entries, not copies of them: a copy's model reference goes stale when Unity unloads and
        // reloads the model (as when play mode changes scenes), while the serialized list is kept up to date.
        private Dictionary<string, int> _byKey;

        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>The model for <paramref name="key"/>, or null if the library has none.</summary>
        public GameObject Model(string key) => TryGet(key, out var entry) ? entry.model : null;

        /// <summary>The entry for <paramref name="key"/>, if the library has one.</summary>
        public bool TryGet(string key, out Entry entry)
        {
            if (_byKey == null)
            {
                _byKey = new Dictionary<string, int>();
                for (var i = 0; i < entries.Count; i++)
                {
                    _byKey[entries[i].key] = i;
                }
            }

            if (_byKey.TryGetValue(key, out var index) && index < entries.Count)
            {
                entry = entries[index];
                return true;
            }

            entry = default;
            return false;
        }

        /// <summary>Replaces the entries (the editor's content sync does this).</summary>
        public void SetEntries(IEnumerable<Entry> newEntries)
        {
            entries = new List<Entry>(newEntries);
            _byKey = null;
        }
    }
}
