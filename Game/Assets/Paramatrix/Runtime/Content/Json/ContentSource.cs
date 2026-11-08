using System;

namespace Paramatrix.Content
{
    /// <summary>One content file: a name for diagnostics (usually the asset path) and its JSON text.</summary>
    public readonly struct ContentSource
    {
        public ContentSource(string name, string text)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("Content source name must not be empty.", nameof(name));
            }

            Name = name;
            Text = text ?? throw new ArgumentNullException(nameof(text));
        }

        public string Name { get; }

        public string Text { get; }
    }
}
