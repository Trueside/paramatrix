using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Paramatrix.Conditions;
using Paramatrix.Content;
using Paramatrix.Scenario;
using Paramatrix.State;
using Paramatrix.Tests.Content;

namespace Paramatrix.Tests.Scenario
{
    public enum FakeMode
    {
        Started = 1,
        Immediate,
        Fail,
    }

    /// <summary>
    /// Scenario test stand: real content, State, Conditions and ScenarioRuntime; fake Gameplay ports
    /// that record every call into <see cref="Log"/> and complete operations only when the test says so.
    /// </summary>
    internal sealed class ScenarioHarness : IGameFlowPort, IDialoguePort, IMovementPort, IAnimationPort, ILocationPort, IInventoryPort
    {
        private readonly List<OperationHandle> _pending = new List<OperationHandle>();
        private int _nextToken = 1;
        private ControlToken? _held;

        private ScenarioHarness(ContentRegistry registry, ScenarioLimits limits)
        {
            Registry = registry;
            Diagnostics = new DiagnosticBag();
            State = new StateSystem(registry, Diagnostics);
            Conditions = new ConditionEvaluator(registry, State);
            Runtime = new ScenarioRuntime(registry, State, Conditions, new ScenarioPorts(this, this, this, this, this, this), limits, Diagnostics);
            Runtime.Start();
        }

        public ContentRegistry Registry { get; }

        public DiagnosticBag Diagnostics { get; }

        public StateSystem State { get; }

        public ConditionEvaluator Conditions { get; }

        public ScenarioRuntime Runtime { get; }

        public List<string> Log { get; } = new List<string>();

        /// <summary>How async ports answer: Started (test completes later), Immediate or Fail.</summary>
        public FakeMode OperationMode { get; set; } = FakeMode.Started;

        public bool AllowControl { get; set; } = true;

        public bool FailInventory { get; set; }

        public bool HoldsControl => _held.HasValue;

        public int PendingOperations => _pending.Count;

        public static ScenarioHarness Create(params string[] definitions) => Create(ScenarioLimits.Default, definitions);

        public static ScenarioHarness Create(ScenarioLimits limits, params string[] definitions) =>
            new ScenarioHarness(TestContent.LoadValid(definitions), limits);

        // ---------- driving ----------

        public void Click(InteractionType interaction, string target, string item = null) =>
            Runtime.Submit(Trigger.PlayerInteraction(
                interaction,
                target == null ? (EntityId?)null : EntityId.Parse(target),
                item == null ? (EntityId?)null : EntityId.Parse(item)));

        /// <summary>Completes the oldest pending operation.</summary>
        public void Complete(OperationOutcome outcome = OperationOutcome.Completed)
        {
            Assert.That(_pending, Is.Not.Empty, "no pending operation to complete");
            var handle = _pending[0];
            _pending.RemoveAt(0);
            Runtime.CompleteOperation(handle, outcome);
        }

        /// <summary>Completes pending operations until there are none (bounded).</summary>
        public void CompleteAll()
        {
            for (var guard = 0; _pending.Count > 0; guard++)
            {
                Assert.That(guard, Is.LessThan(100), "operations keep coming");
                Complete();
            }
        }

        public StateValue Get(string entity, string property) => State.Get(StateAddress.Parse(entity, property));

        /// <summary>A change made by a Gameplay System, followed by the next frame's Pump.</summary>
        public void SetSystem(string entity, string property, StateValue value)
        {
            Assert.That(State.Set(StateAddress.Parse(entity, property), value, MutationSource.System).Status, Is.Not.EqualTo(MutationStatus.Rejected));
            Runtime.Pump();
        }

        public EventStatus Status(string eventId) => Runtime.GetStatus(EntityId.Parse(eventId));

        public DiagnosticCode[] Errors => Diagnostics.Items.Where(d => d.Severity == Severity.Error).Select(d => d.Code).ToArray();

        public DiagnosticCode[] Warnings => Diagnostics.Items.Where(d => d.Severity == Severity.Warning).Select(d => d.Code).ToArray();

        public void AssertNoDiagnostics() =>
            Assert.That(Diagnostics.Items, Is.Empty, TestContent.Describe(Diagnostics.Items));

        public string[] TakeLog()
        {
            var copy = Log.ToArray();
            Log.Clear();
            return copy;
        }

        // ---------- IGameFlowPort ----------

        public bool TryAcquireScenarioControl(out ControlToken token)
        {
            token = default;
            if (!AllowControl)
            {
                Log.Add("acquire:refused");
                return false;
            }

            Assert.That(_held.HasValue, Is.False, "control acquired twice without release");
            token = new ControlToken(_nextToken++);
            _held = token;
            Log.Add("acquire");
            return true;
        }

        public void ReleaseScenarioControl(ControlToken token, ReleaseReason reason)
        {
            Assert.That(_held.HasValue && _held.Value.Equals(token), Is.True, "released control that is not held");
            _held = null;
            Log.Add("release:" + reason);
        }

        // ---------- operations ----------

        private OperationStart Answer(OperationHandle handle, string entry)
        {
            Log.Add(entry);
            switch (OperationMode)
            {
                case FakeMode.Immediate:
                    return OperationStart.CompletedImmediately;
                case FakeMode.Fail:
                    return OperationStart.Failed("fake failure");
                default:
                    _pending.Add(handle);
                    return OperationStart.Started;
            }
        }

        public OperationStart Show(OperationHandle handle, EntityId dialogue) => Answer(handle, "dialogue:" + dialogue);

        public OperationStart ShowLine(OperationHandle handle, EntityId speaker, string text) => Answer(handle, $"line:{speaker}:{text}");

        public OperationStart MoveTo(OperationHandle handle, EntityId character, EntityId target) => Answer(handle, $"move:{character}->{target}");

        public OperationStart Play(OperationHandle handle, EntityId target, string clip) => Answer(handle, $"anim:{target}:{clip}");

        public OperationStart Transition(OperationHandle handle, EntityId location, string entry)
        {
            // As the real Location System does: State, then the new Visit, then completion.
            if (OperationMode != FakeMode.Fail)
            {
                State.Set(new StateAddress(Registry.Game.PlayerCharacter, BuiltInStates.Location), StateValue.Enum(location.Value), MutationSource.System);
                Runtime.BeginLocationVisit(location);
            }

            return Answer(handle, $"transition:{location}:{entry ?? "default"}");
        }

        public InventoryResult MoveItem(EntityId item, string to)
        {
            Log.Add($"inventory:{item}->{to}");
            if (FailInventory)
            {
                return InventoryResult.Failure("fake failure");
            }

            var result = State.Set(new StateAddress(item, BuiltInStates.Holder), StateValue.Enum(to), MutationSource.System);
            if (result.Status == MutationStatus.Rejected)
            {
                return InventoryResult.Failure(result.ToString());
            }

            if (to == BuiltInStates.Inventory && result.Status == MutationStatus.Applied)
            {
                Runtime.Submit(Trigger.ItemAcquired(item));
            }

            return InventoryResult.Success;
        }

        public void ClearSelection() => Log.Add("clear_selection");

        // ---------- content helpers ----------

        public static string Event(string id, string execution, string repeat, string triggers, string actions, string extra = "") =>
            "{ 'id': '" + id + "', 'execution': '" + execution + "', 'repeat': '" + repeat + "', 'triggers': [" + triggers + "], 'actions': [" + actions + "]" + extra + " }";

        public static string OnClick(string interaction, string target) =>
            "{ 'type': 'interaction', 'interaction': '" + interaction + "', 'target': '" + target + "' }";

        public static string SetFlag(string property, bool value) =>
            "{ 'action': 'set_state', 'target': 'story.flags', 'property': '" + property + "', 'value': " + (value ? "true" : "false") + " }";

        public static string Dialogue(string id) => "{ 'action': 'dialogue', 'dialogue': '" + id + "' }";

        public static string Call(string id) => "{ 'action': 'call', 'event': '" + id + "' }";

        public static string StartEvent(string id) => "{ 'action': 'start', 'event': '" + id + "' }";

        public static string FlagIs(string property, bool value) =>
            "{ 'target': 'story.flags', 'property': '" + property + "', 'operator': 'equals', 'value': " + (value ? "true" : "false") + " }";

        public static string Flags(params string[] names) =>
            "{ 'id': 'story.flags', 'states': { " +
            string.Join(", ", names.Select(n => "'" + n + "': { 'type': 'boolean', 'default': false, 'persistence': 'persistent', 'mutation_policy': 'scenario_or_system' }")) +
            " } }";

        public static string Line(string id, string text) =>
            "{ 'id': '" + id + "', 'mode': 'minimal', 'lines': [ { 'speaker': 'character.paranoik', 'text': '" + text + "' } ] }";

        public static string[] With(string[] common, params string[] more) => common.Concat(more).ToArray();
    }
}
