using System.Collections.Generic;

namespace Paramatrix.Content
{
    /// <summary>
    /// Mutable intermediate result of parsing one definition. Drafts hold what can be checked locally;
    /// <see cref="ContentLinker"/> checks references across all files and builds immutable definitions.
    /// </summary>
    internal abstract class Draft
    {
        protected Draft(EntityId id, SourceLocation source)
        {
            Id = id;
            Source = source;
        }

        public EntityId Id { get; }

        public SourceLocation Source { get; }
    }

    internal sealed class GameSettingsDraft : Draft
    {
        public GameSettingsDraft(EntityId id, SourceLocation source)
            : base(id, source)
        {
        }

        public EntityId PlayerCharacter { get; set; }

        public SourceLocation PlayerCharacterSource { get; set; }
    }

    internal sealed class WorldDraft : Draft
    {
        public WorldDraft(EntityId id, SourceLocation source, int declarationOrder)
            : base(id, source)
        {
            DeclarationOrder = declarationOrder;
        }

        public int DeclarationOrder { get; }

        /// <summary>Valid State declarations.</summary>
        public List<StateDraft> States { get; } = new List<StateDraft>();

        /// <summary>Every declared property name, including declarations that failed validation.</summary>
        public HashSet<string> DeclaredProperties { get; } = new HashSet<string>(System.StringComparer.Ordinal);

        public string Name { get; set; }

        public string Description { get; set; }

        public InteractionType PrimaryInteraction { get; set; }

        public string Scene { get; set; }

        /// <summary>Character: valid declared avatar emotions (Р10); empty when none are declared.</summary>
        public List<string> Emotions { get; } = new List<string>();

        public List<SourceLocation> EmotionSources { get; } = new List<SourceLocation>();

        /// <summary>Character: "none" or a location ID. Item: "inventory", "consumed", a location or a character ID.</summary>
        public string InitialPlace { get; set; }

        public SourceLocation InitialPlaceSource { get; set; }
    }

    /// <summary>A dictionary emotion (Р10).</summary>
    internal sealed class EmotionDraft : Draft
    {
        public EmotionDraft(EntityId id, SourceLocation source)
            : base(id, source)
        {
        }

        public string Name { get; set; }
    }

    internal sealed class StateDraft
    {
        public StateDraft(StateAddress address, SourceLocation source)
        {
            Address = address;
            Source = source;
        }

        public StateAddress Address { get; }

        public SourceLocation Source { get; }

        public StateType Type { get; set; }

        public StateValue Default { get; set; }

        public Persistence Persistence { get; set; }

        public MutationPolicy MutationPolicy { get; set; }

        public int? Min { get; set; }

        public int? Max { get; set; }

        public List<string> EnumValues { get; } = new List<string>();

        public List<SourceLocation> EnumValueSources { get; } = new List<SourceLocation>();
    }
}
