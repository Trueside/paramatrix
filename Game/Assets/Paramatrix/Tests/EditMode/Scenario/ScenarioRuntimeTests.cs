using NUnit.Framework;
using Paramatrix.Content;
using Paramatrix.Scenario;
using static Paramatrix.Tests.Scenario.ScenarioHarness;

namespace Paramatrix.Tests.Scenario
{
    /// <summary>Architecture 0.4 §18: triggers, lifecycle, actions, execution — on fake ports.</summary>
    public sealed class ScenarioRuntimeTests
    {
        private const string Door = "{ 'id': 'object.office_door', 'primary_interaction': 'exit' }";
        private const string Window = "{ 'id': 'object.window', 'primary_interaction': 'primary' }";
        private const string Trinity = "{ 'id': 'character.trinity', 'name': 'Тринити', 'initial_location': 'location.office', 'primary_interaction': 'talk' }";
        private const string Key = "{ 'id': 'item.key', 'name': 'Ключ', 'description': 'Латунный.', 'initial_holder': 'location.office' }";
        private const string Corridor = "{ 'id': 'location.corridor', 'scene': 'Location_Corridor' }";

        private static readonly string[] World =
        {
            Door, Window, Trinity, Key, Corridor,
            Flags("a", "b", "c", "locked", "seen"),
            Line("dialogue.hello", "Привет."),
            Line("dialogue.bye", "Пока."),
        };

        private static ScenarioHarness Create(params string[] definitions) => ScenarioHarness.Create(With(World, definitions));

        // ================= triggers =================

        [Test]
        public void ClickRunsExclusiveEventUnderControl()
        {
            var h = Create(Event("event.open", "exclusive", "repeatable", OnClick("exit", "object.office_door"), SetFlag("a", true)));

            h.Click(InteractionType.Exit, "object.office_door");

            Assert.That(h.Get("story.flags", "a").AsBoolean, Is.True);
            Assert.That(h.Status("event.open"), Is.EqualTo(EventStatus.Completed));
            Assert.That(h.TakeLog(), Is.EqualTo(new[] { "acquire", "release:Completed" }));
            Assert.That(h.HoldsControl, Is.False);
            h.AssertNoDiagnostics();
        }

        [Test]
        public void NonMatchingClickFallsBackToResponse()
        {
            var h = Create(Event("event.open", "exclusive", "repeatable", OnClick("exit", "object.office_door"), SetFlag("a", true)));

            h.Click(InteractionType.Exit, "object.window");

            Assert.That(h.Get("story.flags", "a").AsBoolean, Is.False);
            Assert.That(h.TakeLog(), Is.EqualTo(new[] { "acquire", "line:character.paranoik:Туда не пройти." }));
            h.Complete();
            Assert.That(h.TakeLog(), Is.EqualTo(new[] { "release:Completed" }));
        }

        [Test]
        public void StateChangedTriggerWithValueFilter()
        {
            var h = Create(Event("event.react", "background", "repeatable",
                "{ 'type': 'state_changed', 'target': 'story.flags', 'property': 'a', 'to': true }", SetFlag("b", true)));

            h.SetSystem("story.flags", "a", StateValue.Boolean(true));
            Assert.That(h.Get("story.flags", "b").AsBoolean, Is.True);

            h.SetSystem("story.flags", "b", StateValue.Boolean(false));
            h.SetSystem("story.flags", "a", StateValue.Boolean(false));
            Assert.That(h.Get("story.flags", "b").AsBoolean, Is.False, "to=true filter must not match a change to false");
            h.AssertNoDiagnostics();
        }

        [Test]
        public void LocationEnteredStartsNewVisit()
        {
            var h = Create(Event("event.intro", "exclusive", "once", "{ 'type': 'location_entered', 'location': 'location.office' }", SetFlag("a", true)));

            Assert.That(h.Runtime.CurrentVisit, Is.EqualTo(0));
            h.Runtime.BeginLocationVisit(EntityId.Parse("location.office"));

            Assert.That(h.Runtime.CurrentVisit, Is.EqualTo(1));
            Assert.That(h.Get("story.flags", "a").AsBoolean, Is.True);
        }

        [Test]
        public void DialogueAndEventCompletedTriggers()
        {
            var h = Create(
                Event("event.talk", "exclusive", "repeatable", OnClick("talk", "character.trinity"), Dialogue("dialogue.hello")),
                Event("event.after_dialogue", "background", "repeatable", "{ 'type': 'dialogue_completed', 'dialogue': 'dialogue.hello' }", SetFlag("a", true)),
                Event("event.after_event", "background", "repeatable", "{ 'type': 'event_completed', 'event': 'event.talk' }", SetFlag("b", true)));

            h.Click(InteractionType.Talk, "character.trinity");
            Assert.That(h.Get("story.flags", "a").AsBoolean, Is.False, "dialogue still open");

            h.Complete();

            Assert.That(h.Get("story.flags", "a").AsBoolean, Is.True);
            Assert.That(h.Get("story.flags", "b").AsBoolean, Is.True);
        }

        [Test]
        public void ItemAcquiredTrigger()
        {
            var h = Create(
                Event("event.take", "exclusive", "once", OnClick("take", "item.key"), "{ 'action': 'inventory', 'item': 'item.key', 'to': 'inventory' }"),
                Event("event.got_key", "background", "once", "{ 'type': 'item_acquired', 'item': 'item.key' }", SetFlag("a", true)));

            h.Click(InteractionType.Take, "item.key");

            Assert.That(h.Get("item.key", "holder").AsEnum, Is.EqualTo("inventory"));
            Assert.That(h.Get("story.flags", "a").AsBoolean, Is.True);
        }

        [Test]
        public void SeveralEventsOnOneAutomaticTriggerRunInIdOrder()
        {
            var h = Create(
                Event("event.b_second", "exclusive", "once", "{ 'type': 'location_entered' }", Dialogue("dialogue.bye")),
                Event("event.a_first", "exclusive", "once", "{ 'type': 'location_entered' }", Dialogue("dialogue.hello")));

            h.Runtime.BeginLocationVisit(EntityId.Parse("location.office"));
            Assert.That(h.TakeLog(), Is.EqualTo(new[] { "acquire", "dialogue:dialogue.hello" }));
            Assert.That(h.Runtime.QueuedExclusiveCount, Is.EqualTo(1));

            h.Complete();
            Assert.That(h.TakeLog(), Is.EqualTo(new[] { "release:Completed", "acquire", "dialogue:dialogue.bye" }));
            h.Complete();
            Assert.That(h.Status("event.b_second"), Is.EqualTo(EventStatus.Completed));
        }

        [Test]
        public void CombineItemsMatchesEitherOrder()
        {
            var h = Create(
                "{ 'id': 'item.note', 'name': 'Записка', 'description': 'М.', 'initial_holder': 'inventory' }",
                Event("event.combine", "background", "repeatable",
                    "{ 'type': 'interaction', 'interaction': 'combine_items', 'target': 'item.key', 'item': 'item.note' }", SetFlag("a", true)));

            h.Click(InteractionType.CombineItems, "item.note", "item.key");
            Assert.That(h.Get("story.flags", "a").AsBoolean, Is.True);
        }

        // ================= lifecycle & repeat policies =================

        [Test]
        public void RunningEventIgnoresRepeatedAutomaticRequest()
        {
            var h = Create(Event("event.scene", "exclusive", "repeatable", "{ 'type': 'state_changed', 'target': 'story.flags', 'property': 'a' }", Dialogue("dialogue.hello")));

            h.SetSystem("story.flags", "a", StateValue.Boolean(true));
            Assert.That(h.Status("event.scene"), Is.EqualTo(EventStatus.Running));
            h.SetSystem("story.flags", "a", StateValue.Boolean(false));

            Assert.That(h.Runtime.QueuedExclusiveCount, Is.Zero);
            h.Complete();
            Assert.That(h.PendingOperations, Is.Zero);
            h.AssertNoDiagnostics();
        }

        [Test]
        public void OnceRunsOnlyOnce()
        {
            var h = Create(Event("event.once", "exclusive", "once", OnClick("primary", "object.window"), SetFlag("a", true)));

            h.Click(InteractionType.Primary, "object.window");
            Assert.That(h.Get("story.flags", "a").AsBoolean, Is.True);
            h.SetSystem("story.flags", "a", StateValue.Boolean(false));
            h.TakeLog();

            h.Click(InteractionType.Primary, "object.window");

            Assert.That(h.Get("story.flags", "a").AsBoolean, Is.False);
            Assert.That(h.TakeLog(), Is.EqualTo(new[] { "acquire", "line:character.paranoik:Ничего не происходит." }));
        }

        [Test]
        public void RepeatableRunsAgain()
        {
            var h = Create(Event("event.again", "background", "repeatable", OnClick("primary", "object.window"), SetFlag("a", true)));

            h.Click(InteractionType.Primary, "object.window");
            h.SetSystem("story.flags", "a", StateValue.Boolean(false));
            h.Click(InteractionType.Primary, "object.window");

            Assert.That(h.Get("story.flags", "a").AsBoolean, Is.True);
        }

        [Test]
        public void OncePerLocationVisit()
        {
            var h = Create(Event("event.radio", "background", "once_per_location_visit", "{ 'type': 'location_entered' }," + OnClick("primary", "object.window"), SetFlag("a", true)));
            var office = EntityId.Parse("location.office");

            h.Runtime.BeginLocationVisit(office);
            Assert.That(h.Get("story.flags", "a").AsBoolean, Is.True);
            h.SetSystem("story.flags", "a", StateValue.Boolean(false));

            h.Click(InteractionType.Primary, "object.window");
            Assert.That(h.Get("story.flags", "a").AsBoolean, Is.False, "already completed in this Visit");

            h.Runtime.BeginLocationVisit(office);
            Assert.That(h.Get("story.flags", "a").AsBoolean, Is.True, "a new Visit allows it again");
        }

        [Test]
        public void LaunchConditionFalseDoesNotStart()
        {
            var h = Create(Event("event.guarded", "exclusive", "repeatable", OnClick("primary", "object.window"), SetFlag("b", true),
                ", 'launch_condition': " + FlagIs("a", true)));

            h.Click(InteractionType.Primary, "object.window");
            Assert.That(h.Status("event.guarded"), Is.EqualTo(EventStatus.Idle));

            h.CompleteAll();
            h.SetSystem("story.flags", "a", StateValue.Boolean(true));
            h.Click(InteractionType.Primary, "object.window");
            Assert.That(h.Get("story.flags", "b").AsBoolean, Is.True);
        }

        [Test]
        public void FailureKeepsAppliedMutationsAndReleasesControl()
        {
            var h = Create(Event("event.broken", "exclusive", "repeatable", OnClick("primary", "object.window"),
                SetFlag("a", true) + ", " + Dialogue("dialogue.hello") + ", " + SetFlag("b", true)));
            h.OperationMode = FakeMode.Fail;

            h.Click(InteractionType.Primary, "object.window");

            Assert.That(h.Status("event.broken"), Is.EqualTo(EventStatus.Failed));
            Assert.That(h.Get("story.flags", "a").AsBoolean, Is.True, "applied mutation stays (Addendum §3)");
            Assert.That(h.Get("story.flags", "b").AsBoolean, Is.False, "nothing after the failure runs");
            Assert.That(h.TakeLog(), Is.EqualTo(new[] { "acquire", "dialogue:dialogue.hello", "release:Failed" }));
            Assert.That(h.Errors, Is.EqualTo(new[] { DiagnosticCode.OperationFailed }));
        }

        [Test]
        public void FailedEventIsBlockedAndNotHiddenByFallback()
        {
            var h = Create(Event("event.broken", "exclusive", "repeatable", OnClick("primary", "object.window"), Dialogue("dialogue.hello")));
            h.Click(InteractionType.Primary, "object.window");
            h.Complete(OperationOutcome.Failed);
            h.TakeLog();

            h.Click(InteractionType.Primary, "object.window");

            Assert.That(h.TakeLog(), Is.Empty, "neither the event nor a fallback response runs");
            Assert.That(h.Errors, Is.EqualTo(new[] { DiagnosticCode.OperationFailed, DiagnosticCode.EventBlockedByFailure }));
        }

        [Test]
        public void CancelledOperationFailsTheEvent()
        {
            var h = Create(Event("event.talk", "exclusive", "repeatable", OnClick("talk", "character.trinity"), Dialogue("dialogue.hello") + ", " + SetFlag("a", true)));
            h.Click(InteractionType.Talk, "character.trinity");

            h.Complete(OperationOutcome.Cancelled);

            Assert.That(h.Status("event.talk"), Is.EqualTo(EventStatus.Failed));
            Assert.That(h.Get("story.flags", "a").AsBoolean, Is.False, "cancellation is not completion");
            Assert.That(h.Errors, Is.EqualTo(new[] { DiagnosticCode.OperationCancelled }));
        }

        [Test]
        public void InventoryFailureFailsTheEvent()
        {
            var h = Create(Event("event.take", "exclusive", "once", OnClick("take", "item.key"), "{ 'action': 'inventory', 'item': 'item.key', 'to': 'inventory' }"));
            h.FailInventory = true;

            h.Click(InteractionType.Take, "item.key");

            Assert.That(h.Status("event.take"), Is.EqualTo(EventStatus.Failed));
            Assert.That(h.Errors, Is.EqualTo(new[] { DiagnosticCode.InventoryOperationFailed }));
        }

        // ================= actions =================

        [Test]
        public void IfTakesThenOrElse()
        {
            var actions = "{ 'action': 'if', 'condition': " + FlagIs("a", true) + ", 'then': [ " + SetFlag("b", true) + " ], 'else': [ " + SetFlag("c", true) + " ] }";
            var h = Create(Event("event.branch", "background", "repeatable", OnClick("primary", "object.window"), actions));

            h.Click(InteractionType.Primary, "object.window");
            Assert.That(h.Get("story.flags", "b").AsBoolean, Is.False);
            Assert.That(h.Get("story.flags", "c").AsBoolean, Is.True);

            h.SetSystem("story.flags", "a", StateValue.Boolean(true));
            h.Click(InteractionType.Primary, "object.window");
            Assert.That(h.Get("story.flags", "b").AsBoolean, Is.True);
        }

        [Test]
        public void EndFinishesEarlySuccessfully()
        {
            var actions = "{ 'action': 'if', 'condition': " + FlagIs("a", false) + ", 'then': [ " + SetFlag("b", true) + ", { 'action': 'end' } ] }, " + SetFlag("c", true);
            var h = Create(Event("event.early", "background", "repeatable", OnClick("primary", "object.window"), actions));

            h.Click(InteractionType.Primary, "object.window");

            Assert.That(h.Get("story.flags", "b").AsBoolean, Is.True);
            Assert.That(h.Get("story.flags", "c").AsBoolean, Is.False);
            Assert.That(h.Status("event.early"), Is.EqualTo(EventStatus.Completed));
        }

        [Test]
        public void AsyncActionsWaitForCompletionInOrder()
        {
            var actions =
                "{ 'action': 'move', 'character': 'character.paranoik', 'to': 'object.office_door' }," +
                "{ 'action': 'animation', 'target': 'character.paranoik', 'clip': 'open_door' }," +
                Dialogue("dialogue.hello") + "," +
                "{ 'action': 'transition', 'location': 'location.corridor', 'entry': 'from_office' }," +
                SetFlag("a", true);
            var h = Create(Event("event.leave", "exclusive", "repeatable", OnClick("exit", "object.office_door"), actions));

            h.Click(InteractionType.Exit, "object.office_door");
            Assert.That(h.TakeLog(), Is.EqualTo(new[] { "acquire", "move:character.paranoik->object.office_door" }));
            h.Complete();
            Assert.That(h.TakeLog(), Is.EqualTo(new[] { "anim:character.paranoik:open_door" }));
            h.Complete();
            Assert.That(h.TakeLog(), Is.EqualTo(new[] { "dialogue:dialogue.hello" }));
            h.Complete();
            Assert.That(h.TakeLog(), Is.EqualTo(new[] { "transition:location.corridor:from_office" }));
            Assert.That(h.Runtime.CurrentVisit, Is.EqualTo(1));
            Assert.That(h.Get("story.flags", "a").AsBoolean, Is.False);
            h.Complete();
            Assert.That(h.Get("story.flags", "a").AsBoolean, Is.True);
            Assert.That(h.TakeLog(), Is.EqualTo(new[] { "release:Completed" }));
        }

        [Test]
        public void ImmediateOperationsContinueAtOnce()
        {
            var h = Create(Event("event.quick", "exclusive", "repeatable", OnClick("primary", "object.window"), Dialogue("dialogue.hello") + ", " + SetFlag("a", true)));
            h.OperationMode = FakeMode.Immediate;

            h.Click(InteractionType.Primary, "object.window");

            Assert.That(h.Get("story.flags", "a").AsBoolean, Is.True);
            Assert.That(h.Status("event.quick"), Is.EqualTo(EventStatus.Completed));
        }

        [Test]
        public void ClearSelectionReachesInventory()
        {
            var h = Create(Event("event.apply", "exclusive", "repeatable",
                "{ 'type': 'interaction', 'interaction': 'apply_item', 'target': 'object.office_door', 'item': 'item.key' }", "{ 'action': 'clear_selection' }"));

            h.Click(InteractionType.ApplyItem, "object.office_door", "item.key");

            Assert.That(h.TakeLog(), Is.EqualTo(new[] { "acquire", "clear_selection", "release:Completed" }));
        }

        // ================= CALL / START =================

        [Test]
        public void CallRunsNestedAndCallerContinues()
        {
            var h = Create(
                Event("event.sub", "exclusive", "repeatable", "", Dialogue("dialogue.hello") + ", " + SetFlag("a", true)),
                Event("event.main", "exclusive", "repeatable", OnClick("primary", "object.window"), Call("event.sub") + ", " + SetFlag("b", true)));

            h.Click(InteractionType.Primary, "object.window");
            Assert.That(h.Status("event.sub"), Is.EqualTo(EventStatus.Running));
            Assert.That(h.Status("event.main"), Is.EqualTo(EventStatus.Running));
            Assert.That(h.Get("story.flags", "b").AsBoolean, Is.False, "caller waits for the callee");

            h.Complete();

            Assert.That(h.Get("story.flags", "a").AsBoolean, Is.True);
            Assert.That(h.Get("story.flags", "b").AsBoolean, Is.True);
            Assert.That(h.Status("event.sub"), Is.EqualTo(EventStatus.Completed));
            Assert.That(h.TakeLog(), Is.EqualTo(new[] { "acquire", "dialogue:dialogue.hello", "release:Completed" }), "one control acquisition for the whole chain");
        }

        [Test]
        public void CalleeFailureFailsCaller()
        {
            var h = Create(
                Event("event.sub", "exclusive", "repeatable", "", Dialogue("dialogue.hello")),
                Event("event.main", "exclusive", "repeatable", OnClick("primary", "object.window"), Call("event.sub") + ", " + SetFlag("b", true)));

            h.Click(InteractionType.Primary, "object.window");
            h.Complete(OperationOutcome.Failed);

            Assert.That(h.Status("event.sub"), Is.EqualTo(EventStatus.Failed));
            Assert.That(h.Status("event.main"), Is.EqualTo(EventStatus.Failed));
            Assert.That(h.Get("story.flags", "b").AsBoolean, Is.False);
            Assert.That(h.HoldsControl, Is.False);
        }

        [Test]
        public void CallOfExhaustedOnceEventFailsCaller()
        {
            var h = Create(
                Event("event.sub", "background", "once", "", SetFlag("a", true)),
                Event("event.main", "exclusive", "repeatable", OnClick("primary", "object.window"), Call("event.sub") + ", " + SetFlag("b", true)));

            h.Click(InteractionType.Primary, "object.window");
            Assert.That(h.Status("event.main"), Is.EqualTo(EventStatus.Completed));

            h.Click(InteractionType.Primary, "object.window");

            Assert.That(h.Status("event.main"), Is.EqualTo(EventStatus.Failed));
            Assert.That(h.Errors, Is.EqualTo(new[] { DiagnosticCode.CallUnavailable }));
        }

        [Test]
        public void CallWithFalseLaunchConditionFailsCaller()
        {
            var h = Create(
                Event("event.sub", "background", "repeatable", "", SetFlag("a", true), ", 'launch_condition': " + FlagIs("c", true)),
                Event("event.main", "exclusive", "repeatable", OnClick("primary", "object.window"), Call("event.sub")));

            h.Click(InteractionType.Primary, "object.window");

            Assert.That(h.Status("event.main"), Is.EqualTo(EventStatus.Failed));
            Assert.That(h.Status("event.sub"), Is.EqualTo(EventStatus.Idle));
            Assert.That(h.Errors, Is.EqualTo(new[] { DiagnosticCode.CallUnavailable }));
        }

        [Test]
        public void StartBackgroundRunsImmediately()
        {
            var h = Create(
                Event("event.bg", "background", "repeatable", "", SetFlag("a", true)),
                Event("event.main", "exclusive", "repeatable", OnClick("primary", "object.window"),
                    StartEvent("event.bg") + ", { 'action': 'if', 'condition': " + FlagIs("a", true) + ", 'then': [ " + SetFlag("b", true) + " ] }"));

            h.Click(InteractionType.Primary, "object.window");

            Assert.That(h.Get("story.flags", "b").AsBoolean, Is.True, "background completed before the next action");
        }

        [Test]
        public void StartExclusiveRunsAfterCurrentChain()
        {
            var h = Create(
                Event("event.next", "exclusive", "repeatable", "", Dialogue("dialogue.bye")),
                Event("event.main", "exclusive", "repeatable", OnClick("primary", "object.window"), StartEvent("event.next") + ", " + Dialogue("dialogue.hello")));

            h.Click(InteractionType.Primary, "object.window");
            Assert.That(h.TakeLog(), Is.EqualTo(new[] { "acquire", "dialogue:dialogue.hello" }));

            h.Complete();
            Assert.That(h.TakeLog(), Is.EqualTo(new[] { "release:Completed", "acquire", "dialogue:dialogue.bye" }));
        }

        [Test]
        public void StartOfUnavailableEventFailsCaller()
        {
            var h = Create(
                Event("event.bg", "background", "repeatable", "", SetFlag("a", true), ", 'launch_condition': " + FlagIs("c", true)),
                Event("event.main", "exclusive", "repeatable", OnClick("primary", "object.window"), StartEvent("event.bg") + ", " + SetFlag("b", true)));

            h.Click(InteractionType.Primary, "object.window");

            Assert.That(h.Status("event.main"), Is.EqualTo(EventStatus.Failed));
            Assert.That(h.Get("story.flags", "b").AsBoolean, Is.False);
            Assert.That(h.Errors, Is.EqualTo(new[] { DiagnosticCode.StartUnavailable }));
        }

        [Test]
        public void CallDepthIsLimited()
        {
            var h = ScenarioHarness.Create(new ScenarioLimits(2, 64), With(World,
                Event("event.c", "background", "repeatable", "", SetFlag("a", true)),
                Event("event.b", "background", "repeatable", "", Call("event.c")),
                Event("event.a", "exclusive", "repeatable", OnClick("primary", "object.window"), Call("event.b"))));

            h.Click(InteractionType.Primary, "object.window");

            Assert.That(h.Status("event.a"), Is.EqualTo(EventStatus.Failed));
            Assert.That(h.Get("story.flags", "a").AsBoolean, Is.False);
            Assert.That(h.Errors, Is.EqualTo(new[] { DiagnosticCode.CallDepthExceeded }));
        }

        // ================= execution & arbitration =================

        [Test]
        public void AutomaticExclusiveWaitsInQueue()
        {
            var h = Create(
                Event("event.scene", "exclusive", "repeatable", OnClick("primary", "object.window"), Dialogue("dialogue.hello")),
                Event("event.auto", "exclusive", "repeatable", "{ 'type': 'state_changed', 'target': 'story.flags', 'property': 'a' }", SetFlag("b", true)));

            h.Click(InteractionType.Primary, "object.window");
            h.SetSystem("story.flags", "a", StateValue.Boolean(true));
            Assert.That(h.Runtime.QueuedExclusiveCount, Is.EqualTo(1));
            Assert.That(h.Get("story.flags", "b").AsBoolean, Is.False);

            h.Complete();

            Assert.That(h.Get("story.flags", "b").AsBoolean, Is.True);
            Assert.That(h.Runtime.QueuedExclusiveCount, Is.Zero);
        }

        [Test]
        public void QueuedRequestIsDroppedWhenConditionNoLongerHolds()
        {
            var h = Create(
                Event("event.scene", "exclusive", "repeatable", OnClick("primary", "object.window"), Dialogue("dialogue.hello")),
                Event("event.auto", "exclusive", "repeatable", "{ 'type': 'state_changed', 'target': 'story.flags', 'property': 'a' }", SetFlag("b", true),
                    ", 'launch_condition': " + FlagIs("a", true)));

            h.Click(InteractionType.Primary, "object.window");
            h.SetSystem("story.flags", "a", StateValue.Boolean(true));
            h.SetSystem("story.flags", "a", StateValue.Boolean(false));

            h.Complete();

            Assert.That(h.Get("story.flags", "b").AsBoolean, Is.False);
            Assert.That(h.Runtime.QueuedExclusiveCount, Is.Zero);
            h.AssertNoDiagnostics();
        }

        [Test]
        public void PlayerActionDuringExclusiveIsRejected()
        {
            var h = Create(
                Event("event.scene", "exclusive", "repeatable", OnClick("primary", "object.window"), Dialogue("dialogue.hello")),
                Event("event.other", "exclusive", "repeatable", OnClick("exit", "object.office_door"), SetFlag("a", true)));

            h.Click(InteractionType.Primary, "object.window");
            h.Click(InteractionType.Exit, "object.office_door");

            Assert.That(h.Get("story.flags", "a").AsBoolean, Is.False);
            Assert.That(h.Runtime.QueuedExclusiveCount, Is.Zero, "player requests are never queued");
            Assert.That(h.Warnings, Is.EqualTo(new[] { DiagnosticCode.PlayerActionRejected }));
        }

        [Test]
        public void QueueWaitsWhileGameFlowRefusesControl()
        {
            var h = Create(Event("event.intro", "exclusive", "once", "{ 'type': 'location_entered' }", SetFlag("a", true)));
            h.AllowControl = false;

            h.Runtime.BeginLocationVisit(EntityId.Parse("location.office"));
            Assert.That(h.Runtime.QueuedExclusiveCount, Is.EqualTo(1));
            Assert.That(h.Get("story.flags", "a").AsBoolean, Is.False);

            h.AllowControl = true;
            h.Runtime.Pump();

            Assert.That(h.Get("story.flags", "a").AsBoolean, Is.True);
        }

        [Test]
        public void TwoEligibleScenesForOnePlayerActionIsError()
        {
            var h = Create(
                Event("event.one", "exclusive", "repeatable", OnClick("primary", "object.window"), SetFlag("a", true)),
                Event("event.two", "exclusive", "repeatable", OnClick("primary", "object.window"), SetFlag("b", true)));

            h.Click(InteractionType.Primary, "object.window");

            Assert.That(h.Get("story.flags", "a").AsBoolean, Is.False);
            Assert.That(h.Get("story.flags", "b").AsBoolean, Is.False);
            Assert.That(h.TakeLog(), Is.Empty);
            Assert.That(h.Errors, Is.EqualTo(new[] { DiagnosticCode.AmbiguousPlayerAction }));
        }

        [Test]
        public void MutuallyExclusiveScenesOnOneObjectAreFine()
        {
            var h = Create(
                Event("event.locked", "exclusive", "repeatable", OnClick("exit", "object.office_door"), Dialogue("dialogue.hello"), ", 'launch_condition': " + FlagIs("locked", true)),
                Event("event.open", "exclusive", "repeatable", OnClick("exit", "object.office_door"), SetFlag("a", true), ", 'launch_condition': " + FlagIs("locked", false)));

            h.Click(InteractionType.Exit, "object.office_door");

            Assert.That(h.Get("story.flags", "a").AsBoolean, Is.True);
            h.AssertNoDiagnostics();
        }

        [Test]
        public void CascadeLimitStopsEndlessReactions()
        {
            var h = ScenarioHarness.Create(new ScenarioLimits(8, 10), With(World,
                Event("event.ping", "background", "repeatable", "{ 'type': 'state_changed', 'target': 'story.flags', 'property': 'a', 'to': true }", SetFlag("a", false)),
                Event("event.pong", "background", "repeatable", "{ 'type': 'state_changed', 'target': 'story.flags', 'property': 'a', 'to': false }", SetFlag("a", true))));

            h.SetSystem("story.flags", "a", StateValue.Boolean(true));

            Assert.That(h.Errors, Is.EqualTo(new[] { DiagnosticCode.CascadeLimitExceeded }));
        }

        [Test]
        public void UnknownOperationCompletionIsContractViolation()
        {
            var h = Create();
            h.Runtime.CompleteOperation(new OperationHandle(999), OperationOutcome.Completed);
            Assert.That(h.Errors, Is.EqualTo(new[] { DiagnosticCode.OperationContractViolation }));
        }

        [Test]
        public void ExecutionIsDeterministic()
        {
            string[] Run()
            {
                var h = Create(
                    Event("event.z", "background", "repeatable", "{ 'type': 'location_entered' }", SetFlag("c", true)),
                    Event("event.y", "exclusive", "repeatable", "{ 'type': 'location_entered' }", Dialogue("dialogue.bye")),
                    Event("event.x", "exclusive", "repeatable", "{ 'type': 'location_entered' }", Dialogue("dialogue.hello") + ", " + StartEvent("event.z")));
                h.Runtime.BeginLocationVisit(EntityId.Parse("location.office"));
                h.CompleteAll();
                return h.Log.ToArray();
            }

            var first = Run();
            Assert.That(Run(), Is.EqualTo(first));
            Assert.That(first, Is.EqualTo(new[]
            {
                "acquire", "dialogue:dialogue.hello", "release:Completed", "acquire", "dialogue:dialogue.bye", "release:Completed",
            }));
        }
    }
}
