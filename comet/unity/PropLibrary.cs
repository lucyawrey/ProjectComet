using System;
using System.Collections.Generic;
using UnityEngine;

namespace Comet.Unity
{
    /// <summary>
    /// The model for each prop id (<c>prop.rock</c>), so the client and the zone editor can place what zones list. A
    /// game fills it in from its content's prop entries (in ShapeLand, the editor's ContentSync), never by hand.
    /// </summary>
    [CreateAssetMenu(menuName = "Comet/Prop Library")]
    public sealed class PropLibrary : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string id;

            [Tooltip("The name the zone editor shows; empty shows the id without its prefix.")]
            public string displayName;

            public GameObject model;

            [Tooltip("Placements may only scale it the same on every axis.")]
            public bool uniformScale;

            [Tooltip("The box around its model, in its own space: the zone editor checks how far placements reach with it.")]
            public Bounds bounds;

            [Tooltip("Whether its placements are drawn in its own color, not the placed objects' material's.")]
            public bool hasColor;

            public Color color;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        private Dictionary<string, Entry>? _byId;

        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>The model for <paramref name="id"/>, or null if the library has none.</summary>
        public GameObject? Model(string id) => TryGet(id, out var entry) ? entry.model : null;

        /// <summary>The entry for <paramref name="id"/>, if the library has one.</summary>
        public bool TryGet(string id, out Entry entry)
        {
            if (_byId == null)
            {
                _byId = new Dictionary<string, Entry>();
                foreach (var e in entries)
                {
                    _byId[e.id] = e;
                }
            }

            return _byId.TryGetValue(id, out entry);
        }

        /// <summary>Replaces the entries (the editor's content sync does this).</summary>
        public void SetEntries(IEnumerable<Entry> newEntries)
        {
            entries = new List<Entry>(newEntries);
            _byId = null;
        }
    }
}
