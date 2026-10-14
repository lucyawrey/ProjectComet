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
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        private Dictionary<string, GameObject> _byKey;

        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>The model for <paramref name="key"/>, or null if the library has none.</summary>
        public GameObject Model(string key)
        {
            if (_byKey == null)
            {
                _byKey = new Dictionary<string, GameObject>();
                foreach (var entry in entries)
                {
                    _byKey[entry.key] = entry.model;
                }
            }

            return _byKey.TryGetValue(key, out var model) ? model : null;
        }

        /// <summary>Replaces the entries (the editor's content sync does this).</summary>
        public void SetEntries(IEnumerable<Entry> newEntries)
        {
            entries = new List<Entry>(newEntries);
            _byKey = null;
        }
    }
}
