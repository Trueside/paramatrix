using System;
using System.Linq;
using NUnit.Framework;
using Paramatrix.Conditions;
using Paramatrix.Content;
using Paramatrix.Scenario;
using Paramatrix.State;
using static Paramatrix.Tests.Scenario.ScenarioHarness;

namespace Paramatrix.Tests.Scenario
{
    /// <summary>0.3.2 §11 Integration, Semantic Addendum §1 and §4, T6, T7.</summary>
    public sealed class MonitorAndSnapshotTests
    {
        private const string Window = "{ 'id': 'object.window', 'primary_interaction': 'primary' }";

        private static readonly string[] World = { Window, Flags("a", "b", "fired", "x"), Line("dialogue.hello", "Привет.") };

        private static ScenarioHarness Create(params string[] definitions) => ScenarioHarness.Create(With(World, definitions));

        /// <summary>Background event "fired = true" on conditions_met(a && b).</summary>
        private static string Watcher(string repeat = "repeatable") =>
            Event("event.watch", "background", repeat,
                "{ 'type': 'conditions_met', 'condition': { 'all': [ " + FlagIs("a", true) + ", " + FlagIs("b", true) + " ] } }",
                SetFlag("fired", true));

        private static void Reset(ScenarioHarness h) => h.SetSystem("story.flags", "fired", StateValue.Boolean(false));

        private static bool Fired(ScenarioHarness h) => h.Get("story.flags", "fired").AsBoolean;

        // ================= monitors =================

        [Test]
        public void FalseToTrueFires()
        {
            var h = Create(Watcher());

            h.SetSystem("story.flags", "a", StateValue.Boolean(true));
            Assert.That(Fired(h), Is.False);
            h.SetSystem("story.flags", "b", StateValue.Boolean(true));

            Assert.That(Fired(h), Is.True);
            h.AssertNoDiagnostics();
        }

        [Test]
        public void TrueToTrueDoesNotFireAgain()
        {
            var h = Create(Watcher());
            h.SetSystem("story.flags", "a", StateValue.Boolean(true));
            h.SetSystem("story.flags", "b", StateValue.Boolean(true));
            Reset(h);

            // A dependency changes, the condition stays true.
            h.SetSystem("story.flags", "b", StateValue.Boolean(true));
            h.SetSystem("story.flags", "a", StateValue.Boolean(true));

            Assert.That(Fired(h), Is.False);
        }

        [Test]
        public void TrueFalseTrueFiresAgainForRepeatable()
        {
            var h = Create(Watcher());
            h.SetSystem("story.flags", "a", StateValue.Boolean(true));
            h.SetSystem("story.flags", "b", StateValue.Boolean(true));
            Reset(h);

            h.SetSystem("story.flags", "b", StateValue.Boolean(false));
            Assert.That(Fired(h), Is.False);
            h.SetSystem("story.flags", "b", StateValue.Boolean(true));

            Assert.That(Fired(h), Is.True);
        }

        [Test]
        public void OnceEventFiresOnlyOnce()
        {
            var h = Create(Watcher("once"));
            h.SetSystem("story.flags", "a", StateValue.Boolean(true));
            h.SetSystem("story.flags", "b", StateValue.Boolean(true));
            Reset(h);

            h.SetSystem("story.flags", "b", StateValue.Boolean(false));
            h.SetSystem("story.flags", "b", StateValue.Boolean(true));

            Assert.That(Fired(h), Is.False);
        }

        [Test]
        public void AlreadyTrueAtStartDoesNotFire()
        {
            var flags = "{ 'id': 'story.flags', 'states': { " +
                        "'a': { 'type': 'boolean', 'default': true, 'persistence': 'persistent', 'mutation_policy': 'scenario_or_system' }, " +
                        "'b': { 'type': 'boolean', 'default': true, 'persistence': 'persistent', 'mutation_policy': 'scenario_or_system' }, " +
                        "'fired': { 'type': 'boolean', 'default': false, 'persistence': 'persistent', 'mutation_policy': 'scenario_or_system' } } }";
            var h = ScenarioHarness.Create(Window, flags, Watcher());

            h.Runtime.Pump();
            h.SetSystem("story.flags", "a", StateValue.Boolean(true));

            Assert.That(Fired(h), Is.False, "0.3.2 §8.2: a condition true at registration does not fire");
        }

        [Test]
        public void MonitorFollowsReferencedConditions()
        {
            var h = Create(
                "{ 'id': 'condition.both', 'condition': { 'all': [ " + FlagIs("a", true) + ", " + FlagIs("b", true) + " ] } }",
                Event("event.watch", "background", "repeatable", "{ 'type': 'conditions_met', 'condition': { 'ref': 'condition.both' } }", SetFlag("fired", true)));

            h.SetSystem("story.flags", "a", StateValue.Boolean(true));
            h.SetSystem("story.flags", "b", StateValue.Boolean(true));

            Assert.That(Fired(h), Is.True);
        }

        [Test]
        public void UnrelatedChangesDoNotEvaluateMonitor()
        {
            var h = Create(Watcher());
            h.SetSystem("story.flags", "x", StateValue.Boolean(true));
            Assert.That(Fired(h), Is.False);
            h.AssertNoDiagnostics();
        }

        [Test]
        public void ChangesAreProcessedBetweenActions()
        {
            // T1: the inbox is drained after every action, so each action's change is evaluated before the next action.
            var h = Create(
                Watcher(),
                Event("event.flip", "exclusive", "repeatable", OnClick("primary", "object.window"),
                    SetFlag("a", true) + ", " + SetFlag("b", true) + ", " + SetFlag("b", false)));

            h.Click(InteractionType.Primary, "object.window");

            // The action "b = true" ran, its change was processed before "b = false": the edge is seen.
            Assert.That(Fired(h), Is.True);
        }

        [Test]
        public void MonitorLaunchesExclusiveThroughQueue()
        {
            var h = Create(Event("event.scene", "exclusive", "once",
                "{ 'type': 'conditions_met', 'condition': " + FlagIs("a", true) + " }", Dialogue("dialogue.hello")));

            h.SetSystem("story.flags", "a", StateValue.Boolean(true));

            Assert.That(h.Status("event.scene"), Is.EqualTo(EventStatus.Running));
            Assert.That(h.TakeLog(), Is.EqualTo(new[] { "acquire", "dialogue:dialogue.hello" }));
        }

        // ================= CanSave / Export =================

        [Test]
        public void CanSaveOnlyWhenIdle()
        {
            var h = Create(
                Event("event.scene", "exclusive", "repeatable", OnClick("primary", "object.window"), Dialogue("dialogue.hello")),
                Event("event.auto", "exclusive", "repeatable", "{ 'type': 'state_changed', 'target': 'story.flags', 'property': 'x' }", SetFlag("a", true)));
            Assert.That(h.Runtime.CanSave, Is.True);

            h.Click(InteractionType.Primary, "object.window");
            Assert.That(h.Runtime.CanSave, Is.False, "active chain");
            Assert.Throws<InvalidOperationException>(() => h.Runtime.Export());

            h.State.Set(StateAddress.Parse("story.flags", "x"), StateValue.Boolean(true), MutationSource.System);
            Assert.That(h.Runtime.CanSave, Is.False, "unprocessed change");
            h.Runtime.Pump();
            Assert.That(h.Runtime.QueuedExclusiveCount, Is.EqualTo(1));

            h.Complete();
            Assert.That(h.Runtime.CanSave, Is.True);
        }

        [Test]
        public void ExportListsEveryEventSorted()
        {
            var h = Create(
                Event("event.b", "background", "once", OnClick("primary", "object.window"), SetFlag("a", true)),
                Event("event.a", "background", "once_per_location_visit", "{ 'type': 'location_entered' }", SetFlag("b", true)));
            h.Runtime.BeginLocationVisit(EntityId.Parse("location.office"));
            h.Click(InteractionType.Primary, "object.window");

            var snapshot = h.Runtime.Export();

            Assert.That(snapshot.CurrentVisit, Is.EqualTo(1));
            Assert.That(snapshot.Events.Select(e => e.Event.Value), Is.EqualTo(new[] { "event.a", "event.b" }));
            Assert.That(snapshot.Events[0].LastCompletedVisit, Is.EqualTo(1));
            Assert.That(snapshot.Events[1].EverCompleted, Is.True);
        }

        // ================= Save → Load =================

        private static readonly string[] SaveWorld =
        {
            Event("event.once", "background", "once", OnClick("primary", "object.window"), SetFlag("a", true)),
            Event("event.radio", "background", "once_per_location_visit", "{ 'type': 'location_entered' }," + OnClick("use", "object.window"), SetFlag("b", true)),
            Event("event.broken", "exclusive", "repeatable", OnClick("inspect", "object.window"), Dialogue("dialogue.hello")),
            Event("event.intro", "exclusive", "once", "{ 'type': 'location_entered' }," + "{ 'type': 'state_changed', 'target': 'story.flags', 'property': 'x' }", SetFlag("x", true)),
            Watcher(),
        };

        private static ScenarioHarness Played()
        {
            var h = Create(SaveWorld);
            h.Runtime.BeginLocationVisit(EntityId.Parse("location.office")); // radio + intro complete
            h.Click(InteractionType.Primary, "object.window");              // once completes
            h.Click(InteractionType.Inspect, "object.window");
            h.Complete(OperationOutcome.Failed);                             // broken fails
            h.SetSystem("story.flags", "a", StateValue.Boolean(true));
            h.SetSystem("story.flags", "b", StateValue.Boolean(true));       // watcher fired, condition true now
            Assert.That(h.Runtime.CanSave, Is.True);
            return h;
        }

        private static ScenarioHarness Load(ScenarioHarness saved)
        {
            var state = saved.State.ExportPersistent();
            var scenario = saved.Runtime.Export();

            var loaded = Create(SaveWorld);
            Assert.That(loaded.State.Restore(state, "slot").Succeeded, Is.True);
            var result = loaded.Runtime.Restore(scenario, "slot");
            Assert.That(result.Succeeded, Is.True, Content.TestContent.Describe(result.Diagnostics));
            loaded.Runtime.Pump();
            return loaded;
        }

        [Test]
        public void LoadRestoresHistoryWithoutTriggers()
        {
            var loaded = Load(Played());

            Assert.That(loaded.Log, Is.Empty, "no LocationEntered replay, no scene, no response");
            Assert.That(loaded.Runtime.CurrentVisit, Is.EqualTo(1));
            Assert.That(loaded.Status("event.once"), Is.EqualTo(EventStatus.Completed));
            Assert.That(loaded.Status("event.broken"), Is.EqualTo(EventStatus.Idle), "Failed is not saved (Р3)");
            loaded.AssertNoDiagnostics();
        }

        [Test]
        public void OnceStaysUsedAfterLoad()
        {
            var loaded = Load(Played());
            loaded.SetSystem("story.flags", "a", StateValue.Boolean(false));

            loaded.Click(InteractionType.Primary, "object.window");

            Assert.That(loaded.Get("story.flags", "a").AsBoolean, Is.False);
        }

        [Test]
        public void PerVisitStaysUsedInSameVisitAfterLoad()
        {
            var loaded = Load(Played());
            loaded.SetSystem("story.flags", "b", StateValue.Boolean(false));
            Reset(loaded);

            // Same Visit after Load: the radio must not run again (Addendum §1).
            loaded.Click(InteractionType.Use, "object.window");
            Assert.That(loaded.Get("story.flags", "b").AsBoolean, Is.False);
            loaded.CompleteAll();

            loaded.Runtime.BeginLocationVisit(EntityId.Parse("location.office"));
            Assert.That(loaded.Get("story.flags", "b").AsBoolean, Is.True, "a new ordinary entry starts a new Visit");
        }

        [Test]
        public void FailedSceneWorksAgainAfterLoad()
        {
            var loaded = Load(Played());

            loaded.Click(InteractionType.Inspect, "object.window");

            Assert.That(loaded.TakeLog(), Is.EqualTo(new[] { "acquire", "dialogue:dialogue.hello" }));
        }

        [Test]
        public void RestoreDoesNotCreateFalseMonitorEdge()
        {
            // Saved with a && b true; baseline after load is True, so true → true never fires.
            var loaded = Load(Played());
            Reset(loaded);

            loaded.SetSystem("story.flags", "a", StateValue.Boolean(true));

            Assert.That(Fired(loaded), Is.False);
            loaded.SetSystem("story.flags", "a", StateValue.Boolean(false));
            loaded.SetSystem("story.flags", "a", StateValue.Boolean(true));
            Assert.That(Fired(loaded), Is.True, "a real false → true after load still fires");
        }

        // ================= restore validation =================

        private static ScenarioRestoreResult RestoreModified(Func<ScenarioSnapshot, ScenarioSnapshot> modify, out ScenarioHarness target)
        {
            var saved = Played().Runtime.Export();
            target = Create(SaveWorld);
            target.Runtime.BeginLocationVisit(EntityId.Parse("location.office"));
            target.TakeLog();
            var before = target.Runtime.Export();

            var result = target.Runtime.Restore(modify(saved), "slot");

            Assert.That(result.Succeeded, Is.False);
            var after = target.Runtime.Export();
            Assert.That(after.CurrentVisit, Is.EqualTo(before.CurrentVisit), "failed restore changes nothing");
            Assert.That(after.Events.Select(e => (e.Event, e.EverCompleted, e.LastCompletedVisit)),
                Is.EqualTo(before.Events.Select(e => (e.Event, e.EverCompleted, e.LastCompletedVisit))));
            return result;
        }

        private static DiagnosticCode[] Codes(ScenarioRestoreResult result) => result.Diagnostics.Select(d => d.Code).ToArray();

        [Test]
        public void RestoreRejectsUnknownEvent()
        {
            var result = RestoreModified(s => new ScenarioSnapshot(s.CurrentVisit, s.Events.Append(new EventRecord(EntityId.Parse("event.removed"), true, 0))), out _);
            Assert.That(Codes(result), Is.EqualTo(new[] { DiagnosticCode.SnapshotUnknownEntity }));
        }

        [Test]
        public void RestoreRejectsMissingEvent()
        {
            var result = RestoreModified(s => new ScenarioSnapshot(s.CurrentVisit, s.Events.Where(e => e.Event.Value != "event.radio")), out _);
            Assert.That(Codes(result), Is.EqualTo(new[] { DiagnosticCode.SnapshotMissingEntry }));
        }

        [Test]
        public void RestoreRejectsDuplicateEvent()
        {
            var result = RestoreModified(s => new ScenarioSnapshot(s.CurrentVisit, s.Events.Append(s.Events[0])), out _);
            Assert.That(Codes(result), Is.EqualTo(new[] { DiagnosticCode.SnapshotDuplicateEntry }));
        }

        [Test]
        public void RestoreRejectsInconsistentRecords()
        {
            var result = RestoreModified(s => new ScenarioSnapshot(s.CurrentVisit, s.Events.Select(e =>
                e.Event.Value == "event.radio" ? new EventRecord(e.Event, false, 1) :
                e.Event.Value == "event.once" ? new EventRecord(e.Event, true, 7) : e)), out _);
            Assert.That(Codes(result), Is.EqualTo(new[] { DiagnosticCode.SnapshotInvalidValue, DiagnosticCode.SnapshotInvalidValue }));
        }

        [Test]
        public void RestoreRejectsNegativeVisit()
        {
            var result = RestoreModified(s => new ScenarioSnapshot(-1, s.Events.Select(e => new EventRecord(e.Event, false, -1))), out _);
            Assert.That(Codes(result), Is.EqualTo(new[] { DiagnosticCode.SnapshotInvalidValue }));
        }

        [Test]
        public void RestoreWhileBusyIsCodeError()
        {
            var h = Create(SaveWorld);
            var snapshot = h.Runtime.Export();
            h.Click(InteractionType.Inspect, "object.window");

            Assert.Throws<InvalidOperationException>(() => h.Runtime.Restore(snapshot, "slot"));
        }

        // ================= abort / clear =================

        [Test]
        public void AbortReleasesControlAndFailsChain()
        {
            var h = Create(
                Event("event.sub", "exclusive", "repeatable", "", Dialogue("dialogue.hello")),
                Event("event.main", "exclusive", "repeatable", OnClick("primary", "object.window"), Call("event.sub") + ", " + SetFlag("a", true)));
            h.Click(InteractionType.Primary, "object.window");
            h.TakeLog();

            h.Runtime.AbortActiveChain("dialogue view was destroyed");

            Assert.That(h.TakeLog(), Is.EqualTo(new[] { "release:Aborted" }));
            Assert.That(h.Status("event.main"), Is.EqualTo(EventStatus.Failed));
            Assert.That(h.Status("event.sub"), Is.EqualTo(EventStatus.Failed));
            Assert.That(h.Runtime.CanSave, Is.True);
            Assert.That(h.Errors, Is.EqualTo(new[] { DiagnosticCode.ChainAborted }));

            h.Complete(); // the late completion of the abandoned dialogue is ignored
            Assert.That(h.Get("story.flags", "a").AsBoolean, Is.False);
            Assert.That(h.Errors, Is.EqualTo(new[] { DiagnosticCode.ChainAborted }));
        }

        [Test]
        public void AbortResponse()
        {
            var h = Create();
            h.Click(InteractionType.Primary, "object.window");
            Assert.That(h.HoldsControl, Is.True);

            h.Runtime.AbortActiveChain("recovery");

            Assert.That(h.HoldsControl, Is.False);
            Assert.That(h.Errors, Is.EqualTo(new[] { DiagnosticCode.ChainAborted }));
        }

        [Test]
        public void AbortWithoutActiveChainDoesNothing()
        {
            var h = Create();
            h.Runtime.AbortActiveChain("nothing");
            h.AssertNoDiagnostics();
        }

        [Test]
        public void ClearPendingRequestsEmptiesQueue()
        {
            var h = Create(
                Event("event.scene", "exclusive", "repeatable", OnClick("primary", "object.window"), Dialogue("dialogue.hello")),
                Event("event.auto", "exclusive", "repeatable", "{ 'type': 'state_changed', 'target': 'story.flags', 'property': 'x' }", SetFlag("a", true)));
            h.Click(InteractionType.Primary, "object.window");
            h.SetSystem("story.flags", "x", StateValue.Boolean(true));
            Assert.That(h.Runtime.QueuedExclusiveCount, Is.EqualTo(1));

            h.Runtime.ClearPendingRequests();
            h.Complete();

            Assert.That(h.Runtime.QueuedExclusiveCount, Is.Zero);
            Assert.That(h.Get("story.flags", "a").AsBoolean, Is.False);
        }
    }
}
