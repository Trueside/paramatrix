using System;

namespace Paramatrix.Content
{
    /// <summary>
    /// Closed set of definition kinds. The kind of a definition is the prefix of its ID
    /// ("item.key" is an Item); there is no separate "type" field in content.
    /// </summary>
    public enum EntityKind
    {
        Game = 1,
        Character,
        Item,
        Object,
        Location,
        Story,
        Condition,
        Event,
        Dialogue,
        Response,

        /// <summary>An avatar emotion of the common dictionary (Р10): file name = the ID name, a Russian name for authors.</summary>
        Emotion,
    }

    public static class EntityKinds
    {
        private static readonly (string Prefix, EntityKind Kind)[] Table =
        {
            ("game", EntityKind.Game),
            ("character", EntityKind.Character),
            ("item", EntityKind.Item),
            ("object", EntityKind.Object),
            ("location", EntityKind.Location),
            ("story", EntityKind.Story),
            ("condition", EntityKind.Condition),
            ("event", EntityKind.Event),
            ("dialogue", EntityKind.Dialogue),
            ("response", EntityKind.Response),
            ("emotion", EntityKind.Emotion),
        };

        public static bool TryFromPrefix(string prefix, out EntityKind kind)
        {
            foreach (var entry in Table)
            {
                if (string.Equals(entry.Prefix, prefix, StringComparison.Ordinal))
                {
                    kind = entry.Kind;
                    return true;
                }
            }

            kind = default;
            return false;
        }

        public static string Prefix(EntityKind kind)
        {
            foreach (var entry in Table)
            {
                if (entry.Kind == kind)
                {
                    return entry.Prefix;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown entity kind.");
        }

        /// <summary>Kinds whose definitions may declare State properties (Architecture 0.3.1 §3).</summary>
        public static bool CanOwnState(EntityKind kind)
        {
            switch (kind)
            {
                case EntityKind.Character:
                case EntityKind.Item:
                case EntityKind.Object:
                case EntityKind.Location:
                case EntityKind.Story:
                    return true;
                default:
                    return false;
            }
        }
    }
}
