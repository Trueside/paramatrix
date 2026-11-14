using System;
using System.Collections.Generic;

namespace Paramatrix.Content
{
    /// <summary>
    /// Cross-file validation and construction of immutable definitions, in phases:
    /// 1) duplicate IDs; 2) game settings and world references; 3) the State table (declared + built-in, T14);
    /// 4) conditions against that table, reference cycles; 5) definitions — only when no Error was reported anywhere.
    /// </summary>
    internal static class ContentLinker
    {
        public static List<Definition> Link(List<Draft> drafts, DiagnosticBag diagnostics, string firstSource)
        {
            var unique = RemoveDuplicates(drafts, diagnostics);

            var locations = new List<EntityId>();
            var characters = new List<EntityId>();
            foreach (var draft in unique.Values)
            {
                if (draft.Id.Kind == EntityKind.Location)
                {
                    locations.Add(draft.Id);
                }
                else if (draft.Id.Kind == EntityKind.Character)
                {
                    characters.Add(draft.Id);
                }
            }

            locations.Sort();
            characters.Sort();

            CheckGameSettings(unique, diagnostics, firstSource);
            foreach (var draft in unique.Values)
            {
                if (draft is WorldDraft world)
                {
                    CheckReferences(world, unique, diagnostics);
                    CheckEmotions(world, unique, diagnostics);
                }
            }

            // State table: everything that can be built, even if other parts of the content have errors,
            // so that conditions are checked and reported in the same run.
            var stateTable = new Dictionary<StateAddress, StateDefinition>();
            var statesByEntity = new Dictionary<EntityId, List<StateDefinition>>();
            foreach (var draft in unique.Values)
            {
                if (draft is WorldDraft world)
                {
                    var states = BuildStates(world, unique, locations, characters);
                    statesByEntity.Add(world.Id, states);
                    foreach (var state in states)
                    {
                        stateTable.Add(state.Address, state);
                    }
                }
            }

            var resolver = new ConditionResolver(unique, stateTable, diagnostics);
            var conditions = new Dictionary<EntityId, ConditionNode>();
            foreach (var draft in unique.Values)
            {
                if (draft is ConditionDefinitionDraft condition && condition.Root != null)
                {
                    var root = resolver.Resolve(condition.Root);
                    if (root != null)
                    {
                        conditions.Add(condition.Id, root);
                    }
                }
            }

            CheckConditionCycles(unique, diagnostics);

            // Scenario layer: dialogues, responses, events.
            unique.TryGetValue(EntityId.Parse(GameSettingsDefinition.SettingsId), out var settingsDraft);
            var settings = settingsDraft as GameSettingsDraft;
            var scenario = new ScenarioResolver(unique, stateTable, resolver, settings?.PlayerCharacter ?? default, diagnostics);
            var scenarioDefinitions = new Dictionary<EntityId, Definition>();
            var responses = new List<ResponseDefinition>();
            var allResponsesValid = true;
            foreach (var draft in unique.Values)
            {
                Definition built = null;
                if (draft is RawDraft raw && draft.Id.Kind == EntityKind.Dialogue)
                {
                    built = scenario.ResolveDialogue(raw);
                }
                else if (draft is RawDraft rawResponse && draft.Id.Kind == EntityKind.Response)
                {
                    var response = scenario.ResolveResponse(rawResponse);
                    allResponsesValid &= response != null;
                    if (response != null)
                    {
                        responses.Add(response);
                    }

                    built = response;
                }
                else if (draft is EventDraft eventDraft)
                {
                    built = scenario.ResolveEvent(eventDraft);
                }

                if (built != null)
                {
                    scenarioDefinitions.Add(built.Id, built);
                }
            }

            // Set-level checks only over a fully valid set: a broken response would make them report noise.
            if (allResponsesValid)
            {
                scenario.CheckResponseSet(responses, settings?.Source ?? new SourceLocation(firstSource ?? "<no content>", null, 0, 0));
            }

            scenario.CheckEventGraphs();

            var result = new List<Definition>();
            if (diagnostics.HasErrors)
            {
                return result;
            }

            foreach (var draft in unique.Values)
            {
                result.Add(scenarioDefinitions.TryGetValue(draft.Id, out var definition)
                    ? definition
                    : Build(draft, statesByEntity, conditions));
            }

            return result;
        }

        private static SortedDictionary<EntityId, Draft> RemoveDuplicates(List<Draft> drafts, DiagnosticBag diagnostics)
        {
            var unique = new SortedDictionary<EntityId, Draft>();
            foreach (var draft in drafts)
            {
                if (unique.TryGetValue(draft.Id, out var first))
                {
                    diagnostics.Error(DiagnosticCode.DuplicateId, draft.Source, $"ID '{draft.Id}' is already defined at {first.Source}.");
                    continue;
                }

                unique.Add(draft.Id, draft);
            }

            return unique;
        }

        private static void CheckGameSettings(SortedDictionary<EntityId, Draft> drafts, DiagnosticBag diagnostics, string firstSource)
        {
            var settingsId = EntityId.Parse(GameSettingsDefinition.SettingsId);
            if (!drafts.TryGetValue(settingsId, out var found) || !(found is GameSettingsDraft settings))
            {
                diagnostics.Error(
                    DiagnosticCode.MissingGameSettings,
                    new SourceLocation(firstSource ?? "<no content>", null, 0, 0),
                    $"Content must define '{GameSettingsDefinition.SettingsId}'.");
                return;
            }

            if (!settings.PlayerCharacter.IsValid)
            {
                return; // already reported while parsing
            }

            if (!drafts.TryGetValue(settings.PlayerCharacter, out var player))
            {
                diagnostics.Error(DiagnosticCode.UnknownReference, settings.PlayerCharacterSource, $"Player character '{settings.PlayerCharacter}' is not defined.");
                return;
            }

            if (player is WorldDraft world && world.InitialPlace == BuiltInStates.None)
            {
                diagnostics.Error(
                    DiagnosticCode.PlayerCharacterWithoutLocation,
                    world.InitialPlaceSource,
                    $"Player character '{player.Id}' must start in a location: its initial_location is the start location of a new game.");
            }
        }

        private static void CheckReferences(WorldDraft draft, SortedDictionary<EntityId, Draft> drafts, DiagnosticBag diagnostics)
        {
            if (draft.InitialPlace != null && StateValueReader.IsEntityReference(draft.InitialPlace))
            {
                var target = EntityId.Parse(draft.InitialPlace);
                if (!drafts.ContainsKey(target))
                {
                    diagnostics.Error(DiagnosticCode.UnknownReference, draft.InitialPlaceSource, $"'{target}' is not defined.");
                }
            }

            foreach (var state in draft.States)
            {
                for (var i = 0; i < state.EnumValues.Count; i++)
                {
                    var value = state.EnumValues[i];
                    if (StateValueReader.IsEntityReference(value) && !drafts.ContainsKey(EntityId.Parse(value)))
                    {
                        diagnostics.Error(
                            DiagnosticCode.UnknownReference,
                            state.EnumValueSources[i],
                            $"Enum value '{value}' of {state.Address} references an undefined entity.");
                    }
                }
            }
        }

        private static void CheckConditionCycles(SortedDictionary<EntityId, Draft> drafts, DiagnosticBag diagnostics)
        {
            // 0 = not visited, 1 = on the current path, 2 = done.
            var color = new Dictionary<EntityId, int>();
            var path = new List<EntityId>();

            void Visit(ConditionDefinitionDraft node)
            {
                color[node.Id] = 1;
                path.Add(node.Id);

                var references = new List<RefConditionDraft>();
                if (node.Root != null)
                {
                    ConditionResolver.CollectReferences(node.Root, references);
                }

                foreach (var reference in references)
                {
                    if (!drafts.TryGetValue(reference.Id, out var target) || !(target is ConditionDefinitionDraft next))
                    {
                        continue; // unknown reference is reported by the resolver
                    }

                    color.TryGetValue(next.Id, out var state);
                    if (state == 1)
                    {
                        var start = path.IndexOf(next.Id);
                        var cycle = new List<string>();
                        for (var i = start; i < path.Count; i++)
                        {
                            cycle.Add(path[i].Value);
                        }

                        cycle.Add(next.Id.Value);
                        diagnostics.Error(DiagnosticCode.ConditionCycle, reference.Source, "Condition reference cycle: " + string.Join(" → ", cycle) + ".");
                    }
                    else if (state == 0)
                    {
                        Visit(next);
                    }
                }

                path.RemoveAt(path.Count - 1);
                color[node.Id] = 2;
            }

            foreach (var draft in drafts.Values)
            {
                if (draft is ConditionDefinitionDraft condition && !color.ContainsKey(condition.Id))
                {
                    Visit(condition);
                }
            }
        }

        private static List<StateDefinition> BuildStates(
            WorldDraft world,
            SortedDictionary<EntityId, Draft> drafts,
            List<EntityId> locations,
            List<EntityId> characters)
        {
            var states = new List<StateDefinition>();
            foreach (var state in world.States)
            {
                states.Add(new StateDefinition(
                    state.Address,
                    state.Type,
                    state.Default,
                    state.Persistence,
                    state.MutationPolicy,
                    state.Min,
                    state.Max,
                    state.EnumValues,
                    false,
                    state.Source));
            }

            var placeIsValid = world.InitialPlace != null
                && (!StateValueReader.IsEntityReference(world.InitialPlace) || drafts.ContainsKey(EntityId.Parse(world.InitialPlace)));
            if (placeIsValid && world.Id.Kind == EntityKind.Character)
            {
                states.Add(BuildLocationState(world, locations));
            }
            else if (placeIsValid && world.Id.Kind == EntityKind.Item)
            {
                states.Add(BuildHolderState(world, locations, characters));
            }

            return states;
        }

        /// <summary>A character declares only emotions of the common dictionary (Р10): a typo is an error, not a new emotion.</summary>
        private static void CheckEmotions(WorldDraft character, IReadOnlyDictionary<EntityId, Draft> drafts, DiagnosticBag diagnostics)
        {
            for (var i = 0; i < character.Emotions.Count; i++)
            {
                if (!EntityId.TryParse(EntityKinds.Prefix(EntityKind.Emotion) + "." + character.Emotions[i], out var id, out _) || !drafts.ContainsKey(id))
                {
                    diagnostics.Error(DiagnosticCode.UnknownEmotion, character.EmotionSources[i],
                        $"{character.Id} declares emotion '{character.Emotions[i]}', which is not in the emotion dictionary (emotion.{character.Emotions[i]}).");
                }
            }
        }

        private static Definition Build(
            Draft draft,
            Dictionary<EntityId, List<StateDefinition>> statesByEntity,
            Dictionary<EntityId, ConditionNode> conditions)
        {
            switch (draft)
            {
                case GameSettingsDraft settings:
                    return new GameSettingsDefinition(settings.Id, settings.Source, settings.PlayerCharacter);
                case ConditionDefinitionDraft condition:
                    return new NamedConditionDefinition(condition.Id, condition.Source, conditions[condition.Id]);
                case EmotionDraft emotion:
                    return new EmotionDefinition(emotion.Id, emotion.Source, emotion.Name);
            }

            var world = (WorldDraft)draft;
            var states = statesByEntity[world.Id];
            switch (world.Id.Kind)
            {
                case EntityKind.Character:
                    return new CharacterDefinition(world.Id, world.Source, states, world.Name, world.PrimaryInteraction, world.Emotions);
                case EntityKind.Item:
                    return new ItemDefinition(world.Id, world.Source, states, world.Name, world.Description, world.DeclarationOrder);
                case EntityKind.Object:
                    return new ObjectDefinition(world.Id, world.Source, states, world.PrimaryInteraction);
                case EntityKind.Location:
                    return new LocationDefinition(world.Id, world.Source, states, world.Scene);
                case EntityKind.Story:
                    return new StoryDefinition(world.Id, world.Source, states);
                default:
                    throw new InvalidOperationException($"Unexpected world kind {world.Id.Kind}.");
            }
        }

        private static StateDefinition BuildLocationState(WorldDraft character, List<EntityId> locations)
        {
            var values = new List<string> { BuiltInStates.None };
            foreach (var location in locations)
            {
                values.Add(location.Value);
            }

            return new StateDefinition(
                new StateAddress(character.Id, BuiltInStates.Location),
                StateType.Enum,
                StateValue.Enum(character.InitialPlace),
                Persistence.Persistent,
                MutationPolicy.ScenarioOrSystem,
                null,
                null,
                values,
                true,
                character.InitialPlaceSource);
        }

        private static StateDefinition BuildHolderState(WorldDraft item, List<EntityId> locations, List<EntityId> characters)
        {
            var values = new List<string> { BuiltInStates.Inventory, BuiltInStates.Consumed };
            foreach (var location in locations)
            {
                values.Add(location.Value);
            }

            foreach (var character in characters)
            {
                values.Add(character.Value);
            }

            return new StateDefinition(
                new StateAddress(item.Id, BuiltInStates.Holder),
                StateType.Enum,
                StateValue.Enum(item.InitialPlace),
                Persistence.Persistent,
                MutationPolicy.SystemOnly,
                null,
                null,
                values,
                true,
                item.InitialPlaceSource);
        }
    }
}
