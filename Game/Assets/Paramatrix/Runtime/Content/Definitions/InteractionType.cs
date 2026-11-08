using System;

namespace Paramatrix.Content
{
    /// <summary>Player interaction types (Architecture 0.3 §5.2).</summary>
    public enum InteractionType
    {
        Inspect = 1,
        Primary,
        Take,
        Talk,
        Use,
        ApplyItem,
        CombineItems,
        Exit,
    }

    public static class InteractionTypes
    {
        private static readonly (string Name, InteractionType Type)[] Table =
        {
            ("inspect", InteractionType.Inspect),
            ("primary", InteractionType.Primary),
            ("take", InteractionType.Take),
            ("talk", InteractionType.Talk),
            ("use", InteractionType.Use),
            ("apply_item", InteractionType.ApplyItem),
            ("combine_items", InteractionType.CombineItems),
            ("exit", InteractionType.Exit),
        };

        public static readonly InteractionType[] All =
        {
            InteractionType.Inspect,
            InteractionType.Primary,
            InteractionType.Take,
            InteractionType.Talk,
            InteractionType.Use,
            InteractionType.ApplyItem,
            InteractionType.CombineItems,
            InteractionType.Exit,
        };

        public static bool TryParse(string name, out InteractionType type)
        {
            foreach (var entry in Table)
            {
                if (string.Equals(entry.Name, name, StringComparison.Ordinal))
                {
                    type = entry.Type;
                    return true;
                }
            }

            type = default;
            return false;
        }

        public static string Name(InteractionType type)
        {
            foreach (var entry in Table)
            {
                if (entry.Type == type)
                {
                    return entry.Name;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown interaction type.");
        }

        /// <summary>
        /// Types a left click on a character or object may mean (T12). Inspect is always the right click;
        /// apply_item / combine_items need a selected item and are never a plain click.
        /// </summary>
        public static bool IsAllowedAsPrimary(InteractionType type)
        {
            switch (type)
            {
                case InteractionType.Primary:
                case InteractionType.Take:
                case InteractionType.Talk:
                case InteractionType.Use:
                case InteractionType.Exit:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Interaction types whose context carries a selected item.</summary>
        public static bool RequiresItem(InteractionType type) =>
            type == InteractionType.ApplyItem || type == InteractionType.CombineItems;
    }
}
