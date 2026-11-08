using System.Linq;
using NUnit.Framework;
using Paramatrix.Content;
using static Paramatrix.Tests.Content.TestContent;

namespace Paramatrix.Tests.Content
{
    /// <summary>Validator rules for events, triggers, actions, dialogues and responses (0.4 §17, Р4–Р7).</summary>
    public sealed class ScenarioContentTests
    {
        private const string Door =
            "{ 'id': 'object.office_door', 'primary_interaction': 'exit', 'states': {" +
            "  'locked': { 'type': 'boolean', 'default': true, 'persistence': 'persistent', 'mutation_policy': 'scenario_only' }," +
            "  'power':  { 'type': 'integer', 'default': 0, 'min': 0, 'max': 3, 'persistence': 'persistent', 'mutation_policy': 'system_only' } } }";

        private const string Trinity =
            "{ 'id': 'character.trinity', 'name': 'Тринити', 'initial_location': 'location.office', 'primary_interaction': 'talk' }";

        private const string Key = "{ 'id': 'item.key', 'name': 'Ключ', 'description': 'Латунный.', 'initial_holder': 'location.office' }";

        private const string Note = "{ 'id': 'item.note', 'name': 'Записка', 'description': 'Мятая.', 'initial_holder': 'inventory' }";

        private const string Corridor = "{ 'id': 'location.corridor', 'scene': 'Location_Corridor' }";

        private const string Locked =
            "{ 'id': 'condition.door_is_locked', 'condition': { 'target': 'object.office_door', 'property': 'locked', 'operator': 'equals', 'value': true } }";

        private const string LockedDialogue =
            "{ 'id': 'dialogue.door_locked', 'mode': 'minimal', 'lines': [ { 'speaker': 'character.paranoik', 'text': 'Заперто.' } ] }";

        private static readonly string[] World = { Door, Trinity, Key, Note, Corridor, Locked, LockedDialogue };

        private static string Event(string id, string execution, string triggers, string actions, string extra = "") =>
            "{ 'id': '" + id + "', 'execution': '" + execution + "', 'repeat': 'repeatable', 'triggers': [" + triggers + "], 'actions': [" + actions + "]" + extra + " }";

        private static string Exclusive(string actions, string triggers = "") => Event("event.test", "exclusive", triggers, actions);

        private static string Background(string actions, string triggers = "") => Event("event.test", "background", triggers, actions);

        private const string SetLocked = "{ 'action': 'set_state', 'target': 'object.office_door', 'property': 'locked', 'value': false }";

        private static ContentLoadResult With(params string[] definitions) => Load(World.Concat(definitions).ToArray());

        private static ContentRegistry Valid(params string[] definitions) => LoadValid(World.Concat(definitions).ToArray());

        // ================= valid content =================

        [Test]
        public void FullEventLoads()
        {
            var registry = Valid(
                Event("event.background", "background", "", SetLocked),
                Event("event.sub", "exclusive", "", "{ 'action': 'dialogue', 'dialogue': 'dialogue.door_locked' }"),
                "{ 'id': 'event.office_exit', 'execution': 'exclusive', 'repeat': 'once_per_location_visit'," +
                "  'triggers': [ { 'type': 'interaction', 'interaction': 'exit', 'target': 'object.office_door' } ]," +
                "  'launch_condition': { 'not': { 'ref': 'condition.door_is_locked' } }," +
                "  'actions': [" +
                "    { 'action': 'if', 'condition': { 'ref': 'condition.door_is_locked' }," +
                "      'then': [ { 'action': 'dialogue', 'dialogue': 'dialogue.door_locked' }, { 'action': 'end' } ]," +
                "      'else': [ { 'action': 'animation', 'target': 'character.paranoik', 'clip': 'open_door' } ] }," +
                "    { 'action': 'move', 'character': 'character.paranoik', 'to': 'object.office_door' }," +
                "    { 'action': 'inventory', 'item': 'item.key', 'to': 'character.trinity' }," +
                "    { 'action': 'clear_selection' }," +
                "    { 'action': 'start', 'event': 'event.background' }," +
                "    { 'action': 'call', 'event': 'event.sub' }," +
                "    { 'action': 'set_state', 'target': 'character.trinity', 'property': 'location', 'value': 'location.corridor' }," +
                "    { 'action': 'transition', 'location': 'location.corridor', 'entry': 'from_office' } ] }");

            var e = registry.Get<EventDefinition>(EntityId.Parse("event.office_exit"));
            Assert.That(e.ExecutionType, Is.EqualTo(ExecutionType.Exclusive));
            Assert.That(e.RepeatPolicy, Is.EqualTo(RepeatPolicy.OncePerLocationVisit));
            Assert.That(e.Triggers.Single().Interaction, Is.EqualTo(InteractionType.Exit));
            Assert.That(e.Triggers.Single().Target, Is.EqualTo(EntityId.Parse("object.office_door")));
            Assert.That(e.LaunchCondition, Is.InstanceOf<NotCondition>());
            Assert.That(e.Actions.Select(a => a.GetType().Name), Is.EqualTo(new[]
            {
                "IfAction", "MoveAction", "InventoryAction", "ClearSelectionAction", "StartAction", "CallAction", "SetStateAction", "TransitionAction",
            }));

            var branch = (IfAction)e.Actions[0];
            Assert.That(branch.Then.Select(a => a.GetType().Name), Is.EqualTo(new[] { "DialogueAction", "EndAction" }));
            Assert.That(((AnimationAction)branch.Else[0]).Clip, Is.EqualTo("open_door"));
            Assert.That(((InventoryAction)e.Actions[2]).To, Is.EqualTo("character.trinity"));
            Assert.That(((TransitionAction)e.Actions[7]).Entry, Is.EqualTo("from_office"));
            Assert.That(e.Actions[1].IsAsync, Is.True);
            Assert.That(e.Actions[2].IsAsync, Is.False);
        }

        [Test]
        public void EveryTriggerTypeLoads()
        {
            var registry = Valid(Exclusive(SetLocked,
                "{ 'type': 'interaction', 'interaction': 'apply_item', 'target': 'object.office_door', 'item': 'item.key' }," +
                "{ 'type': 'interaction', 'interaction': 'combine_items', 'target': 'item.note', 'item': 'item.key' }," +
                "{ 'type': 'interaction', 'interaction': 'use', 'item': 'item.note' }," +
                "{ 'type': 'interaction', 'interaction': 'inspect' }," +
                "{ 'type': 'item_acquired', 'item': 'item.key' }," +
                "{ 'type': 'item_acquired' }," +
                "{ 'type': 'state_changed', 'target': 'object.office_door', 'property': 'locked', 'to': false }," +
                "{ 'type': 'state_changed', 'target': 'character.trinity', 'property': 'location' }," +
                "{ 'type': 'location_entered', 'location': 'location.office' }," +
                "{ 'type': 'location_entered' }," +
                "{ 'type': 'dialogue_completed', 'dialogue': 'dialogue.door_locked' }," +
                "{ 'type': 'event_completed', 'event': 'event.test' }," +
                "{ 'type': 'conditions_met', 'condition': { 'ref': 'condition.door_is_locked' } }"));

            var triggers = registry.Get<EventDefinition>(EntityId.Parse("event.test")).Triggers;
            Assert.That(triggers.Select(t => t.Type), Is.EqualTo(new[]
            {
                TriggerType.Interaction, TriggerType.Interaction, TriggerType.Interaction, TriggerType.Interaction,
                TriggerType.ItemAcquired, TriggerType.ItemAcquired, TriggerType.StateChanged, TriggerType.StateChanged,
                TriggerType.LocationEntered, TriggerType.LocationEntered, TriggerType.DialogueCompleted,
                TriggerType.EventCompleted, TriggerType.ConditionsMet,
            }));
            Assert.That(triggers[0].Item, Is.EqualTo(EntityId.Parse("item.key")));
            Assert.That(triggers[3].Target, Is.Null);
            Assert.That(triggers[5].Item, Is.Null);
            Assert.That(triggers[6].State, Is.EqualTo(StateAddress.Parse("object.office_door", "locked")));
            Assert.That(triggers[6].To, Is.EqualTo(StateValue.Boolean(false)));
            Assert.That(triggers[7].To, Is.Null);
            Assert.That(triggers[12].Condition, Is.InstanceOf<ConditionRef>());
        }

        [Test]
        public void BackgroundEventWithAllowedActionsLoads()
        {
            Assert.That(With(
                Event("event.other", "background", "", SetLocked),
                Background(
                    SetLocked + ", { 'action': 'if', 'condition': { 'ref': 'condition.door_is_locked' }, 'then': [ " + SetLocked + " ] }," +
                    "{ 'action': 'inventory', 'item': 'item.key', 'to': 'consumed' }," +
                    "{ 'action': 'call', 'event': 'event.other' }, { 'action': 'start', 'event': 'event.other' }, { 'action': 'end' }")).Succeeded,
                Is.True);
        }

        [Test]
        public void ExclusiveMayStartExclusive() =>
            Assert.That(With(Event("event.next", "exclusive", "", SetLocked), Exclusive("{ 'action': 'start', 'event': 'event.next' }")).Succeeded, Is.True);

        [Test]
        public void SharedCalleeIsNotACycle() =>
            Assert.That(With(
                Event("event.c", "exclusive", "", SetLocked),
                Event("event.b", "exclusive", "", "{ 'action': 'call', 'event': 'event.c' }"),
                Event("event.a", "exclusive", "", "{ 'action': 'call', 'event': 'event.b' }, { 'action': 'call', 'event': 'event.c' }")).Succeeded, Is.True);

        [Test]
        public void DialogueLoads()
        {
            var registry = Valid(
                "{ 'id': 'dialogue.morpheus_intro', 'mode': 'vn', 'lines': [" +
                "  { 'speaker': 'character.trinity', 'text': 'Я ждала тебя.' }, { 'speaker': 'character.paranoik', 'text': 'Кто вы?' } ] }");

            var dialogue = registry.Get<DialogueDefinition>(EntityId.Parse("dialogue.morpheus_intro"));
            Assert.That(dialogue.Mode, Is.EqualTo(DialogueMode.VisualNovel));
            Assert.That(dialogue.Lines.Select(l => l.Text), Is.EqualTo(new[] { "Я ждала тебя.", "Кто вы?" }));
            Assert.That(dialogue.Lines[0].Speaker, Is.EqualTo(EntityId.Parse("character.trinity")));
        }

        [Test]
        public void ResponsesOfEveryTierLoad()
        {
            var registry = Valid(
                "{ 'id': 'response.door_locked', 'tier': 'specific', 'interaction': 'exit', 'target': 'object.office_door', 'condition': { 'ref': 'condition.door_is_locked' }, 'text': 'Заперто.' }",
                "{ 'id': 'response.key_door', 'tier': 'specific', 'interaction': 'apply_item', 'target': 'object.office_door', 'item': 'item.key', 'speaker': 'character.trinity', 'text': 'Не тот ключ.' }",
                "{ 'id': 'response.night', 'tier': 'contextual', 'interaction': 'primary', 'condition': { 'ref': 'condition.door_is_locked' }, 'text': 'Ночью всё закрыто.' }",
                "{ 'id': 'response.hm_1', 'tier': 'generic', 'interaction': 'primary', 'text': 'Хм.' }",
                "{ 'id': 'response.hm_2', 'tier': 'generic', 'interaction': 'primary', 'text': 'Не поддаётся.' }");

            var locked = registry.Get<ResponseDefinition>(EntityId.Parse("response.door_locked"));
            Assert.That(locked.Tier, Is.EqualTo(ResponseTier.Specific));
            Assert.That(locked.Target, Is.EqualTo(EntityId.Parse("object.office_door")));
            Assert.That(locked.Condition, Is.InstanceOf<ConditionRef>());
            Assert.That(locked.Speaker, Is.Null);
            Assert.That(registry.Get<ResponseDefinition>(EntityId.Parse("response.key_door")).Item, Is.EqualTo(EntityId.Parse("item.key")));
            Assert.That(registry.Get<ResponseDefinition>(EntityId.Parse("response.fallback_talk")).IsNoReaction, Is.True);
            Assert.That(registry.Get<ResponseDefinition>(EntityId.Parse("response.hm_1")).Target, Is.Null);
        }

        // ================= event header =================

        [Test]
        public void InvalidExecutionType() =>
            AssertErrors(With(Event("event.test", "parallel", "", SetLocked)), DiagnosticCode.InvalidExecutionType);

        [Test]
        public void InvalidRepeatPolicy() =>
            AssertErrors(With("{ 'id': 'event.test', 'execution': 'exclusive', 'repeat': 'twice', 'triggers': [], 'actions': [ " + SetLocked + " ] }"), DiagnosticCode.InvalidRepeatPolicy);

        [Test]
        public void TriggersMustBeArray() =>
            AssertErrors(With("{ 'id': 'event.test', 'execution': 'exclusive', 'repeat': 'once', 'triggers': {}, 'actions': [ " + SetLocked + " ] }"), DiagnosticCode.UnexpectedJsonType);

        [Test]
        public void ActionsMustNotBeEmpty() => AssertErrors(With(Exclusive("")), DiagnosticCode.EmptyActions);

        [Test]
        public void UnknownEventField() =>
            AssertErrors(With(Event("event.test", "exclusive", "", SetLocked, ", 'priority': 1")), DiagnosticCode.UnknownField);

        [Test]
        public void InvalidLaunchCondition() =>
            AssertErrors(With(Event("event.test", "exclusive", "", SetLocked, ", 'launch_condition': { 'any': [] }")), DiagnosticCode.EmptyCompoundCondition);

        // ================= triggers =================

        [Test]
        public void TriggerTypeRequired() => AssertErrors(With(Exclusive(SetLocked, "{ 'interaction': 'exit' }")), DiagnosticCode.MissingRequiredField);

        [Test]
        public void UnknownTriggerType() => AssertErrors(With(Exclusive(SetLocked, "{ 'type': 'player_action' }")), DiagnosticCode.InvalidTriggerType);

        [TestCase("{ 'type': 'interaction', 'interaction': 'exit', 'location': 'location.office' }")]
        [TestCase("{ 'type': 'location_entered', 'target': 'object.office_door' }")]
        [TestCase("{ 'type': 'item_acquired', 'to': true }")]
        [TestCase("{ 'type': 'interaction', 'interaction': 'take', 'item': 'item.key' }")]
        public void FilterMustApplyToTriggerType(string trigger) =>
            AssertErrors(With(Exclusive(SetLocked, trigger)), DiagnosticCode.FilterNotApplicable);

        [Test]
        public void UnknownTriggerField() =>
            AssertErrors(With(Exclusive(SetLocked, "{ 'type': 'location_entered', 'locaton': 'location.office' }")), DiagnosticCode.UnknownField);

        [Test]
        public void InteractionTypeRequired() =>
            AssertErrors(With(Exclusive(SetLocked, "{ 'type': 'interaction', 'target': 'object.office_door' }")), DiagnosticCode.MissingRequiredField);

        [Test]
        public void InteractionTypeMustBeKnown() =>
            AssertErrors(With(Exclusive(SetLocked, "{ 'type': 'interaction', 'interaction': 'kick' }")), DiagnosticCode.InvalidInteractionType);

        [TestCase("apply_item")]
        [TestCase("combine_items")]
        public void ItemRequiredForItemInteractions(string interaction) =>
            AssertErrors(With(Exclusive(SetLocked, "{ 'type': 'interaction', 'interaction': '" + interaction + "' }")), DiagnosticCode.MissingRequiredField);

        [Test]
        public void InteractionTargetMustExist() =>
            AssertErrors(With(Exclusive(SetLocked, "{ 'type': 'interaction', 'interaction': 'exit', 'target': 'object.window' }")), DiagnosticCode.UnknownReference);

        [Test]
        public void InteractionTargetMustBeInteractable() =>
            AssertErrors(With(Exclusive(SetLocked, "{ 'type': 'interaction', 'interaction': 'exit', 'target': 'location.corridor' }")), DiagnosticCode.InvalidReferenceKind);

        [Test]
        public void CombineTargetMustBeItem() =>
            AssertErrors(With(Exclusive(SetLocked, "{ 'type': 'interaction', 'interaction': 'combine_items', 'target': 'object.office_door', 'item': 'item.key' }")), DiagnosticCode.InvalidReferenceKind);

        [Test]
        public void StateChangedNeedsProperty() =>
            AssertErrors(With(Exclusive(SetLocked, "{ 'type': 'state_changed', 'target': 'object.office_door' }")), DiagnosticCode.MissingRequiredField);

        [Test]
        public void StateChangedPropertyMustExist() =>
            AssertErrors(With(Exclusive(SetLocked, "{ 'type': 'state_changed', 'target': 'object.office_door', 'property': 'open' }")), DiagnosticCode.UnknownStateProperty);

        [TestCase("1")]
        [TestCase("'false'")]
        public void StateChangedValueMustMatchType(string value) =>
            AssertErrors(With(Exclusive(SetLocked, "{ 'type': 'state_changed', 'target': 'object.office_door', 'property': 'locked', 'to': " + value + " }")), DiagnosticCode.InvalidValue);

        [Test]
        public void StateChangedValueMustBeAdmissible() =>
            AssertErrors(With(Exclusive(SetLocked, "{ 'type': 'state_changed', 'target': 'object.office_door', 'property': 'power', 'to': 9 }")), DiagnosticCode.InvalidValue);

        [Test]
        public void LocationFilterMustBeLocation() =>
            AssertErrors(With(Exclusive(SetLocked, "{ 'type': 'location_entered', 'location': 'character.trinity' }")), DiagnosticCode.InvalidReferenceKind);

        [Test]
        public void DialogueFilterMustExist() =>
            AssertErrors(With(Exclusive(SetLocked, "{ 'type': 'dialogue_completed', 'dialogue': 'dialogue.missing' }")), DiagnosticCode.UnknownReference);

        [Test]
        public void EventFilterMustExist() =>
            AssertErrors(With(Exclusive(SetLocked, "{ 'type': 'event_completed', 'event': 'event.missing' }")), DiagnosticCode.UnknownReference);

        [Test]
        public void ConditionsMetNeedsCondition() =>
            AssertErrors(With(Exclusive(SetLocked, "{ 'type': 'conditions_met' }")), DiagnosticCode.MissingRequiredField);

        [Test]
        public void ConditionsMetConditionIsValidated() =>
            AssertErrors(With(Exclusive(SetLocked, "{ 'type': 'conditions_met', 'condition': { 'ref': 'condition.nope' } }")), DiagnosticCode.UnknownReference);

        // ================= actions =================

        [Test]
        public void ActionFieldRequired() => AssertErrors(With(Exclusive("{ 'target': 'object.office_door' }")), DiagnosticCode.MissingRequiredField);

        [Test]
        public void UnknownAction() => AssertErrors(With(Exclusive("{ 'action': 'wait' }")), DiagnosticCode.InvalidActionType);

        [Test]
        public void UnknownActionField() => AssertErrors(With(Exclusive("{ 'action': 'end', 'reason': 'x' }")), DiagnosticCode.UnknownField);

        [Test]
        public void SetStateSystemOnlyIsRejected() =>
            AssertErrors(With(Exclusive("{ 'action': 'set_state', 'target': 'object.office_door', 'property': 'power', 'value': 1 }")), DiagnosticCode.ActionMutationNotAllowed);

        [Test]
        public void SetStateOnHolderIsRejected() =>
            AssertErrors(With(Exclusive("{ 'action': 'set_state', 'target': 'item.key', 'property': 'holder', 'value': 'inventory' }")), DiagnosticCode.ActionMutationNotAllowed);

        [Test]
        public void SetStateOnPlayerLocationIsRejected() =>
            AssertErrors(With(Exclusive("{ 'action': 'set_state', 'target': 'character.paranoik', 'property': 'location', 'value': 'location.corridor' }")), DiagnosticCode.ActionMutationNotAllowed);

        [TestCase("1")]
        [TestCase("'false'")]
        public void SetStateValueMustMatchType(string value) =>
            AssertErrors(With(Exclusive("{ 'action': 'set_state', 'target': 'object.office_door', 'property': 'locked', 'value': " + value + " }")), DiagnosticCode.InvalidValue);

        [Test]
        public void SetStateEnumValueMustBeDeclared() =>
            AssertErrors(With(Exclusive("{ 'action': 'set_state', 'target': 'character.trinity', 'property': 'location', 'value': 'location.roof' }")), DiagnosticCode.InvalidValue);

        [Test]
        public void SetStatePropertyMustExist() =>
            AssertErrors(With(Exclusive("{ 'action': 'set_state', 'target': 'object.office_door', 'property': 'open', 'value': true }")), DiagnosticCode.UnknownStateProperty);

        [Test]
        public void SetStateTargetMustOwnState() =>
            AssertErrors(With(Exclusive("{ 'action': 'set_state', 'target': 'dialogue.door_locked', 'property': 'x', 'value': true }")), DiagnosticCode.InvalidReferenceKind);

        [Test]
        public void IfNeedsThen() =>
            AssertErrors(With(Exclusive("{ 'action': 'if', 'condition': { 'ref': 'condition.door_is_locked' } }")), DiagnosticCode.MissingRequiredField);

        [Test]
        public void IfThenMustNotBeEmpty() =>
            AssertErrors(With(Exclusive("{ 'action': 'if', 'condition': { 'ref': 'condition.door_is_locked' }, 'then': [] }")), DiagnosticCode.EmptyActions);

        [Test]
        public void IfElseMustNotBeEmpty() =>
            AssertErrors(With(Exclusive("{ 'action': 'if', 'condition': { 'ref': 'condition.door_is_locked' }, 'then': [ " + SetLocked + " ], 'else': [] }")), DiagnosticCode.EmptyActions);

        [Test]
        public void IfConditionIsValidated() =>
            AssertErrors(With(Exclusive("{ 'action': 'if', 'condition': { }, 'then': [ " + SetLocked + " ] }")), DiagnosticCode.InvalidConditionForm);

        [Test]
        public void ErrorsInsideBranchesAreFound() =>
            AssertErrors(With(Exclusive("{ 'action': 'if', 'condition': { 'ref': 'condition.door_is_locked' }, 'then': [ { 'action': 'jump' } ], 'else': [ { 'action': 'dialogue', 'dialogue': 'dialogue.none' } ] }")),
                DiagnosticCode.InvalidActionType, DiagnosticCode.UnknownReference);

        [Test]
        public void ActionAfterEndIsUnreachable() =>
            AssertErrors(With(Exclusive("{ 'action': 'end' }, " + SetLocked + ", " + SetLocked)), DiagnosticCode.UnreachableAction);

        [Test]
        public void ActionAfterEndInBranchIsUnreachable() =>
            AssertErrors(With(Exclusive("{ 'action': 'if', 'condition': { 'ref': 'condition.door_is_locked' }, 'then': [ { 'action': 'end' }, " + SetLocked + " ] }")), DiagnosticCode.UnreachableAction);

        [Test]
        public void CallTargetMustExist() => AssertErrors(With(Exclusive("{ 'action': 'call', 'event': 'event.missing' }")), DiagnosticCode.UnknownReference);

        [Test]
        public void CallTargetMustBeEvent() => AssertErrors(With(Exclusive("{ 'action': 'call', 'event': 'dialogue.door_locked' }")), DiagnosticCode.InvalidReferenceKind);

        [TestCase("call")]
        [TestCase("start")]
        public void SelfCallIsRejected(string verb) => AssertErrors(With(Exclusive("{ 'action': '" + verb + "', 'event': 'event.test' }")), DiagnosticCode.SelfCall);

        [Test]
        public void CallCycle()
        {
            var result = With(
                Event("event.a", "exclusive", "", "{ 'action': 'call', 'event': 'event.b' }"),
                Event("event.b", "exclusive", "", "{ 'action': 'if', 'condition': { 'ref': 'condition.door_is_locked' }, 'then': [ { 'action': 'call', 'event': 'event.a' } ] }"));

            AssertErrors(result, DiagnosticCode.CallCycle);
            Assert.That(result.Diagnostics.Single(d => d.Code == DiagnosticCode.CallCycle).Message, Does.Contain("event.a → event.b → event.a"));
        }

        [Test]
        public void StartCycle() =>
            AssertErrors(With(
                Event("event.a", "background", "", "{ 'action': 'start', 'event': 'event.b' }"),
                Event("event.b", "background", "", "{ 'action': 'start', 'event': 'event.a' }")), DiagnosticCode.StartCycle);

        [TestCase("{ 'action': 'dialogue', 'dialogue': 'dialogue.door_locked' }")]
        [TestCase("{ 'action': 'move', 'character': 'character.trinity', 'to': 'object.office_door' }")]
        [TestCase("{ 'action': 'animation', 'target': 'object.office_door', 'clip': 'shake' }")]
        [TestCase("{ 'action': 'transition', 'location': 'location.corridor' }")]
        [TestCase("{ 'action': 'clear_selection' }")]
        public void BackgroundCannotWaitOrTakeControl(string action) =>
            AssertErrors(With(Background(action)), DiagnosticCode.BackgroundActionNotAllowed);

        [Test]
        public void BackgroundRuleAppliesInsideBranches() =>
            AssertErrors(With(Background("{ 'action': 'if', 'condition': { 'ref': 'condition.door_is_locked' }, 'then': [ { 'action': 'dialogue', 'dialogue': 'dialogue.door_locked' } ] }")),
                DiagnosticCode.BackgroundActionNotAllowed);

        [TestCase("call")]
        [TestCase("start")]
        public void BackgroundCannotInvokeExclusive(string verb) =>
            AssertErrors(With(Event("event.scene", "exclusive", "", SetLocked), Background("{ 'action': '" + verb + "', 'event': 'event.scene' }")),
                DiagnosticCode.BackgroundCallsExclusive);

        [TestCase("nowhere")]
        [TestCase("location.roof")]
        [TestCase("object.office_door")]
        [TestCase("")]
        public void InventoryDestinationMustBeAPlace(string to) =>
            AssertErrors(With(Exclusive("{ 'action': 'inventory', 'item': 'item.key', 'to': '" + to + "' }")), DiagnosticCode.InvalidHolderValue);

        [Test]
        public void InventoryItemMustBeItem() =>
            AssertErrors(With(Exclusive("{ 'action': 'inventory', 'item': 'object.office_door', 'to': 'inventory' }")), DiagnosticCode.InvalidReferenceKind);

        [Test]
        public void MoveDestinationMustBeInteractable() =>
            AssertErrors(With(Exclusive("{ 'action': 'move', 'character': 'character.paranoik', 'to': 'location.corridor' }")), DiagnosticCode.InvalidReferenceKind);

        [Test]
        public void MoveNeedsCharacter() =>
            AssertErrors(With(Exclusive("{ 'action': 'move', 'character': 'object.office_door', 'to': 'item.key' }")), DiagnosticCode.InvalidReferenceKind);

        [Test]
        public void AnimationClipMustNotBeEmpty() =>
            AssertErrors(With(Exclusive("{ 'action': 'animation', 'target': 'character.paranoik', 'clip': ' ' }")), DiagnosticCode.EmptyText);

        [Test]
        public void TransitionLocationMustExist() =>
            AssertErrors(With(Exclusive("{ 'action': 'transition', 'location': 'location.roof' }")), DiagnosticCode.UnknownReference);

        [Test]
        public void TransitionEntryMustNotBeEmpty() =>
            AssertErrors(With(Exclusive("{ 'action': 'transition', 'location': 'location.corridor', 'entry': '' }")), DiagnosticCode.EmptyText);

        [Test]
        public void DialogueActionTargetMustBeDialogue() =>
            AssertErrors(With(Exclusive("{ 'action': 'dialogue', 'dialogue': 'event.test' }")), DiagnosticCode.InvalidReferenceKind);

        // ================= dialogues =================

        [Test]
        public void DialogueModeMustBeKnown() =>
            AssertErrors(With("{ 'id': 'dialogue.x', 'mode': 'bubble', 'lines': [ { 'speaker': 'character.paranoik', 'text': 'А.' } ] }"), DiagnosticCode.InvalidDialogueMode);

        [Test]
        public void DialogueNeedsLines() => AssertErrors(With("{ 'id': 'dialogue.x', 'mode': 'vn', 'lines': [] }"), DiagnosticCode.EmptyDialogue);

        [Test]
        public void SpeakerMustExist() =>
            AssertErrors(With("{ 'id': 'dialogue.x', 'mode': 'vn', 'lines': [ { 'speaker': 'character.neo', 'text': 'А.' } ] }"), DiagnosticCode.UnknownReference);

        [Test]
        public void SpeakerMustBeCharacter() =>
            AssertErrors(With("{ 'id': 'dialogue.x', 'mode': 'vn', 'lines': [ { 'speaker': 'object.office_door', 'text': 'А.' } ] }"), DiagnosticCode.InvalidReferenceKind);

        [Test]
        public void LineTextMustNotBeEmpty() =>
            AssertErrors(With("{ 'id': 'dialogue.x', 'mode': 'vn', 'lines': [ { 'speaker': 'character.paranoik', 'text': '' } ] }"), DiagnosticCode.EmptyText);

        [Test]
        public void UnknownLineField() =>
            AssertErrors(With("{ 'id': 'dialogue.x', 'mode': 'vn', 'lines': [ { 'speaker': 'character.paranoik', 'text': 'А.', 'emotion': 'sad' } ] }"), DiagnosticCode.UnknownField);

        [Test]
        public void UnknownDialogueField() =>
            AssertErrors(With("{ 'id': 'dialogue.x', 'mode': 'vn', 'skippable': true, 'lines': [ { 'speaker': 'character.paranoik', 'text': 'А.' } ] }"), DiagnosticCode.UnknownField);

        // ================= responses =================

        [Test]
        public void ResponseTierMustBeKnown() =>
            AssertErrors(With("{ 'id': 'response.x', 'tier': 'special', 'interaction': 'primary', 'target': 'object.office_door', 'text': 'А.' }"), DiagnosticCode.InvalidResponseTier);

        [Test]
        public void SpecificNeedsTarget() =>
            AssertErrors(With("{ 'id': 'response.x', 'tier': 'specific', 'interaction': 'primary', 'text': 'А.' }"), DiagnosticCode.MissingRequiredField);

        [TestCase("contextual")]
        [TestCase("generic")]
        [TestCase("fallback")]
        public void OnlySpecificHasTarget(string tier) =>
            AssertErrors(With("{ 'id': 'response.x', 'tier': '" + tier + "', 'interaction': 'primary', 'target': 'object.office_door', 'text': 'А.' }"), DiagnosticCode.FilterNotApplicable);

        [Test]
        public void FallbackHasNoCondition() =>
            AssertErrors(With("{ 'id': 'response.x', 'tier': 'fallback', 'interaction': 'primary', 'condition': { 'ref': 'condition.door_is_locked' }, 'text': 'А.' }"),
                DiagnosticCode.FilterNotApplicable);

        [Test]
        public void ResponseItemOnlyForItemInteractions() =>
            AssertErrors(With("{ 'id': 'response.x', 'tier': 'specific', 'interaction': 'take', 'target': 'item.key', 'item': 'item.note', 'text': 'А.' }"), DiagnosticCode.FilterNotApplicable);

        [TestCase("'text': 'А.', 'none': true")]
        [TestCase("'speaker': 'character.paranoik'")]
        public void ResponseNeedsExactlyTextOrNone(string body) =>
            AssertErrors(With("{ 'id': 'response.x', 'tier': 'specific', 'interaction': 'primary', 'target': 'object.office_door', " + body + " }"), DiagnosticCode.InvalidResponseText);

        [Test]
        public void NoneOnlyForFallback() =>
            AssertErrors(With("{ 'id': 'response.x', 'tier': 'specific', 'interaction': 'primary', 'target': 'object.office_door', 'none': true }"), DiagnosticCode.InvalidResponseText);

        [Test]
        public void NoneMustBeTrue() =>
            AssertErrors(LoadFallbacksReplacing("talk", "{ 'id': 'response.fallback_talk', 'tier': 'fallback', 'interaction': 'talk', 'none': false }"),
                DiagnosticCode.InvalidResponseText);

        [Test]
        public void ResponseSpeakerMustBeCharacter() =>
            AssertErrors(With("{ 'id': 'response.x', 'tier': 'specific', 'interaction': 'primary', 'target': 'object.office_door', 'speaker': 'item.key', 'text': 'А.' }"), DiagnosticCode.InvalidReferenceKind);

        [Test]
        public void EveryInteractionNeedsFallback()
        {
            var result = LoadFallbacksReplacing("exit", null);
            AssertErrors(result, DiagnosticCode.MissingFallbackResponse);
            Assert.That(result.Diagnostics.Single().Message, Does.Contain("'exit'"));
        }

        [Test]
        public void OnlyOneFallbackPerInteraction() =>
            AssertErrors(With("{ 'id': 'response.another_take', 'tier': 'fallback', 'interaction': 'take', 'text': 'Нет.' }"), DiagnosticCode.DuplicateFallbackResponse);

        [Test]
        public void GenericPoolOfOneIsRejected() =>
            AssertErrors(With("{ 'id': 'response.hm', 'tier': 'generic', 'interaction': 'primary', 'text': 'Хм.' }"), DiagnosticCode.GenericPoolTooSmall);

        [Test]
        public void UnconditionalDuplicatesAreRejected() =>
            AssertErrors(With(
                "{ 'id': 'response.a', 'tier': 'specific', 'interaction': 'primary', 'target': 'object.office_door', 'text': 'А.' }",
                "{ 'id': 'response.b', 'tier': 'specific', 'interaction': 'primary', 'target': 'object.office_door', 'text': 'Б.' }"),
                DiagnosticCode.DuplicateResponse);

        [Test]
        public void ConditionalOrDistinctResponsesAreFine() =>
            Assert.That(With(
                "{ 'id': 'response.a', 'tier': 'specific', 'interaction': 'primary', 'target': 'object.office_door', 'text': 'А.' }",
                "{ 'id': 'response.b', 'tier': 'specific', 'interaction': 'primary', 'target': 'object.office_door', 'condition': { 'ref': 'condition.door_is_locked' }, 'text': 'Б.' }",
                "{ 'id': 'response.c', 'tier': 'specific', 'interaction': 'inspect', 'target': 'object.office_door', 'text': 'В.' }",
                "{ 'id': 'response.d', 'tier': 'specific', 'interaction': 'primary', 'target': 'character.trinity', 'text': 'Г.' }").Succeeded,
                Is.True);

        /// <summary>Loads base content with the standard fallbacks, one of them replaced (null = removed).</summary>
        private static ContentLoadResult LoadFallbacksReplacing(string interaction, string replacement)
        {
            var fallbacks = Fallbacks
                .Select(f => f.Contains("'interaction': '" + interaction + "'") ? replacement : f)
                .Where(f => f != null)
                .ToArray();
            return ContentLoader.Load(new[] { Base(), new ContentSource("fallbacks.json", File(fallbacks)) });
        }
    }
}
