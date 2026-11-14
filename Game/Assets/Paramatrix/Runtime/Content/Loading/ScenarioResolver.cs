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
                var at = reader.LocationOf(reader.Object["mode"]);
                switch (modeName)
                {
                    case "normal":
                        mode = DialogueMode.Normal;
                        break;
                    case "novel":
                        mode = DialogueMode.Novel;
                        break;
                    case "minimal":
                        _diagnostics.Error(DiagnosticCode.InvalidDialogueMode, at,
                            "Monologues are not dialogues: a scene says a line with the 'say' action, a click on a target gets a response (Р10).");
                        valid = false;
                        break;
                    default:
                        _diagnostics.Error(DiagnosticCode.InvalidDialogueMode, at, $"Dialogue mode '{modeName}' is not supported; use normal or novel.");
                        valid = false;
                        break;
                }
            }
            else
            {
                valid = false;
            }

            // A novel is a conversation with one partner, shown large; a normal dialogue has none (Р12).
            EntityId? partner = null;
            var partnerToken = reader.Optional("partner");
            if (mode == DialogueMode.Novel)
            {
                valid &= ReadRef(reader, "partner", true, out var partnerId, EntityKind.Character);
                partner = partnerId.IsValid ? partnerId : (EntityId?)null;
                if (partner == _player)
                {
                    _diagnostics.Error(DiagnosticCode.InvalidDialoguePartner, reader.LocationOf(partnerToken), "A novel's partner is the one the hero talks to, not the hero.");
                    valid = false;
                }
            }
            else if (partnerToken != null)
            {
                _diagnostics.Error(DiagnosticCode.InvalidDialoguePartner, reader.LocationOf(partnerToken), "Only a novel dialogue has a 'partner'.");
                valid = false;
            }

            var steps = ResolveSteps(reader, "lines", true, draft.SourceName, ref valid);
            if (valid && mode == DialogueMode.Novel && partner.HasValue)
            {
                valid &= CheckNovelSpeakers(steps, partner.Value);
            }

            reader.ReportUnknownFields();
            return valid ? new DialogueDefinition(draft.Id, draft.Source, mode, partner, steps) : null;
        }

        /// <summary>The steps of a dialogue or of a choice's option: lines, and choices (an object with 'choice').</summary>
        private List<DialogueStep> ResolveSteps(JsonObjectReader reader, string field, bool required, string source, ref bool valid)
        {
            var steps = new List<DialogueStep>();
            JArray array;
            if (required)
            {
                array = reader.RequiredArray(field);
                if (array == null)
                {
                    valid = false;
                    return steps;
                }

                if (array.Count == 0)
                {
                    _diagnostics.Error(DiagnosticCode.EmptyDialogue, reader.LocationOf(array), "Dialogue must have at least one line.");
                    valid = false;
                    return steps;
                }
            }
            else
            {
                array = reader.OptionalArray(field, out var present);
                if (!present)
                {
                    return steps;
                }

                if (array == null)
                {
                    valid = false;
                    return steps;
                }
            }

            foreach (var token in array)
            {
                DialogueStep step = token is JObject obj && obj.ContainsKey("choice")
                    ? (DialogueStep)ResolveChoice(token, source)
                    : ResolveLine(token, source);
                valid &= step != null;
                steps.Add(step);
            }

            return steps;
        }

        /// <summary>A choice of 2 to 9 numbered options (a key per option); each option leads on, ends, or goes to another dialogue.</summary>
        private DialogueChoice ResolveChoice(JToken token, string source)
        {
            var reader = JsonObjectReader.ForObject(token, source, _diagnostics, "Dialogue choice");
            var array = reader.RequiredArray("choice");
            reader.ReportUnknownFields();
            if (array == null)
            {
                return null;
            }

            if (array.Count < 2 || array.Count > 9)
            {
                _diagnostics.Error(DiagnosticCode.InvalidChoice, reader.LocationOf(array),
                    $"A choice has {array.Count} option(s); it needs 2 to 9 (one key each). A single answer is a line, not a choice.");
                return null;
            }

            var valid = true;
            var options = new List<DialogueOption>();
            foreach (var optionToken in array)
            {
                var option = ResolveOption(optionToken, source);
                valid &= option != null;
                options.Add(option);
            }

            return valid ? new DialogueChoice(options, reader.Location) : null;
        }

        private DialogueOption ResolveOption(JToken token, string source)
        {
            var reader = JsonObjectReader.ForObject(token, source, _diagnostics, "Choice option");
            if (reader == null)
            {
                return null;
            }

            var label = ReadText(reader, "label", true);
            var valid = label != null;
            var steps = ResolveSteps(reader, "lines", false, source, ref valid);

            var exit = DialogueOptionExit.Continue;
            EntityId? next = null;
            var endToken = reader.Optional("end");
            if (endToken != null)
            {
                if (endToken.Type != JTokenType.Boolean || !endToken.Value<bool>())
                {
                    _diagnostics.Error(DiagnosticCode.InvalidChoice, reader.LocationOf(endToken), "'end' is written only as true: the option ends the dialogue.");
                    valid = false;
                }

                exit = DialogueOptionExit.End;
            }

            if (reader.Has("next"))
            {
                if (endToken != null)
                {
                    _diagnostics.Error(DiagnosticCode.InvalidChoice, reader.Location, "An option either ends the dialogue ('end') or goes on in another ('next'), not both.");
                    valid = false;
                }

                valid &= ReadRef(reader, "next", true, out var nextId, EntityKind.Dialogue);
                exit = DialogueOptionExit.Next;
                next = nextId.IsValid ? nextId : (EntityId?)null;
            }

            reader.ReportUnknownFields();
            return valid ? new DialogueOption(label, steps, exit, next, reader.Location) : null;
        }

        /// <summary>In a novel only the hero and the partner speak: the partner's picture is the one on screen (Р12).</summary>
        private bool CheckNovelSpeakers(IEnumerable<DialogueStep> steps, EntityId partner)
        {
            var valid = true;
            foreach (var step in steps)
            {
                switch (step)
                {
                    case DialogueLine line when line.Speaker != partner && line.Speaker != _player:
                        _diagnostics.Error(DiagnosticCode.InvalidDialoguePartner, line.Source,
                            $"{line.Speaker} speaks in a novel with {partner}: only the hero and the partner speak there.");
                        valid = false;
                        break;
                    case DialogueChoice choice:
                        foreach (var option in choice.Options)
                        {
                            valid &= CheckNovelSpeakers(option.Steps, partner);
                        }

                        break;
                }
            }

            return valid;
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

            var emotion = Emotions.Default;
            var emotionToken = reader.Optional("emotion");
            if (emotionToken != null)
            {
                valid &= reader.ReadString(emotionToken, "emotion", out emotion);
            }

            // The avatar must exist in content: the speaker declares its emotions (Р10).
            if (speaker.IsValid && emotion != null && _drafts.TryGetValue(speaker, out var draft) && draft is WorldDraft character)
            {
                var at = emotionToken != null ? reader.LocationOf(emotionToken) : reader.Location;
                if (character.Emotions.Count == 0)
                {
                    _diagnostics.Error(DiagnosticCode.SpeakerWithoutAvatars, at,
                        $"{speaker} speaks in a conversation but declares no 'emotions': it has no avatar to show.");
                    valid = false;
                }
                else if (!character.Emotions.Contains(emotion))
                {
                    _diagnostics.Error(DiagnosticCode.UnknownEmotion, at,
                        $"{speaker} has no emotion '{emotion}'; declared: {string.Join(", ", character.Emotions)}.");
                    valid = false;
                }
            }

            reader.ReportUnknownFields();
            return valid ? new DialogueLine(speaker, text, emotion, reader.Location) : null;
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

            // target: required for specific, optional for generic (the target's own stub pool, Р10), not allowed otherwise.
            EntityId? target = null;
            if (tier == ResponseTier.Specific || tier == ResponseTier.Generic)
            {
                valid &= ReadRef(reader, "target", tier == ResponseTier.Specific, out var id, InteractableKinds);
                target = id.IsValid ? id : (EntityId?)null;
            }
            else if (tier != 0)
            {
                valid &= NotApplicable(reader, "target", "Field 'target' applies only to specific and generic responses.");
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

            // Exactly one of: text (said every time), lines (prepared monologue, Р10), none: true (fallback only).
            var lines = new List<string>();
            var hasText = reader.Has("text");
            var hasLines = reader.Has("lines");
            var hasNone = reader.Has("none");
            if ((hasText ? 1 : 0) + (hasLines ? 1 : 0) + (hasNone ? 1 : 0) != 1)
            {
                _diagnostics.Error(DiagnosticCode.InvalidResponseText, reader.Location, "A response needs exactly one of 'text', 'lines' or 'none': true.");
                reader.Optional("text");
                reader.Optional("lines");
                reader.Optional("none");
                valid = false;
            }
            else if (hasText)
            {
                var text = ReadText(reader, "text", true);
                valid &= text != null;
                lines.Add(text);
            }
            else if (hasLines)
            {
                valid &= ReadPreparedLines(reader, tier, lines);
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
                lines,
                hasLines);
        }

        /// <summary>A prepared monologue: one or more non-empty lines; only specific and contextual responses have one.</summary>
        private bool ReadPreparedLines(JsonObjectReader reader, ResponseTier tier, List<string> into)
        {
            var array = reader.RequiredArray("lines");
            if (array == null)
            {
                return false;
            }

            var valid = true;
            if (tier == ResponseTier.Generic || tier == ResponseTier.Fallback)
            {
                _diagnostics.Error(DiagnosticCode.InvalidResponseText, reader.LocationOf(array),
                    "Only specific and contextual responses can be prepared monologues ('lines'); stubs and fallbacks use 'text'.");
                valid = false;
            }

            if (array.Count == 0)
            {
                _diagnostics.Error(DiagnosticCode.InvalidResponseText, reader.LocationOf(array), "A prepared monologue needs at least one line.");
                return false;
            }

            foreach (var token in array)
            {
                if (!reader.ReadString(token, "lines", out var line))
                {
                    valid = false;
                    continue;
                }

                if (line.Trim().Length == 0)
                {
                    _diagnostics.Error(DiagnosticCode.EmptyText, reader.LocationOf(token), "A monologue line must not be empty.");
                    valid = false;
                    continue;
                }

                into.Add(line);
            }

            return valid;
        }

        /// <summary>
        /// Rules over the whole set of responses (Р6, Р10): exactly one fallback per interaction type,
        /// no two unconditional responses that would always collide. A stub pool of one is allowed: the "…" that
        /// closes every round keeps the same stub from repeating back to back.
        /// </summary>
        public void CheckResponseSet(IReadOnlyList<ResponseDefinition> responses, SourceLocation settingsSource)
        {
            foreach (var type in InteractionTypes.All)
            {
                ResponseDefinition fallback = null;
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
                }

                if (fallback == null)
                {
                    _diagnostics.Error(
                        DiagnosticCode.MissingFallbackResponse,
                        settingsSource,
                        $"Interaction '{InteractionTypes.Name(type)}' has no fallback response; declare one (text or none: true).");
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

                case "say":
                    valid &= ReadRef(reader, "speaker", true, out var speaker, EntityKind.Character);
                    var said = ReadText(reader, "text", true);
                    valid &= said != null;
                    result = said == null ? null : new SayAction(speaker, said, location);
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
                        $"Action '{name}' is not supported; use set_state, if, end, call, start, inventory, clear_selection, dialogue, say, move, animation or transition.");
                    return null;
            }

            if (owner.ExecutionType == ExecutionType.Background
                && (result is DialogueAction || result is SayAction || result is MoveAction || result is AnimationAction || result is TransitionAction || result is ClearSelectionAction))
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
