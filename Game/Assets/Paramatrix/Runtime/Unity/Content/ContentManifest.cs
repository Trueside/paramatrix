using System;
using System.Collections.Generic;
using Paramatrix.Content;
using UnityEngine;

namespace Paramatrix.Unity
{
    /// <summary>
    /// The list of content files the game loads (A14). Filled by the editor ("Paramatrix → Collect Content")
    /// from Assets/Paramatrix/Content; the game never searches folders at runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "Paramatrix/Content Manifest", fileName = "ContentManifest")]
    public sealed class ContentManifest : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("Asset path, used as the source name in diagnostics.")]
            public string path;

            public TextAsset asset;
        }

        [SerializeField]
        private List<Entry> entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>Content sources for <see cref="ContentLoader"/>. A missing asset is a configuration error.</summary>
        public List<ContentSource> ToSources()
        {
            var sources = new List<ContentSource>();
            foreach (var entry in entries)
            {
                if (entry.asset == null)
                {
                    throw new InvalidOperationException($"Content manifest entry '{entry.path}' has no asset; run Paramatrix → Collect Content.");
                }

                sources.Add(new ContentSource(entry.path, entry.asset.text));
            }

            return sources;
        }

        /// <summary>Editor only: replaces the list.</summary>
        public void SetEntries(IEnumerable<Entry> newEntries)
        {
            entries = new List<Entry>(newEntries);
        }
    }
}
