using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Paramatrix.Unity.Editor.Tests
{
    /// <summary>
    /// The character sprites in the game have the heights tools/art/characters.json gives them: a character's own
    /// height when it has one, the common default otherwise. Catches a sprite that was not re-made after the list changed.
    /// </summary>
    public sealed class CharacterSizesTests
    {
        [Serializable]
        private sealed class Canvas
        {
            public int width;
            public int height;
            public int x;
            public int y;
        }

        [Serializable]
        private sealed class Character
        {
            public string id;
            public string source;
            public string @out;
            public int height; // 0: not given, the default applies
            public Canvas canvas;
        }

        [Serializable]
        private sealed class List
        {
            public int default_height;
            public Character[] characters;
        }

        private static string RepositoryRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

        [Test]
        public void SpritesHaveTheHeightsOfTheList()
        {
            var list = JsonUtility.FromJson<List>(File.ReadAllText(Path.Combine(RepositoryRoot, "tools", "art", "characters.json")));
            Assert.That(list.default_height, Is.GreaterThan(0), "default_height");
            Assert.That(list.characters, Is.Not.Empty);

            foreach (var character in list.characters)
            {
                var expected = character.height > 0 ? character.height : list.default_height;
                var texture = new Texture2D(2, 2);
                try
                {
                    Assert.That(texture.LoadImage(File.ReadAllBytes(Path.Combine(RepositoryRoot, character.@out))), Is.True, character.id + ": " + character.@out);
                    Assert.That(OpaqueHeight(texture), Is.EqualTo(expected), character.id + " figure height in grid pixels");
                    if (character.canvas != null && character.canvas.width > 0)
                    {
                        Assert.That(new Vector2Int(texture.width, texture.height), Is.EqualTo(new Vector2Int(character.canvas.width, character.canvas.height)), character.id + " canvas");
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }
        }

        private static int OpaqueHeight(Texture2D texture)
        {
            var pixels = texture.GetPixels32();
            int top = -1, bottom = -1;
            for (var y = 0; y < texture.height; y++)
            {
                for (var x = 0; x < texture.width; x++)
                {
                    if (pixels[(y * texture.width) + x].a > 0)
                    {
                        bottom = bottom < 0 ? y : bottom;
                        top = y;
                        break;
                    }
                }
            }

            return top < 0 ? 0 : top - bottom + 1;
        }
    }
}
