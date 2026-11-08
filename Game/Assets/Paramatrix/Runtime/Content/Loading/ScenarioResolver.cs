using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Paramatrix.Content
{
    /// <summary>
    /// Parses and validates events, dialogues and responses against the whole content (Architecture 0.4 §17, Р6, Р7).
    /// Every method returns null for an invalid definition after reporting each problem it found.
    /// </summary>
    internal sealed class ScenarioResolver
    {
        private static readonly string[] TriggerFilters = { "interaction", "target", "item", "location", "dialogue", "event", "property", "to", "condition" };
        private static readonly EntityKind[] InteractableKinds = { EntityKind.Character, EntityKind.Object, EntityKind.Item };

        private readonly IReadOnlyDictionary<EntityId, Draft> _drafts;
        private readonly IReadOnlyDictionary<StateAddress, StateDefinition> _states;
        private readonly ConditionResolver _conditions;
        private readonly EntityId _player;
        private readonly DiagnosticBag _diagnostics;

        private readonly Dictionary<EntityId, List<(EntityId Target, SourceLocation Source)>> _calls =
            new Dictionary<EntityId, List<(EntityId, SourceLocation)>>();

        private readonly Dictionary<EntityId, List<(EntityId Target, SourceLocation Source)>> _starts =
            new Dictionary<EntityId, List<(EntityId, SourceLocation)>>();

        public ScenarioResolver(
            IReadOnlyDictionary<EntityId, Draft> drafts,
            IReadOnlyDictionary<StateAddress, StateDefinition> states,
            ConditionResolver conditions,
            EntityId player,
            DiagnosticBag diagnostics)
        {
            _drafts = drafts;
            _states = states;
            _conditions = conditions;
            _player = player;
            _diagnostics = diagnostics;
        }

        // ================= dialogues =================

        public DialogueDefinition ResolveDialogue(RawDraft draft)
        {
            var reader = new JsonObjectReader(draft.Json, draft.SourceName, _diagnostics);
            reader.Optional("id");
            var valid = true;

            DialogueMode mode = 0;
            if (reader.RequiredString("mode", out var modeName))
            {
                switch (modeName)
                {
                    case "minimal": mode = DialogueMode.Minimal; break;
                    case "vn": mode = DialogueMode.VisualNovel; break;
                    default:
                        _diagnostics.Error(DiagnosticCode.InvalidDialogueMode, reader.LocationOf(reader.Object["mode"]), $"Dialogue mode '{modeName}' is not supported; use minimal or vn.");
                        valid = false;
                        break;
                }
            }
            else
            {
                valid = false;
            }

            var lines = new List<DialogueLine>();
            var array = reader.RequiredArray("lines");
            if (array == null)
            {
                valid = false;
            }
            else if (array.Count == 0)
            {
                _diagnostics.Error(DiagnosticCode.EmptyDialogue, reader.LocationOf(array), "Dialogue must have at least one line.");
                valid = false;
            }
            else
            {
                foreach (var token in array)
                {
                    var line = ResolveLine(token, draft.SourceName);
                    valid &= line != null;
                    lines.Add(line);
                }
            }

            reader.ReportUnknownFields();
            return valid ? new DialogueDefinition(draft.Id, draft.Source, mode, lines) : null;
        }

        private DialogueLine ResolveLine(JToken token, string source)
        {
            var reader = JsonObjectReader.ForObject(token, source, _diagnostics, "Dialogue line");
            if (reader == null)
            {
                return null;
            }

            var valid = ReadRef(reader, "speaker", true, out var speaker, EntityKind.Character);
            var text = ReadText(reader, "text", true);
            valid &= text != null;
            reader.ReportUnknownFields();
            return valid ? new DialogueLine(speaker, text, reader.Location) : null;
        }

        // ================= responses =================

        public ResponseDefinition ResolveResponse(RawDraft draft)
        {
            var reader = new JsonObjectReader(draft.Json, draft.SourceName, _diagnostics);
            reader.Optional("id");
            var valid = true;

            ResponseTier tier = 0;
            if (reader.RequiredString("tier", out var tierName))
            {
                switch (tierName)
                {
                    case "specific": tier = ResponseTier.Specific; break;
                    case "contextual": tier = ResponseTier.Contextual; break;
                    case "generic": tier = ResponseTier.Generic; break;
                    case "fallback": tier = ResponseTier.Fallback; break;
                    default:
                        _diagnostics.Error(DiagnosticCode.InvalidResponseTier, reader.LocationOf(reader.Object["tier"]), $"Response tier '{tierName}' is not supported; use specific, contextual, generic or fallback.");
                        valid = false;
                        break;
                }
            }
            else
            {
                valid = false;
            }

            valid &= ReadInteraction(reader, out var interaction);

            // target: required for specific, not allowed for the other tiers.
            EntityId? target = null;
            if (tier == ResponseTier.Specific)
            {
                valid &= ReadRef(reader, "target", true, out var id, InteractableKinds);
                target = id.IsValid ? id : (EntityId?)null;
            }
            else if (tier != 0)
            {
                valid &= NotApplicable(reader, "target", "Field 'target' applies only to specific responses.");
            }
            else
            {
                reader.Optional("target"); // tier already invalid: do not add an unknown-field error on top
            }

            EntityId? item = null;
            if (reader.Has("item"))
            {
                if (interaction != 0 && !CarriesItem(interaction))
                {
                    valid &= NotApplicable(reader, "item", "Field 'item' applies only to use, apply_item and combine_items.");
                }
                else
                {
                    valid &= ReadRef(reader, "item", false, out var itemId, EntityKind.Item);
                    item = itemId.IsValid ? itemId : (EntityId?)null;
                }
            }

            ConditionNode condition = null;
            if (tier == ResponseTier.Fallback)
            {
                valid &= NotApplicable(reader, "condition", "A fallback response has no condition.");
            }
            else
            {
                var token = reader.Optional("condition");
                if (token != null)
                {
                    condition = ResolveCondition(token, draft.SourceName);
                    valid &= condition != null;
                }
            }

            valid &= ReadRef(reader, "speaker", false, out var speakerId, EntityKind.Character);

            // text XOR none:true; none only for fallback.
            string text = null;
            var hasText = reader.Has("text");
            var hasNone = reader.Has("none");
            if (hasText == hasNone)
            {
                _diagnostics.Error(DiagnosticCode.InvalidResponseText, reader.Location, "A response needs exactly one of 'text' or 'none': true.");
                reader.Optional("text");
                reader.Optional("none");
                valid = false;
            }
            else if (hasText)
            {
                text = ReadText(reader, "text", true);
                valid &= text != null;
            }
            else
            {
                if (reader.RequiredBoolean("none", out var none) && !none)
                {
                    _diagnostics.Error(DiagnosticCode.InvalidResponseText, reader.LocationOf(reader.Object["none"]), "'none' can only be true; write 'text' for a reaction.");
                    valid = false;
                }
                else if (tier != 0 && tier != ResponseTier.Fallback)
                {
                    _diagnostics.Error(DiagnosticCode.InvalidResponseText, reader.LocationOf(reader.Object["none"]), "Only a fallback response may declare 'none' (no reaction).");
                    valid = false;
                }
            }

            reader.ReportUnknownFields();
            if (!valid)
            {
                return null;
            }

            return new ResponseDefinition(
                draft.Id,
                draft.Source,
                tier,
                interaction,
                target,
                item,
                condition,
                speakerId.IsValid ? speakerId : (EntityId?)null,
                text);
        }

        /// <summary>
        /// Rules over the whole set of responses (Р6): exactly one fallback per interaction type,
        /// a generic pool of 0 or ≥ 2 responses, no two unconditional responses that would always collide.
        /// </summary>
        public void CheckResponseSet(IReadOnlyList<ResponseDefinition> responses, SourceLocation settingsSource)
        {
            foreach (var type in InteractionTypes.All)
            {
                ResponseDefinition fallback = null;
                var generic = new List<ResponseDefinition>();
                foreach (var response in responses)
                {
                    if (response.Interaction != type)
                    {
                        continue;
                    }

                    if (response.Tier == ResponseTier.Fallback)
                    {
                        if (fallback != null)
                        {
                            _diagnostics.Error(
                                DiagnosticCode.DuplicateFallbackResponse,
                                response.Source,
                                $"Interaction '{InteractionTypes.Name(type)}' already has fallback '{fallback.Id}'.");
                        }
                        else
                        {
                            fallback = response;
                        }
                    }
                    else if (response.Tier == ResponseTier.Generic)
                    {
                        generic.Add(response);
                    }
                }

                if (fallback == null)
                {
                    _diagnostics.Error(
                        DiagnosticCode.MissingFallbackResponse,
                        settingsSource,
                        $"Interaction '{InteractionTypes.Name(type)}' has no fallback response; declare one (text or none: true).");
                }

                if (generic.Count == 1)
                {
                    _diagnostics.Error(
                        DiagnosticCode.GenericPoolTooSmall,
                        generic[0].Source,
                        $"Generic pool of '{InteractionTypes.Name(type)}' has one response; it cannot rotate without repeating. Add another or remove it.");
                }
            }

            for (var i = 0; i < responses.Count; i++)
            {
                var a = responses[i];
                if (a.Condition != null || (a.Tier != ResponseTier.Specific && a.Tier != ResponseTier.Contextual))
                {
                    continue;
                }

                for (var j = 0; j < i; j++)
                {
                    var b = responses[j];
                    if (b.Condition == null && b.Tier == a.Tier && b.Interaction == a.Interaction
                        && Nullable.Equals(a.Target, b.Target) && Nullable.Equals(a.Item, b.Item))
                    {
                        _diagnostics.Error(
                            DiagnosticCode.DuplicateResponse,
                            a.Source,
                            $"Responses '{b.Id}' and '{a.Id}' match the same action unconditionally; one of them could never be chosen.");
                    }
                }
            }
        }

        // ================= events =================

        public EventDefinition ResolveEvent(EventDraft draft)
        {
            var valid = draft.ExecutionType != 0 && draft.RepeatPolicy != 0 && draft.Triggers != null && draft.Actions != null;

            var triggers = new List<TriggerDefinition>();
            if (draft.Triggers != null)
            {
                foreach (var token in draft.Triggers)
                {
                    var trigger = ResolveTrigger(token, draft.SourceName);
                    valid &= trigger != null;
                    triggers.Add(trigger);
                }
            }

            ConditionNode launch = null;
            if (draft.LaunchCondition != null)
            {
                launch = ResolveCondition(draft.LaunchCondition, draft.SourceName);
                valid &= launch != null;
            }

            var actions = new List<ActionDefinition>();
            if (draft.Actions != null)
            {
                if (draft.Actions.Count == 0)
                {
                    _diagnostics.Error(DiagnosticCode.EmptyActions, JsonDocument.Locate(draft.SourceName, draft.Actions), "An event must have at least one action.");
                    valid = false;
                }

                valid &= ResolveActionList(draft.Actions, draft, actions);
            }

            return valid
                ? new EventDefinition(draft.Id, draft.Source, draft.ExecutionType, draft.RepeatPolicy, triggers, launch, actions)
                : null;
        }

        /// <summary>CALL and START cycles between events (0.4 §17). Self-calls are reported separately as SelfCall.</summary>
        public void CheckEventGraphs()
        {
            FindCycles(_calls, DiagnosticCode.CallCycle, "CALL");
            FindCycles(_starts, DiagnosticCode.StartCycle, "START");
        }

        private TriggerDefinition ResolveTrigger(JToken token, string source)
        {
            var reader = JsonObjectReader.ForObject(token, source, _diagnostics, "Trigger");
            if (reader == null)
            {
                return null;
            }

            if (!reader.RequiredString("type", out var typeName))
            {
                ConsumeFilters(reader);
                return null;
            }

            TriggerType type;
            string[] allowed;
            switch (typeName)
            {
                case "interaction": type = TriggerType.Interaction; allowed = new[] { "interaction", "target", "item" }; break;
                case "item_acquired": type = TriggerType.ItemAcquired; allowed = new[] { "item" }; break;
                case "state_changed": type = TriggerType.StateChanged; allowed = new[] { "target", "property", "to" }; break;
                case "location_entered": type = TriggerType.LocationEntered; allowed = new[] { "location" }; break;
                case "dialogue_completed": type = TriggerType.DialogueCompleted; allowed = new[] { "dialogue" }; break;
                case "event_completed": type = TriggerType.EventCompleted; allowed = new[] { "event" }; break;
                case "conditions_met": type = TriggerType.ConditionsMet; allowed = new[] { "condition" }; break;
                default:
                    _diagnostics.Error(
                        DiagnosticCode.InvalidTriggerType,
                        reader.LocationOf(reader.Object["type"]),
                        $"Trigger type '{typeName}' is not supported; use interaction, item_acquired, state_changed, location_entered, dialogue_completed, event_completed or conditions_met.");
                    ConsumeFilters(reader);
                    return null;
            }

            var valid = true;
            foreach (var filter in TriggerFilters)
            {
                if (reader.Has(filter) && Array.IndexOf(allowed, filter) < 0)
                {
                    valid &= NotApplicable(reader, filter, $"Field '{filter}' does not apply to a '{typeName}' trigger.");
                }
            }

            TriggerDefinition result = null;
            switch (type)
            {
                case TriggerType.Interaction:
                    result = ResolveInteractionTrigger(reader, ref valid);
                    break;

                case TriggerType.ItemAcquired:
                    valid &= ReadRef(reader, "item", false, out var acquired, EntityKind.Item);
                    result = new TriggerDefinition(type, reader.Location, item: Optional(acquired));
                    break;

                case TriggerType.StateChanged:
                    result = ResolveStateChangedTrigger(reader, ref valid);
                    break;

                case TriggerType.LocationEntered:
                    valid &= ReadRef(reader, "location", false, out var location, EntityKind.Location);
                    result = new TriggerDefinition(type, reader.Location, location: Optional(location));
                    break;

                case TriggerType.DialogueCompleted:
                    valid &= ReadRef(reader, "dialogue", false, out var dialogue, EntityKind.Dialogue);
                    result = new TriggerDefinition(type, reader.Location, dialogue: Optional(dialogue));
                    break;

                case TriggerType.EventCompleted:
                    valid &= ReadRef(reader, "event", false, out var completed, EntityKind.Event);
                    result = new TriggerDefinition(type, reader.Location, @event: Optional(completed));
                    break;

                case TriggerType.ConditionsMet:
                    var conditionToken = reader.Required("condition");
                    var condition = conditionToken == null ? null : ResolveCondition(conditionToken, source);
                    valid &= condition != null;
                    result = new TriggerDefinition(type, reader.Location, condition: condition);
                    break;
            }

            reader.ReportUnknownFields();
            return valid ? result : null;
        }

        private TriggerDefinition ResolveInteractionTrigger(JsonObjectReader reader, ref bool valid)
        {
            valid &= ReadInteraction(reader, out var interaction);

            var targetKinds = interaction == InteractionType.CombineItems ? new[] { EntityKind.Item } : InteractableKinds;
            valid &= ReadRef(reader, "target", false, out var target, targetKinds);

            EntityId item = default;
            if (reader.Has("item"))
            {
                if (interaction != 0 && !CarriesItem(interaction))
                {
                    valid &= NotApplicable(reader, "item", "Field 'item' applies only to use, apply_item and combine_items.");
                }
                else
                {
                    valid &= ReadRef(reader, "item", false, out item, EntityKind.Item);
                }
            }
            else if (interaction != 0 && InteractionTypes.RequiresItem(interaction))
            {
                _diagnostics.Error(
                    DiagnosticCode.MissingRequiredField,
                    reader.Location,
                    $"A '{InteractionTypes.Name(interaction)}' trigger needs field 'item'.");
                valid = false;
            }

            return new TriggerDefinition(TriggerType.Interaction, reader.Location, interaction: interaction, target: Optional(target), item: Optional(item));
        }

        private TriggerDefinition ResolveStateChangedTrigger(JsonObjectReader reader, ref bool valid)
        {
            var hasTarget = ReadRef(reader, "target", true, out var target);
            var hasProperty = reader.RequiredString("property", out var property);
            valid &= hasTarget && hasProperty;

            if (!hasTarget || !target.IsValid || !hasProperty)
            {
                reader.Optional("to");
                return null;
            }

            if (!StateAddress.IsValidPropertyName(property))
            {
                _diagnostics.Error(DiagnosticCode.InvalidPropertyName, reader.LocationOf(reader.Object["property"]), $"Property name '{property}' is invalid.");
                reader.Optional("to");
                valid = false;
                return null;
            }

            // Existence of the target was already checked by ReadRef; avoid a second report.
            if (!_drafts.ContainsKey(target) || !_conditions.TryResolveState(target, reader.LocationOf(reader.Object["target"]), property, reader.LocationOf(reader.Object["property"]), out var state))
            {
                reader.Optional("to");
                valid = false;
                return null;
            }

            StateValue? to = null;
            var toToken = reader.Optional("to");
            if (toToken != null)
            {
                if (ReadValue(toToken, state, reader.Source, out var value))
                {
                    to = value;
                }
                else
                {
                    valid = false;
                }
            }

            return new TriggerDefinition(TriggerType.StateChanged, reader.Location, state: state.Address, to: to);
        }

        private bool ResolveActionList(JArray array, EventDraft owner, List<ActionDefinition> into)
        {
            var valid = true;
            var ended = false;
            var unreachableReported = false;
            foreach (var token in array)
            {
                if (ended && !unreachableReported)
                {
                    _diagnostics.Error(DiagnosticCode.UnreachableAction, JsonDocument.Locate(owner.SourceName, token), "Action after 'end' can never run.");
                    unreachableReported = true;
                    valid = false;
                }

                var action = ResolveAction(token, owner);
                valid &= action != null;
                into.Add(action);
                ended |= action is EndAction;
            }

            return valid;
        }

        private ActionDefinition ResolveAction(JToken token, EventDraft owner)
        {
            var reader = JsonObjectReader.ForObject(token, owner.SourceName, _diagnostics, "Action");
            if (reader == null || !reader.RequiredString("action", out var name))
            {
                return null;
            }

            var location = reader.Location;
            var valid = true;
            ActionDefinition result;
            switch (name)
            {
                case "set_state":
                    result = ResolveSetState(reader, ref valid);
                    break;

                case "if":
                    result = ResolveIf(reader, owner, ref valid);
                    break;

                case "end":
                    result = new EndAction(location);
                    break;

                case "call":
                case "start":
                    result = ResolveCallOrStart(reader, owner, name == "call", ref valid);
                    break;

                case "inventory":
                    result = ResolveInventory(reader, ref valid);
                    break;

                case "clear_selection":
                    result = new ClearSelectionAction(location);
                    break;

                case "dialogue":
                    valid &= ReadRef(reader, "dialogue", true, out var dialogue, EntityKind.Dialogue);
                    result = new DialogueAction(dialogue, location);
                    break;

                case "move":
                    valid &= ReadRef(reader, "character", true, out var character, EntityKind.Character);
                    valid &= ReadRef(reader, "to", true, out var destination, InteractableKinds);
                    result = new MoveAction(character, destination, location);
                    break;

                case "animation":
                    valid &= ReadRef(reader, "target", true, out var animated, InteractableKinds);
                    var clip = ReadText(reader, "clip", true);
                    valid &= clip != null;
                    result = clip == null ? null : new AnimationAction(animated, clip, location);
                    break;

                case "transition":
                    valid &= ReadRef(reader, "location", true, out var targetLocation, EntityKind.Location);
                    string entry = null;
                    if (reader.Has("entry"))
                    {
                        entry = ReadText(reader, "entry", true);
                        valid &= entry != null;
                    }

                    result = new TransitionAction(targetLocation, entry, location);
                    break;

                default:
                    _diagnostics.Error(
                        DiagnosticCode.InvalidActionType,
                        reader.LocationOf(reader.Object["action"]),
                        $"Action '{name}' is not supported; use set_state, if, end, call, start, inventory, clear_selection, dialogue, move, animation or transition.");
                    return null;
            }

            if (owner.ExecutionType == ExecutionType.Background
                && (result is DialogueAction || result is MoveAction || result is AnimationAction || result is TransitionAction || result is ClearSelectionAction))
            {
                _diagnostics.Error(
                    DiagnosticCode.BackgroundActionNotAllowed,
                    location,
                    $"A background event cannot use '{name}': background events run instantly and never wait or take control (A6).");
                valid = false;
            }

            reader.ReportUnknownFields();
            return valid ? result : null;
        }

        private ActionDefinition ResolveSetState(JsonObjectReader reader, ref bool valid)
        {
            var hasTarget = ReadRef(reader, "target", true, out var target);
            var hasProperty = reader.RequiredString("property", out var property);
            var valueToken = reader.Required("value");
            if (!hasTarget || !target.IsValid || !hasProperty || valueToken == null)
            {
                valid = false;
                return null;
            }

            if (!StateAddress.IsValidPropertyName(property))
            {
                _diagnostics.Error(DiagnosticCode.InvalidPropertyName, reader.LocationOf(reader.Object["property"]), $"Property name '{property}' is invalid.");
                valid = false;
                return null;
            }

            if (!_drafts.ContainsKey(target)
                || !_conditions.TryResolveState(target, reader.LocationOf(reader.Object["target"]), property, reader.LocationOf(reader.Object["property"]), out var state))
            {
                valid = false;
                return null;
            }

            if (!ReadValue(valueToken, state, reader.Source, out var value))
            {
                valid = false;
                return null;
            }

            if (state.Address.Entity.Kind == EntityKind.Item && property == BuiltInStates.Holder)
            {
                _diagnostics.Error(DiagnosticCode.ActionMutationNotAllowed, reader.Location, "An item's holder changes only through the 'inventory' action (Р7).");
                valid = false;
                return null;
            }

            if (_player.IsValid && state.Address.Entity == _player && property == BuiltInStates.Location)
            {
                _diagnostics.Error(DiagnosticCode.ActionMutationNotAllowed, reader.Location, "The player character changes location only through the 'transition' action.");
                valid = false;
                return null;
            }

            if (!state.Allows(MutationPolicy.ScenarioOnly))
            {
                _diagnostics.Error(DiagnosticCode.ActionMutationNotAllowed, reader.Location, $"{state.Address} is system_only; a scenario cannot set it.");
                valid = false;
                return null;
            }

            return new SetStateAction(state.Address, value, reader.Location);
        }

        private ActionDefinition ResolveIf(JsonObjectReader reader, EventDraft owner, ref bool valid)
        {
            var conditionToken = reader.Required("condition");
            var condition = conditionToken == null ? null : ResolveCondition(conditionToken, owner.SourceName);
            valid &= condition != null;

            var then = new List<ActionDefinition>();
            var thenArray = reader.RequiredArray("then");
            if (thenArray == null)
            {
                valid = false;
            }
            else
            {
                if (thenArray.Count == 0)
                {
                    _diagnostics.Error(DiagnosticCode.EmptyActions, reader.LocationOf(thenArray), "'then' must have at least one action.");
                    valid = false;
                }

                valid &= ResolveActionList(thenArray, owner, then);
            }

            var @else = new List<ActionDefinition>();
            var elseArray = reader.OptionalArray("else", out var hasElse);
            if (hasElse)
            {
                if (elseArray == null)
                {
                    valid = false;
                }
                else
                {
                    if (elseArray.Count == 0)
                    {
                        _diagnostics.Error(DiagnosticCode.EmptyActions, reader.LocationOf(elseArray), "'else' must have at least one action; leave it out instead.");
                        valid = false;
                    }

                    valid &= ResolveActionList(elseArray, owner, @else);
                }
            }

            return valid ? new IfAction(condition, then, @else, reader.Location) : null;
        }

        private ActionDefinition ResolveCallOrStart(JsonObjectReader reader, EventDraft owner, bool isCall, ref bool valid)
        {
            var name = isCall ? "call" : "start";
            if (!ReadRef(reader, "event", true, out var target, EntityKind.Event) || !target.IsValid)
            {
                valid = false;
                return null;
            }

            var location = reader.LocationOf(reader.Object["event"]);
            if (target == owner.Id)
            {
                _diagnostics.Error(
                    DiagnosticCode.SelfCall,
                    location,
                    isCall
                        ? "An event cannot CALL itself."
                        : "An event cannot START itself: it is Running at that moment, so the START always fails (Р4).");
                valid = false;
                return null;
            }

            if (owner.ExecutionType == ExecutionType.Background
                && _drafts.TryGetValue(target, out var targetDraft)
                && targetDraft is EventDraft targetEvent
                && targetEvent.ExecutionType == ExecutionType.Exclusive)
            {
                _diagnostics.Error(
                    DiagnosticCode.BackgroundCallsExclusive,
                    location,
                    $"Background event cannot {name.ToUpperInvariant()} exclusive event '{target}'.");
                valid = false;
            }

            var edges = isCall ? _calls : _starts;
            if (!edges.TryGetValue(owner.Id, out var list))
            {
                edges.Add(owner.Id, list = new List<(EntityId, SourceLocation)>());
            }

            list.Add((target, location));
            return isCall ? (ActionDefinition)new CallAction(target, reader.Location) : new StartAction(target, reader.Location);
        }

        private ActionDefinition ResolveInventory(JsonObjectReader reader, ref bool valid)
        {
            var hasItem = ReadRef(reader, "item", true, out var item, EntityKind.Item);
            var hasTo = reader.RequiredString("to", out var to);
            if (!hasItem || !item.IsValid || !hasTo)
            {
                valid = false;
                return null;
            }

            // The holder table exists only if the item itself is valid; otherwise its error is already reported.
            if (_states.TryGetValue(new StateAddress(item, BuiltInStates.Holder), out var holder)
                && holder.Check(StateValue.Enum(to.Length == 0 ? "?" : to)) != ValueCheck.Ok)
            {
                _diagnostics.Error(
                    DiagnosticCode.InvalidHolderValue,
                    reader.LocationOf(reader.Object["to"]),
                    $"'{to}' is not a place for an item: use inventory, consumed, a location ID or a character ID.");
                valid = false;
                return null;
            }

            return new InventoryAction(item, to, reader.Location);
        }

        // ================= helpers =================

        private ConditionNode ResolveCondition(JToken token, string source)
        {
            var draft = ConditionParser.Parse(token, source, _diagnostics);
            return draft == null ? null : _conditions.Resolve(draft);
        }

        private bool ReadInteraction(JsonObjectReader reader, out InteractionType interaction)
        {
            interaction = 0;
            if (!reader.RequiredString("interaction", out var name))
            {
                return false;
            }

            if (InteractionTypes.TryParse(name, out interaction))
            {
                return true;
            }

            _diagnostics.Error(
                DiagnosticCode.InvalidInteractionType,
                reader.LocationOf(reader.Object["interaction"]),
                $"Interaction '{name}' is not supported; use inspect, primary, take, talk, use, apply_item, combine_items or exit.");
            return false;
        }

        private static bool CarriesItem(InteractionType type) =>
            type == InteractionType.Use || InteractionTypes.RequiresItem(type);

        /// <summary>
        /// Reads an entity reference. Returns false on an invalid value (reported). When the field is absent and not
        /// required, returns true with an uninitialized ID.
        /// </summary>
        private bool ReadRef(JsonObjectReader reader, string field, bool required, out EntityId id, params EntityKind[] kinds)
        {
            id = default;
            var token = required ? reader.Required(field) : reader.Optional(field);
            if (token == null)
            {
                return !required;
            }

            if (!reader.ReadEntityId(token, field, null, out id))
            {
                return false;
            }

            if (kinds.Length > 0 && Array.IndexOf(kinds, id.Kind) < 0)
            {
                var names = new List<string>();
                foreach (var kind in kinds)
                {
                    names.Add(EntityKinds.Prefix(kind));
                }

                _diagnostics.Error(
                    DiagnosticCode.InvalidReferenceKind,
                    reader.LocationOf(token),
                    $"Field '{field}' must reference {string.Join(" / ", names)}, found '{id}'.");
                id = default;
                return false;
            }

            if (!_drafts.ContainsKey(id))
            {
                _diagnostics.Error(DiagnosticCode.UnknownReference, reader.LocationOf(token), $"'{id}' is not defined.");
                id = default;
                return false;
            }

            return true;
        }

        private string ReadText(JsonObjectReader reader, string field, bool required)
        {
            var token = required ? reader.Required(field) : reader.Optional(field);
            if (token == null || !reader.ReadString(token, field, out var text))
            {
                return null;
            }

            if (text.Trim().Length == 0)
            {
                _diagnostics.Error(DiagnosticCode.EmptyText, reader.LocationOf(token), $"Field '{field}' must not be empty.");
                return null;
            }

            return text;
        }

        private bool ReadValue(JToken token, StateDefinition state, string source, out StateValue value)
        {
            if (!StateValueReader.TryRead(token, state.Type, out value, out var error))
            {
                _diagnostics.Error(DiagnosticCode.InvalidValue, JsonDocument.Locate(source, token), $"Value for {state.Address}: {error}");
                return false;
            }

            var check = state.Check(value);
            if (check != ValueCheck.Ok)
            {
                _diagnostics.Error(DiagnosticCode.InvalidValue, JsonDocument.Locate(source, token), $"{value} is not a valid value of {state.Address} ({check}).");
                return false;
            }

            return true;
        }

        private bool NotApplicable(JsonObjectReader reader, string field, string message)
        {
            var token = reader.Optional(field);
            if (token == null)
            {
                return true;
            }

            _diagnostics.Error(DiagnosticCode.FilterNotApplicable, reader.LocationOf(token), message);
            return false;
        }

        private static void ConsumeFilters(JsonObjectReader reader)
        {
            foreach (var filter in TriggerFilters)
            {
                reader.Optional(filter);
            }
        }

        private static EntityId? Optional(EntityId id) => id.IsValid ? id : (EntityId?)null;

        private void FindCycles(Dictionary<EntityId, List<(EntityId Target, SourceLocation Source)>> edges, DiagnosticCode code, string verb)
        {
            var color = new Dictionary<EntityId, int>();
            var path = new List<EntityId>();

            void Visit(EntityId node)
            {
                color[node] = 1;
                path.Add(node);
                if (edges.TryGetValue(node, out var outgoing))
                {
                    foreach (var (target, source) in outgoing)
                    {
                        color.TryGetValue(target, out var state);
                        if (state == 1)
                        {
                            var cycle = new List<string>();
                            for (var i = path.IndexOf(target); i < path.Count; i++)
                            {
                                cycle.Add(path[i].Value);
                            }

                            cycle.Add(target.Value);
                            _diagnostics.Error(code, source, $"{verb} cycle: " + string.Join(" → ", cycle) + ".");
                        }
                        else if (state == 0)
                        {
                            Visit(target);
                        }
                    }
                }

                path.RemoveAt(path.Count - 1);
                color[node] = 2;
            }

            var roots = new List<EntityId>(edges.Keys);
            roots.Sort();
            foreach (var root in roots)
            {
                if (!color.ContainsKey(root))
                {
                    Visit(root);
                }
            }
        }
    }
}
