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

        /// <summary>
        /// One picture of a character by emotion: an avatar (Р10, Art/Characters/&lt;name&gt;/emotions/&lt;emotion&gt;.png) or a
        /// novel picture (Р12, Art/Characters/&lt;name&gt;/novel/&lt;emotion&gt;.png) of character.&lt;name&gt;.
        /// </summary>
        [Serializable]
        public struct Avatar
        {
            public string characterId;

            public string emotion;

            public Sprite sprite;
        }

        [SerializeField]
        private List<Entry> entries = new List<Entry>();

        [SerializeField]
        private List<Avatar> avatars = new List<Avatar>();

        [SerializeField]
        private List<Avatar> novelPictures = new List<Avatar>();

        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>Sorted by character, then emotion.</summary>
        public IReadOnlyList<Avatar> Avatars => avatars;

        /// <summary>The avatar of a character with an emotion; null when there is none.</summary>
        public Sprite FindAvatar(string characterId, string emotion)
        {
            foreach (var avatar in avatars)
            {
                if (avatar.characterId == characterId && avatar.emotion == emotion)
                {
                    return avatar.sprite;
                }
            }

            return null;
        }

        /// <summary>Editor only: replaces the avatar list.</summary>
        public void SetAvatars(IEnumerable<Avatar> newAvatars)
        {
            avatars = new List<Avatar>(newAvatars);
        }

        /// <summary>Novel pictures (Р12), sorted by character, then emotion.</summary>
        public IReadOnlyList<Avatar> NovelPictures => novelPictures;

        /// <summary>The novel picture of a character with an emotion; null when there is none.</summary>
        public Sprite FindNovelPicture(string characterId, string emotion)
        {
            foreach (var picture in novelPictures)
            {
                if (picture.characterId == characterId && picture.emotion == emotion)
                {
                    return picture.sprite;
                }
            }

            return null;
        }

        /// <summary>Editor only: replaces the novel picture list.</summary>
        public void SetNovelPictures(IEnumerable<Avatar> pictures)
        {
            novelPictures = new List<Avatar>(pictures);
        }

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
