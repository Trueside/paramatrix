using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Paramatrix.Content
{
    /// <summary>
    /// Immutable set of validated definitions (Architecture 0.3.1 §2.2). Built only by <see cref="ContentLoader"/>
    /// after the whole content passed validation; holds no runtime State.
    /// </summary>
    public sealed class ContentRegistry
    {
        private readonly Dictionary<EntityId, Definition> _byId;
        private readonly Dictionary<StateAddress, StateDefinition> _states;
        private readonly Dictionary<EntityKind, ReadOnlyCollection<Definition>> _byKind;

        internal ContentRegistry(IEnumerable<Definition> definitions)
        {
            var all = new List<Definition>(definitions);
            all.Sort((a, b) => a.Id.CompareTo(b.Id));

            _byId = new Dictionary<EntityId, Definition>();
            _states = new Dictionary<StateAddress, StateDefinition>();
            var byKind = new Dictionary<EntityKind, List<Definition>>();
            var states = new List<StateDefinition>();
            var items = new List<ItemDefinition>();

            foreach (var definition in all)
            {
                _byId.Add(definition.Id, definition);

                if (!byKind.TryGetValue(definition.Id.Kind, out var list))
                {
                    byKind.Add(definition.Id.Kind, list = new List<Definition>());
                }

                list.Add(definition);

                if (definition is WorldEntityDefinition world)
                {
                    foreach (var state in world.States)
                    {
                        _states.Add(state.Address, state);
                        states.Add(state);
                    }
                }

                if (definition is ItemDefinition item)
                {
                    items.Add(item);
                }

                if (definition is GameSettingsDefinition settings)
                {
                    Game = settings;
                }
            }

            if (Game == null)
            {
                throw new ArgumentException("Content has no game settings.", nameof(definitions));
            }

            _byKind = new Dictionary<EntityKind, ReadOnlyCollection<Definition>>();
            foreach (var pair in byKind)
            {
                _byKind.Add(pair.Key, new ReadOnlyCollection<Definition>(pair.Value));
            }

            states.Sort((a, b) => a.Address.CompareTo(b.Address));
            items.Sort((a, b) => a.DeclarationOrder.CompareTo(b.DeclarationOrder));

            All = new ReadOnlyCollection<Definition>(all);
            States = new ReadOnlyCollection<StateDefinition>(states);
            ItemsInDeclarationOrder = new ReadOnlyCollection<ItemDefinition>(items);
        }

        /// <summary>All definitions sorted by ID.</summary>
        public IReadOnlyList<Definition> All { get; }

        /// <summary>All State definitions sorted by address.</summary>
        public IReadOnlyList<StateDefinition> States { get; }

        public IReadOnlyList<ItemDefinition> ItemsInDeclarationOrder { get; }

        public GameSettingsDefinition Game { get; }

        public bool Exists(EntityId id) => _byId.ContainsKey(id);

        public bool TryGet<T>(EntityId id, out T definition)
            where T : Definition
        {
            if (_byId.TryGetValue(id, out var found) && found is T typed)
            {
                definition = typed;
                return true;
            }

            definition = null;
            return false;
        }

        /// <summary>Gets a definition that must exist (validated reference). Throws otherwise.</summary>
        public T Get<T>(EntityId id)
            where T : Definition
        {
            if (TryGet<T>(id, out var definition))
            {
                return definition;
            }

            throw new KeyNotFoundException($"No {typeof(T).Name} with ID '{id}'.");
        }

        /// <summary>Definitions of one kind sorted by ID.</summary>
        public IReadOnlyList<Definition> GetByKind(EntityKind kind) =>
            _byKind.TryGetValue(kind, out var list) ? (IReadOnlyList<Definition>)list : Array.Empty<Definition>();

        public bool TryGetState(StateAddress address, out StateDefinition definition) =>
            _states.TryGetValue(address, out definition);
    }
}
