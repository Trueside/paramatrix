using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Paramatrix.Content
{
    /// <summary>Base of every immutable content definition.</summary>
    public abstract class Definition
    {
        protected Definition(EntityId id, SourceLocation source)
        {
            if (!id.IsValid)
            {
                throw new ArgumentException("ID is not initialized.", nameof(id));
            }

            Id = id;
            Source = source;
        }

        public EntityId Id { get; }

        public SourceLocation Source { get; }

        public override string ToString() => Id.ToString();
    }

    /// <summary>A definition that owns State properties: character, item, object, location, story.</summary>
    public abstract class WorldEntityDefinition : Definition
    {
        protected WorldEntityDefinition(EntityId id, SourceLocation source, IEnumerable<StateDefinition> states)
            : base(id, source)
        {
            if (!EntityKinds.CanOwnState(id.Kind))
            {
                throw new ArgumentException($"{id} cannot own State.", nameof(id));
            }

            var list = new List<StateDefinition>(states ?? Array.Empty<StateDefinition>());
            list.Sort((a, b) => a.Address.CompareTo(b.Address));

            var map = new Dictionary<string, StateDefinition>(StringComparer.Ordinal);
            foreach (var state in list)
            {
                if (state.Address.Entity != id)
                {
                    throw new ArgumentException($"State {state.Address} does not belong to {id}.", nameof(states));
                }

                map.Add(state.Address.Property, state);
            }

            States = new ReadOnlyCollection<StateDefinition>(list);
            StatesByProperty = new ReadOnlyDictionary<string, StateDefinition>(map);
        }

        /// <summary>All State properties, built-in included, sorted by property name.</summary>
        public IReadOnlyList<StateDefinition> States { get; }

        public IReadOnlyDictionary<string, StateDefinition> StatesByProperty { get; }
    }

    /// <summary>Avatar emotions (Р10): names are file names of the avatar pictures.</summary>
    public static class Emotions
    {
        /// <summary>Every character with avatars has it; a dialogue line without an emotion uses it.</summary>
        public const string Default = "default";

        /// <summary>Lowercase Latin letters, digits and '_', starting with a letter: safe as a file name everywhere.</summary>
        public static bool IsValidName(string name)
        {
            if (string.IsNullOrEmpty(name) || name[0] < 'a' || name[0] > 'z')
            {
                return false;
            }

            foreach (var c in name)
            {
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// One emotion of the common dictionary (Р10): characters may declare only these. The ID name is the avatar file
    /// name (<c>emotion.smirk</c> → <c>smirk.png</c>); <see cref="Name"/> is the Russian word authors pick it by.
    /// </summary>
    public sealed class EmotionDefinition : Definition
    {
        public EmotionDefinition(EntityId id, SourceLocation source, string name)
            : base(id, source)
        {
            Name = name;
        }

        public string Name { get; }
    }

    public sealed class CharacterDefinition : WorldEntityDefinition
    {
        public CharacterDefinition(
            EntityId id,
            SourceLocation source,
            IEnumerable<StateDefinition> states,
            string name,
            InteractionType primaryInteraction,
            IEnumerable<string> emotions)
            : base(id, source, states)
        {
            Name = name;
            PrimaryInteraction = primaryInteraction;
            var list = new List<string>(emotions ?? Array.Empty<string>());
            list.Sort(StringComparer.Ordinal);
            _emotions = new ReadOnlyCollection<string>(list);
        }

        private readonly ReadOnlyCollection<string> _emotions;

        /// <summary>Display name (inventory hints, editor tools). Dialogues do not show it (Р10).</summary>
        public string Name { get; }

        public InteractionType PrimaryInteraction { get; }

        /// <summary>Avatar emotions, sorted; empty when the character has no avatars and cannot speak in a conversation.</summary>
        public IReadOnlyList<string> Emotions => _emotions;

        public bool HasEmotion(string emotion) => _emotions.Contains(emotion);

        public StateDefinition LocationState => StatesByProperty[BuiltInStates.Location];
    }

    /// <summary>
    /// Unique item (Architecture 0.3 §4.2). A left click on an item lying in the world is always <c>take</c>.
    /// </summary>
    public sealed class ItemDefinition : WorldEntityDefinition
    {
        public ItemDefinition(
            EntityId id,
            SourceLocation source,
            IEnumerable<StateDefinition> states,
            string name,
            string description,
            int declarationOrder)
            : base(id, source, states)
        {
            Name = name;
            Description = description;
            DeclarationOrder = declarationOrder;
        }

        public string Name { get; }

        public string Description { get; }

        /// <summary>Position in content (files by path, then position in file). Inventory display order (T18).</summary>
        public int DeclarationOrder { get; }

        public StateDefinition HolderState => StatesByProperty[BuiltInStates.Holder];
    }

    public sealed class ObjectDefinition : WorldEntityDefinition
    {
        public ObjectDefinition(EntityId id, SourceLocation source, IEnumerable<StateDefinition> states, InteractionType primaryInteraction)
            : base(id, source, states)
        {
            PrimaryInteraction = primaryInteraction;
        }

        public InteractionType PrimaryInteraction { get; }
    }

    public sealed class LocationDefinition : WorldEntityDefinition
    {
        public LocationDefinition(EntityId id, SourceLocation source, IEnumerable<StateDefinition> states, string scene)
            : base(id, source, states)
        {
            Scene = scene;
        }

        /// <summary>Unity scene name of this location.</summary>
        public string Scene { get; }
    }

    public sealed class StoryDefinition : WorldEntityDefinition
    {
        public StoryDefinition(EntityId id, SourceLocation source, IEnumerable<StateDefinition> states)
            : base(id, source, states)
        {
        }
    }

    /// <summary>The single <c>game.settings</c> definition.</summary>
    public sealed class GameSettingsDefinition : Definition
    {
        public const string SettingsId = "game.settings";

        public GameSettingsDefinition(EntityId id, SourceLocation source, EntityId playerCharacter)
            : base(id, source)
        {
            PlayerCharacter = playerCharacter;
        }

        public EntityId PlayerCharacter { get; }
    }
}
