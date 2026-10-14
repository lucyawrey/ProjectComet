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

        private Dictionary<string, Entry>? _byKey;

        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>The model for <paramref name="key"/>, or null if the library has none.</summary>
        public GameObject? Model(string key) => TryGet(key, out var entry) ? entry.model : null;

        /// <summary>The entry for <paramref name="key"/>, if the library has one.</summary>
        public bool TryGet(string key, out Entry entry)
        {
            if (_byKey == null)
            {
                _byKey = new Dictionary<string, Entry>();
                foreach (var e in entries)
                {
                    _byKey[e.key] = e;
                }
            }

            return _byKey.TryGetValue(key, out entry);
        }

        /// <summary>Replaces the entries (the editor's content sync does this).</summary>
        public void SetEntries(IEnumerable<Entry> newEntries)
        {
            entries = new List<Entry>(newEntries);
            _byKey = null;
        }
    }
}
