using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Paramatrix.Content;
using Paramatrix.State;
using Paramatrix.Tests.Content;

namespace Paramatrix.Tests.State
{
    public sealed class StateSystemTests
    {
        private const string World =
            "{ 'id': 'object.office_door', 'primary_interaction': 'exit', 'states': {" +
            "  'open':   { 'type': 'boolean', 'default': false, 'persistence': 'persistent', 'mutation_policy': 'scenario_or_system' }," +
            "  'locked': { 'type': 'boolean', 'default': true,  'persistence': 'persistent', 'mutation_policy': 'scenario_only' }," +
            "  'charge': { 'type': 'integer', 'default': 0, 'min': 0, 'max': 3, 'persistence': 'persistent', 'mutation_policy': 'system_only' }," +
            "  'mood':   { 'type': 'enum', 'values': ['calm', 'alarmed'], 'default': 'calm', 'persistence': 'transient', 'mutation_policy': 'scenario_only' }," +
            "  'label':  { 'type': 'string', 'default': 'Офис', 'persistence': 'persistent', 'mutation_policy': 'scenario_only' } } }";

        private static readonly StateAddress Open = StateAddress.Parse("object.office_door", "open");
        private static readonly StateAddress Locked = StateAddress.Parse("object.office_door", "locked");
        private static readonly StateAddress Charge = StateAddress.Parse("object.office_door", "charge");
        private static readonly StateAddress Mood = StateAddress.Parse("object.office_door", "mood");
        private static readonly StateAddress Label = StateAddress.Parse("object.office_door", "label");

        private DiagnosticBag _diagnostics;
        private StateSystem _state;
        private List<StateChange> _changes;

        [SetUp]
        public void SetUp()
        {
            _diagnostics = new DiagnosticBag();
            _state = new StateSystem(TestContent.LoadValid(World), _diagnostics);
            _changes = new List<StateChange>();
            _state.Changed += _changes.Add;
        }

        [Test]
        public void StartsWithDefaults()
        {
            Assert.That(_state.Get(Open), Is.EqualTo(StateValue.Boolean(false)));
            Assert.That(_state.Get(Locked), Is.EqualTo(StateValue.Boolean(true)));
            Assert.That(_state.Get(Charge), Is.EqualTo(StateValue.Integer(0)));
            Assert.That(_state.Get(Mood), Is.EqualTo(StateValue.Enum("calm")));
            Assert.That(_state.Get(StateAddress.Parse("character.paranoik", "location")), Is.EqualTo(StateValue.Enum("location.office")));
            Assert.That(_changes, Is.Empty);
        }

        [Test]
        public void TryGetReportsUnknownEntityAndProperty()
        {
            Assert.That(_state.TryGet(Open, out var value), Is.EqualTo(StateReadStatus.Ok));
            Assert.That(value, Is.EqualTo(StateValue.Boolean(false)));
            Assert.That(_state.TryGet(StateAddress.Parse("object.window", "open"), out _), Is.EqualTo(StateReadStatus.UnknownEntity));
            Assert.That(_state.TryGet(StateAddress.Parse("object.office_door", "broken"), out _), Is.EqualTo(StateReadStatus.UnknownProperty));
            Assert.Throws<KeyNotFoundException>(() => _state.Get(StateAddress.Parse("object.office_door", "broken")));
        }

        [Test]
        public void AppliedChangeIsPublishedAfterCommit()
        {
            StateValue seenInHandler = default;
            _state.Changed += change => seenInHandler = _state.Get(change.Address);

            var result = _state.Set(Open, StateValue.Boolean(true), MutationSource.Scenario);

            Assert.That(result.Status, Is.EqualTo(MutationStatus.Applied));
            Assert.That(_state.Get(Open), Is.EqualTo(StateValue.Boolean(true)));
            Assert.That(seenInHandler, Is.EqualTo(StateValue.Boolean(true)), "handler must already see the committed value");
            Assert.That(_changes.Count, Is.EqualTo(1));
            Assert.That(_changes[0].Address, Is.EqualTo(Open));
            Assert.That(_changes[0].OldValue, Is.EqualTo(StateValue.Boolean(false)));
            Assert.That(_changes[0].NewValue, Is.EqualTo(StateValue.Boolean(true)));
            Assert.That(_changes[0].Source, Is.EqualTo(MutationSource.Scenario));
        }

        [Test]
        public void SameValueIsUnchangedAndSilent()
        {
            var result = _state.Set(Open, StateValue.Boolean(false), MutationSource.System);

            Assert.That(result.Status, Is.EqualTo(MutationStatus.Unchanged));
            Assert.That(_changes, Is.Empty);
        }

        private void AssertRejected(StateAddress address, StateValue value, MutationSource source, MutationError error)
        {
            var before = _state.ExportPersistent().Entries.Select(e => e.Value).ToArray();
            var result = _state.Set(address, value, source);

            Assert.That(result.Status, Is.EqualTo(MutationStatus.Rejected));
            Assert.That(result.Error, Is.EqualTo(error));
            Assert.That(_changes, Is.Empty, "rejected mutation must not publish");
            Assert.That(_state.ExportPersistent().Entries.Select(e => e.Value).ToArray(), Is.EqualTo(before), "rejected mutation must not change State");
        }

        [Test]
        public void UnknownEntityRejected() =>
            AssertRejected(StateAddress.Parse("object.window", "open"), StateValue.Boolean(true), MutationSource.Scenario, MutationError.UnknownEntity);

        [Test]
        public void UnknownPropertyRejected() =>
            AssertRejected(StateAddress.Parse("object.office_door", "broken"), StateValue.Boolean(true), MutationSource.Scenario, MutationError.UnknownProperty);

        [Test]
        public void NonStateEntityIsUnknown() =>
            AssertRejected(StateAddress.Parse("game.settings", "x"), StateValue.Boolean(true), MutationSource.System, MutationError.UnknownEntity);

        [Test]
        public void TypeMismatchRejected() =>
            AssertRejected(Open, StateValue.Integer(1), MutationSource.Scenario, MutationError.TypeMismatch);

        [Test]
        public void EnumAndStringAreDifferentTypes() =>
            AssertRejected(Mood, StateValue.String("calm"), MutationSource.Scenario, MutationError.TypeMismatch);

        [TestCase(-1)]
        [TestCase(4)]
        public void OutOfRangeRejected(int value) =>
            AssertRejected(Charge, StateValue.Integer(value), MutationSource.System, MutationError.OutOfRange);

        [Test]
        public void InvalidEnumRejected() =>
            AssertRejected(Mood, StateValue.Enum("angry"), MutationSource.Scenario, MutationError.InvalidEnumValue);

        [Test]
        public void ScenarioOnlyRejectsSystem() =>
            AssertRejected(Locked, StateValue.Boolean(false), MutationSource.System, MutationError.MutationNotAllowed);

        [Test]
        public void SystemOnlyRejectsScenario() =>
            AssertRejected(Charge, StateValue.Integer(1), MutationSource.Scenario, MutationError.MutationNotAllowed);

        [Test]
        public void ItemHolderIsSystemOnly()
        {
            var state = new StateSystem(
                TestContent.LoadValid("{ 'id': 'item.key', 'name': 'К', 'description': 'К.', 'initial_holder': 'location.office' }"),
                _diagnostics);
            var holder = StateAddress.Parse("item.key", "holder");

            Assert.That(state.Set(holder, StateValue.Enum("inventory"), MutationSource.Scenario).Error, Is.EqualTo(MutationError.MutationNotAllowed));
            Assert.That(state.Set(holder, StateValue.Enum("inventory"), MutationSource.System).Status, Is.EqualTo(MutationStatus.Applied));
            Assert.That(state.Set(holder, StateValue.Enum("location.roof"), MutationSource.System).Error, Is.EqualTo(MutationError.InvalidEnumValue));
        }

        [Test]
        public void ValidationOrderTypeBeforePolicy()
        {
            // Wrong type AND not allowed: §5.7 checks type first.
            Assert.That(_state.Set(Charge, StateValue.Boolean(true), MutationSource.Scenario).Error, Is.EqualTo(MutationError.TypeMismatch));
        }

        [Test]
        public void ScenarioOrSystemAcceptsBoth()
        {
            Assert.That(_state.Set(Open, StateValue.Boolean(true), MutationSource.Scenario).Status, Is.EqualTo(MutationStatus.Applied));
            Assert.That(_state.Set(Open, StateValue.Boolean(false), MutationSource.System).Status, Is.EqualTo(MutationStatus.Applied));
        }

        [Test]
        public void NestedSetIsCommittedButNotifiedInOrder()
        {
            var log = new List<string>();
            _state.Changed += change =>
            {
                log.Add("A:" + change.Address.Property);
                if (change.Address == Open)
                {
                    _state.Set(Label, StateValue.String("открыто"), MutationSource.Scenario);
                    log.Add("A:label committed=" + _state.Get(Label));
                }
            };
            _state.Changed += change => log.Add("B:" + change.Address.Property);

            _state.Set(Open, StateValue.Boolean(true), MutationSource.Scenario);

            Assert.That(log, Is.EqualTo(new[]
            {
                "A:open",
                "A:label committed=\"открыто\"",
                "B:open",
                "A:label",
                "B:label",
            }));
        }

        [Test]
        public void FailingSubscriberDoesNotUndoCommitOrStopOthers()
        {
            var second = 0;
            var state = new StateSystem(TestContent.LoadValid(World), _diagnostics);
            state.Changed += _ => throw new InvalidOperationException("boom");
            state.Changed += _ => second++;

            var result = state.Set(Open, StateValue.Boolean(true), MutationSource.Scenario);

            Assert.That(result.Status, Is.EqualTo(MutationStatus.Applied));
            Assert.That(state.Get(Open), Is.EqualTo(StateValue.Boolean(true)));
            Assert.That(second, Is.EqualTo(1));
            Assert.That(_diagnostics.Items.Single().Code, Is.EqualTo(DiagnosticCode.StateSubscriberFailed));
        }

        // ---------- export / restore ----------

        [Test]
        public void ExportContainsOnlyPersistentSorted()
        {
            var entries = _state.ExportPersistent().Entries;
            var addresses = entries.Select(e => e.Address.ToString()).ToArray();

            Assert.That(addresses, Does.Not.Contain("object.office_door.mood"));
            Assert.That(addresses, Does.Contain("object.office_door.open").And.Contain("character.paranoik.location"));
            Assert.That(addresses, Is.EqualTo(addresses.OrderBy(a => a, StringComparer.Ordinal).ToArray()));
        }

        [Test]
        public void RoundTripRestoresWithoutChangedNotifications()
        {
            _state.Set(Open, StateValue.Boolean(true), MutationSource.Scenario);
            _state.Set(Charge, StateValue.Integer(2), MutationSource.System);
            _state.Set(Mood, StateValue.Enum("alarmed"), MutationSource.Scenario);
            var snapshot = _state.ExportPersistent();

            var fresh = new StateSystem(TestContent.LoadValid(World), _diagnostics);
            var changes = new List<StateChange>();
            var restored = 0;
            fresh.Changed += changes.Add;
            fresh.Restored += () => restored++;

            var result = fresh.Restore(snapshot, "slot1.json");

            Assert.That(result.Succeeded, Is.True, TestContent.Describe(result.Diagnostics));
            Assert.That(fresh.Get(Open), Is.EqualTo(StateValue.Boolean(true)));
            Assert.That(fresh.Get(Charge), Is.EqualTo(StateValue.Integer(2)));
            Assert.That(fresh.Get(Mood), Is.EqualTo(StateValue.Enum("calm")), "transient State resets to default");
            Assert.That(changes, Is.Empty, "Restore must not publish State Changed");
            Assert.That(restored, Is.EqualTo(1));
        }

        [Test]
        public void RestoreResetsTransientOnSameInstance()
        {
            _state.Set(Mood, StateValue.Enum("alarmed"), MutationSource.Scenario);
            _changes.Clear();

            Assert.That(_state.Restore(_state.ExportPersistent(), "s").Succeeded, Is.True);
            Assert.That(_state.Get(Mood), Is.EqualTo(StateValue.Enum("calm")));
            Assert.That(_changes, Is.Empty);
        }

        private RestoreResult RestoreModified(Func<List<StateSnapshotEntry>, IEnumerable<StateSnapshotEntry>> modify)
        {
            _state.Set(Open, StateValue.Boolean(true), MutationSource.Scenario);
            var entries = _state.ExportPersistent().Entries.ToList();
            var before = _state.ExportPersistent().Entries.Select(e => e.Value).ToArray();
            var restoredCount = 0;
            _state.Restored += () => restoredCount++;
            _changes.Clear();

            var result = _state.Restore(new StateSnapshot(modify(entries)), "slot.json");

            Assert.That(result.Succeeded, Is.False);
            Assert.That(_state.ExportPersistent().Entries.Select(e => e.Value).ToArray(), Is.EqualTo(before), "failed restore must not change State");
            Assert.That(restoredCount, Is.EqualTo(0));
            Assert.That(_changes, Is.Empty);
            return result;
        }

        private static DiagnosticCode[] Codes(RestoreResult result) => result.Diagnostics.Select(d => d.Code).ToArray();

        [Test]
        public void RestoreRejectsUnknownEntity()
        {
            var result = RestoreModified(e => e.Append(new StateSnapshotEntry(StateAddress.Parse("object.window", "open"), StateValue.Boolean(true))));
            Assert.That(Codes(result), Is.EqualTo(new[] { DiagnosticCode.SnapshotUnknownEntity }));
        }

        [Test]
        public void RestoreRejectsUnknownProperty()
        {
            var result = RestoreModified(e => e.Append(new StateSnapshotEntry(StateAddress.Parse("object.office_door", "broken"), StateValue.Boolean(true))));
            Assert.That(Codes(result), Is.EqualTo(new[] { DiagnosticCode.SnapshotUnknownProperty }));
        }

        [Test]
        public void RestoreRejectsTransientEntry()
        {
            var result = RestoreModified(e => e.Append(new StateSnapshotEntry(Mood, StateValue.Enum("calm"))));
            Assert.That(Codes(result), Is.EqualTo(new[] { DiagnosticCode.SnapshotNotPersistent }));
        }

        [Test]
        public void RestoreRejectsDuplicateEntry()
        {
            var result = RestoreModified(e => e.Append(new StateSnapshotEntry(Open, StateValue.Boolean(false))));
            Assert.That(Codes(result), Is.EqualTo(new[] { DiagnosticCode.SnapshotDuplicateEntry }));
        }

        [Test]
        public void RestoreRejectsMissingEntry()
        {
            var result = RestoreModified(e => e.Where(x => x.Address != Locked));
            Assert.That(Codes(result), Is.EqualTo(new[] { DiagnosticCode.SnapshotMissingEntry }));
        }

        [TestCase("type")]
        [TestCase("range")]
        [TestCase("enum")]
        public void RestoreRejectsInvalidValue(string kind)
        {
            var result = RestoreModified(entries => entries.Select(e =>
            {
                if (kind == "type" && e.Address == Open) return new StateSnapshotEntry(Open, StateValue.Integer(1));
                if (kind == "range" && e.Address == Charge) return new StateSnapshotEntry(Charge, StateValue.Integer(9));
                if (kind == "enum" && e.Address.Property == "location") return new StateSnapshotEntry(e.Address, StateValue.Enum("location.roof"));
                return e;
            }));
            Assert.That(Codes(result), Is.EqualTo(new[] { DiagnosticCode.SnapshotInvalidValue }));
        }

        [Test]
        public void RestoreReportsAllProblemsAtOnce()
        {
            var result = RestoreModified(entries => entries
                .Where(x => x.Address != Locked)
                .Append(new StateSnapshotEntry(StateAddress.Parse("object.window", "open"), StateValue.Boolean(true))));
            Assert.That(Codes(result), Is.EquivalentTo(new[] { DiagnosticCode.SnapshotUnknownEntity, DiagnosticCode.SnapshotMissingEntry }));
        }

        [Test]
        public void RestoreFromHandlerIsForbidden()
        {
            var snapshot = _state.ExportPersistent();
            Exception caught = null;
            _state.Changed += _ =>
            {
                try { _state.Restore(snapshot, "s"); }
                catch (InvalidOperationException e) { caught = e; }
            };

            _state.Set(Open, StateValue.Boolean(true), MutationSource.Scenario);
            Assert.That(caught, Is.Not.Null);
        }
    }
}
