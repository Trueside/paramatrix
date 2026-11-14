using System;
using System.Collections.Generic;
using Paramatrix.Conditions;
using Paramatrix.Content;
using Paramatrix.State;

namespace Paramatrix.Scenario
{
    /// <summary>Status of the current or last execution of an event (Semantic Addendum §1).</summary>
    public enum EventStatus
    {
        Idle = 1,
        Running,
        Completed,
        Failed,
    }

    /// <summary>
    /// The only executor of scenarios (Architecture 0.4). Single-threaded and deterministic:
    /// work arrives in an inbox (triggers, State changes, operation completions) and is processed by
    /// <see cref="Pump"/>, which runs one instruction of the active exclusive chain, then drains the inbox,
    /// then starts the next queued exclusive event, until nothing is left (T1).
    /// </summary>
    public sealed class ScenarioRuntime
    {
        private readonly ContentRegistry _registry;
        private readonly StateSystem _state;
        private readonly ConditionEvaluator _conditions;
        private readonly ScenarioPorts _ports;
        private readonly ScenarioLimits _limits;
        private readonly IDiagnosticsSink _diagnostics;

        private readonly List<EventRuntime> _events = new List<EventRuntime>();
        private readonly Dictionary<EntityId, int> _eventIndex = new Dictionary<EntityId, int>();
        private readonly Dictionary<TriggerType, List<(int Event, TriggerDefinition Trigger)>> _triggerIndex =
            new Dictionary<TriggerType, List<(int, TriggerDefinition)>>();

        /// <summary>Said by the system after the last stub of a round (Р10): the player has heard everything.</summary>
        public const string EndOfStubs = "…";

        private readonly List<ResponseDefinition> _responses = new List<ResponseDefinition>();

        /// <summary>Lines already said of every prepared monologue (Р10); saved.</summary>
        private readonly Dictionary<EntityId, int> _spoken = new Dictionary<EntityId, int>();

        /// <summary>
        /// Per stub pool (interaction + target, or the common pool): the stub said last, or null when the round was just
        /// closed with "…". No entry: the round starts from the first stub. Lookups only; never enumerated.
        /// </summary>
        private readonly Dictionary<string, EntityId?> _lastStub = new Dictionary<string, EntityId?>(StringComparer.Ordinal);

        /// <summary>
        /// The prepared monologue whose line the hero said last, while nothing else was said by him since: the thought
        /// <see cref="ContinueThought"/> moves on. Not saved: it lives as long as the bubble.
        /// </summary>
        private EntityId? _thought;

        private readonly Queue<InboxItem> _inbox = new Queue<InboxItem>();
        private readonly List<int> _exclusiveQueue = new List<int>();

        private readonly List<Monitor> _monitors = new List<Monitor>();
        private readonly Dictionary<StateAddress, List<int>> _monitorsByAddress = new Dictionary<StateAddress, List<int>>();
        private readonly HashSet<OperationHandle> _abandoned = new HashSet<OperationHandle>();

        private Chain _active;
        private bool _started;
        private bool _pumping;
        private int _nextHandle = 1;
        private int _launchesThisCascade;
        private bool _cascadeTripped;

        public ScenarioRuntime(
            ContentRegistry registry,
            StateSystem state,
            ConditionEvaluator conditions,
            ScenarioPorts ports,
            ScenarioLimits limits,
            IDiagnosticsSink diagnostics)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
            _ports = ports ?? throw new ArgumentNullException(nameof(ports));
            _limits = limits ?? throw new ArgumentNullException(nameof(limits));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        /// <summary>Current Location Visit number; incremented by every ordinary location entry (T4).</summary>
        public int CurrentVisit { get; private set; }

        public bool HasActiveChain => _active != null;

        public int QueuedExclusiveCount => _exclusiveQueue.Count;

        /// <summary>
        /// Compiles programs and builds indexes. Produces no triggers: initialization is not gameplay (0.3.1 §7).
        /// </summary>
        public void Start()
        {
            if (_started)
            {
                throw new InvalidOperationException("ScenarioRuntime is already started.");
            }

            _started = true;

            foreach (var definition in _registry.GetByKind(EntityKind.Event))
            {
                var ev = (EventDefinition)definition;
                _eventIndex.Add(ev.Id, _events.Count);
                _events.Add(new EventRuntime(ev, ProgramCompiler.Compile(ev.Actions)));
            }

            for (var i = 0; i < _events.Count; i++)
            {
                foreach (var trigger in _events[i].Definition.Triggers)
                {
                    if (!_triggerIndex.TryGetValue(trigger.Type, out var list))
                    {
                        _triggerIndex.Add(trigger.Type, list = new List<(int, TriggerDefinition)>());
                    }

                    list.Add((i, trigger));
                }
            }

            foreach (var definition in _registry.GetByKind(EntityKind.Response))
            {
                var response = (ResponseDefinition)definition;
                _responses.Add(response);
                if (response.IsPrepared)
                {
                    _spoken.Add(response.Id, 0);
                }
            }

            // ConditionsMet monitors (0.3.2 §8, Addendum §4): one per trigger, in event ID order,
            // indexed by every State address the condition reads (T2).
            for (var i = 0; i < _events.Count; i++)
            {
                foreach (var trigger in _events[i].Definition.Triggers)
                {
                    if (trigger.Type != TriggerType.ConditionsMet)
                    {
                        continue;
                    }

                    var monitorIndex = _monitors.Count;
                    _monitors.Add(new Monitor(i, trigger.Condition));
                    foreach (var address in _conditions.GetDependencies(trigger.Condition))
                    {
                        if (!_monitorsByAddress.TryGetValue(address, out var list))
                        {
                            _monitorsByAddress.Add(address, list = new List<int>());
                        }

                        list.Add(monitorIndex);
                    }
                }
            }

            // A condition that is already true when monitoring begins does not fire.
            RecomputeBaselines();

            // Only enqueue: Scenario never processes anything inside SetState (0.3.1 §6.2). Pumping from here would
            // also run while State System is still dispatching, deferring further notifications past the end of the
            // pump and splitting one cascade into many — which would defeat the cascade limit (T3).
            // Changes made outside Scenario are processed by the next Pump (Unity calls it every frame).
            _state.Changed += change => _inbox.Enqueue(InboxItem.ForTrigger(Trigger.StateChanged(change)));
        }

        /// <summary>True when there is unprocessed work: a running step, inbox items or a startable queue head.</summary>
        public bool HasPendingWork => _inbox.Count > 0 || (_active != null && !_active.IsWaiting) || (_active == null && _exclusiveQueue.Count > 0);

        public EventStatus GetStatus(EntityId eventId) => _events[IndexOf(eventId)].Status;

        /// <summary>Lines of a prepared monologue already said (Р10).</summary>
        public int SpokenLines(EntityId response) =>
            _spoken.TryGetValue(response, out var spoken) ? spoken : throw new KeyNotFoundException($"{response} is not a prepared monologue.");

        /// <summary>
        /// Would this player action go to a scene rather than to a response? Decides whether the hero walks to the target
        /// first (T13): a scene is approached, a monologue answers from any distance (Р10). True also for an event that
        /// would be reported as blocked or erroneous: the full resolution then reports it.
        /// </summary>
        public bool WouldStartEvent(in Trigger trigger)
        {
            EnsureStarted();
            foreach (var index in Candidates(trigger))
            {
                var availability = Check(index).Availability;
                if (availability != Availability.Exhausted && availability != Availability.ConditionFalse)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Accepts a trigger from a Gameplay System and processes it.</summary>
        public void Submit(in Trigger trigger)
        {
            EnsureStarted();
            Enqueue(InboxItem.ForTrigger(trigger));
        }

        /// <summary>Ordinary location entry: a new Visit begins, then <c>location_entered</c> is published (T4).</summary>
        public void BeginLocationVisit(EntityId location)
        {
            EnsureStarted();
            CurrentVisit++;
            Enqueue(InboxItem.ForTrigger(Trigger.LocationEntered(location)));
        }

        /// <summary>Called by a port exactly once for every operation it reported as Started.</summary>
        public void CompleteOperation(OperationHandle handle, OperationOutcome outcome)
        {
            EnsureStarted();
            Enqueue(InboxItem.ForOperation(handle, outcome));
        }

        /// <summary>Processes everything that can be processed now. Re-entrant calls return immediately.</summary>
        public void Pump()
        {
            EnsureStarted();
            if (_pumping)
            {
                return;
            }

            _pumping = true;
            try
            {
                while (true)
                {
                    var progressed = false;

                    if (_active != null && !_active.IsWaiting)
                    {
                        Step(_active);
                        progressed = true;
                    }

                    while (_inbox.Count > 0)
                    {
                        Process(_inbox.Dequeue());
                        progressed = true;
                    }

                    if (_active == null && _exclusiveQueue.Count > 0)
                    {
                        progressed |= TryStartQueued();
                    }

                    if (!progressed)
                    {
                        break;
                    }
                }
            }
            finally
            {
                _pumping = false;
                _launchesThisCascade = 0;
                _cascadeTripped = false;
            }
        }

        // ======================================================================
        // save / load / recovery
        // ======================================================================

        /// <summary>
        /// Saving is possible only when Scenario is idle: no active chain, nothing queued, nothing unprocessed (T6).
        /// Active execution is never saved (0.4 §19).
        /// </summary>
        public bool CanSave => _started && !_pumping && _active == null && _exclusiveQueue.Count == 0 && _inbox.Count == 0;

        public ScenarioSnapshot Export()
        {
            EnsureStarted();
            if (!CanSave)
            {
                throw new InvalidOperationException("Scenario state can be exported only when CanSave is true.");
            }

            var records = new List<EventRecord>();
            foreach (var runtime in _events)
            {
                records.Add(new EventRecord(runtime.Definition.Id, runtime.EverCompleted, runtime.LastCompletedVisit));
            }

            var monologues = new List<MonologueRecord>();
            foreach (var response in _responses)
            {
                if (response.IsPrepared)
                {
                    monologues.Add(new MonologueRecord(response.Id, _spoken[response.Id]));
                }
            }

            return new ScenarioSnapshot(CurrentVisit, records, monologues);
        }

        /// <summary>
        /// Restores completion history and the current Visit, after State was restored. Checks the whole snapshot
        /// first (decision A15): it must describe exactly the events of current content. Restore creates no Visit and
        /// no triggers; Failed is not saved, so every event is available again by its repeat policy (Р3);
        /// monitor baselines are recomputed from the restored State.
        /// </summary>
        public ScenarioRestoreResult Restore(ScenarioSnapshot snapshot, string sourceName)
        {
            EnsureStarted();
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (!CanSave)
            {
                throw new InvalidOperationException("Scenario can be restored only when it is idle; abort the active execution first.");
            }

            var errors = Validate(snapshot, sourceName, out var records);
            if (errors.Count > 0)
            {
                return new ScenarioRestoreResult(errors);
            }

            CurrentVisit = snapshot.CurrentVisit;
            for (var i = 0; i < _events.Count; i++)
            {
                var record = records[i].Value;
                _events[i].EverCompleted = record.EverCompleted;
                _events[i].LastCompletedVisit = record.LastCompletedVisit;
                _events[i].Status = record.EverCompleted ? EventStatus.Completed : EventStatus.Idle;
            }

            foreach (var monologue in snapshot.Monologues)
            {
                _spoken[monologue.Response] = monologue.Spoken;
            }

            _lastStub.Clear();
            _thought = null;
            RecomputeBaselines();
            return new ScenarioRestoreResult(Array.Empty<Diagnostic>());
        }

        /// <summary>
        /// Checks a snapshot against current content without applying it, so that a load can validate
        /// State and Scenario snapshots together before changing anything.
        /// </summary>
        public IReadOnlyList<Diagnostic> ValidateSnapshot(ScenarioSnapshot snapshot, string sourceName)
        {
            EnsureStarted();
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return Validate(snapshot, sourceName, out _);
        }

        private IReadOnlyList<Diagnostic> Validate(ScenarioSnapshot snapshot, string sourceName, out EventRecord?[] records)
        {
            var location = new SourceLocation(sourceName ?? "<snapshot>", null, 0, 0);
            var errors = new DiagnosticBag();
            if (snapshot.CurrentVisit < 0)
            {
                errors.Error(DiagnosticCode.SnapshotInvalidValue, location, $"Saved visit number {snapshot.CurrentVisit} is negative.");
            }

            records = new EventRecord?[_events.Count];
            foreach (var record in snapshot.Events)
            {
                if (!_eventIndex.TryGetValue(record.Event, out var index))
                {
                    errors.Error(DiagnosticCode.SnapshotUnknownEntity, location, $"Saved event {record.Event} does not exist in current content.");
                    continue;
                }

                if (records[index].HasValue)
                {
                    errors.Error(DiagnosticCode.SnapshotDuplicateEntry, location, $"Event {record.Event} is saved twice.");
                    continue;
                }

                var consistent = record.LastCompletedVisit >= -1
                    && record.LastCompletedVisit <= snapshot.CurrentVisit
                    && (record.EverCompleted || record.LastCompletedVisit == -1);
                if (!consistent)
                {
                    errors.Error(DiagnosticCode.SnapshotInvalidValue, location,
                        $"Event {record.Event}: completed={record.EverCompleted}, last visit {record.LastCompletedVisit} does not fit current visit {snapshot.CurrentVisit}.");
                    continue;
                }

                records[index] = record;
            }

            for (var i = 0; i < _events.Count; i++)
            {
                if (!records[i].HasValue && _eventIndex.ContainsKey(_events[i].Definition.Id) && !Contains(snapshot, _events[i].Definition.Id))
                {
                    errors.Error(DiagnosticCode.SnapshotMissingEntry, location, $"Event {_events[i].Definition.Id} is missing from the save.");
                }
            }

            ValidateMonologues(snapshot, location, errors);
            return errors.Items;
        }

        /// <summary>The save must hold exactly the prepared monologues of current content, each within its line count (A15).</summary>
        private void ValidateMonologues(ScenarioSnapshot snapshot, SourceLocation location, DiagnosticBag errors)
        {
            var seen = new HashSet<EntityId>();
            foreach (var record in snapshot.Monologues)
            {
                if (!_registry.TryGet<ResponseDefinition>(record.Response, out var response))
                {
                    errors.Error(DiagnosticCode.SnapshotUnknownEntity, location, $"Saved monologue {record.Response} does not exist in current content.");
                    continue;
                }

                if (!seen.Add(record.Response))
                {
                    errors.Error(DiagnosticCode.SnapshotDuplicateEntry, location, $"Monologue {record.Response} is saved twice.");
                    continue;
                }

                if (!response.IsPrepared || record.Spoken < 0 || record.Spoken > response.Lines.Count)
                {
                    errors.Error(DiagnosticCode.SnapshotInvalidValue, location,
                        $"Monologue {record.Response}: {record.Spoken} lines said does not fit a prepared monologue of {(response.IsPrepared ? response.Lines.Count : 0)} lines.");
                }
            }

            foreach (var response in _responses)
            {
                if (response.IsPrepared && !seen.Contains(response.Id))
                {
                    errors.Error(DiagnosticCode.SnapshotMissingEntry, location, $"Monologue {response.Id} is missing from the save.");
                }
            }
        }

        /// <summary>
        /// Recovery from a hung execution (T7): the active chain's events become Failed, control is released with
        /// <see cref="ReleaseReason.Aborted"/>, a late completion of its pending operation is ignored.
        /// </summary>
        public void AbortActiveChain(string reason)
        {
            EnsureStarted();
            if (_pumping)
            {
                throw new InvalidOperationException("AbortActiveChain cannot be called while Scenario is processing.");
            }

            var chain = _active;
            if (chain == null)
            {
                return;
            }

            _active = null;
            if (chain.Pending.HasValue)
            {
                _abandoned.Add(chain.Pending.Value);
            }

            var root = _events[chain.Frames[0].Event].Definition;
            Report(DiagnosticCode.ChainAborted, Severity.Error, root.Source, $"Execution of {root.Id} was aborted: {reason}");

            foreach (var frame in chain.Frames)
            {
                _events[frame.Event].Status = EventStatus.Failed;
            }

            chain.Frames.Clear();
            chain.Pending = null;
            _ports.GameFlow.ReleaseScenarioControl(chain.Token.Value, ReleaseReason.Aborted);
        }

        /// <summary>Drops queued exclusive requests and unprocessed triggers (Loading, Recovery; 0.4 §7.4).</summary>
        public void ClearPendingRequests()
        {
            EnsureStarted();
            if (_pumping)
            {
                throw new InvalidOperationException("ClearPendingRequests cannot be called while Scenario is processing.");
            }

            _exclusiveQueue.Clear();
            var keep = new List<InboxItem>();
            foreach (var item in _inbox)
            {
                if (item.IsOperation)
                {
                    keep.Add(item);
                }
            }

            _inbox.Clear();
            foreach (var item in keep)
            {
                _inbox.Enqueue(item);
            }
        }

        private static bool Contains(ScenarioSnapshot snapshot, EntityId id)
        {
            foreach (var record in snapshot.Events)
            {
                if (record.Event == id)
                {
                    return true;
                }
            }

            return false;
        }

        // ======================================================================
        // inbox
        // ======================================================================

        private void Enqueue(InboxItem item)
        {
            _inbox.Enqueue(item);
            if (!_pumping)
            {
                Pump();
            }
        }

        private void Process(InboxItem item)
        {
            if (item.IsOperation)
            {
                OnOperationCompleted(item.Handle, item.Outcome);
                return;
            }

            var trigger = item.Trigger;
            var candidates = Candidates(trigger);
            if (trigger.Type == TriggerType.StateChanged)
            {
                MergeSorted(candidates, EvaluateMonitors(trigger.Change.Value.Address));
            }

            if (trigger.Origin == TriggerOrigin.Player && trigger.Type == TriggerType.Interaction)
            {
                ResolvePlayerAction(trigger, candidates);
            }
            else
            {
                foreach (var index in candidates)
                {
                    LaunchAutomatic(index);
                }
            }
        }

        /// <summary>Events whose trigger matched, each once, in Event ID order (A10).</summary>
        private List<int> Candidates(in Trigger trigger)
        {
            var result = new List<int>();
            if (!_triggerIndex.TryGetValue(trigger.Type, out var list))
            {
                return result;
            }

            foreach (var (index, definition) in list)
            {
                if ((result.Count == 0 || result[result.Count - 1] != index) && TriggerMatcher.Matches(definition, trigger))
                {
                    result.Add(index);
                }
            }

            return result;
        }

        /// <summary>
        /// Re-evaluates the monitors that read this address against current State.
        /// Returns events whose condition went False → True; every baseline is updated (T2).
        /// </summary>
        private List<int> EvaluateMonitors(StateAddress address)
        {
            var fired = new List<int>();
            if (!_monitorsByAddress.TryGetValue(address, out var affected))
            {
                return fired;
            }

            foreach (var monitorIndex in affected)
            {
                var monitor = _monitors[monitorIndex];
                var result = EvaluateMonitor(monitor);
                if (monitor.Previous == ConditionOutcome.False && result == ConditionOutcome.True && !fired.Contains(monitor.Event))
                {
                    fired.Add(monitor.Event);
                }

                monitor.Previous = result;
            }

            fired.Sort();
            return fired;
        }

        private ConditionOutcome EvaluateMonitor(Monitor monitor)
        {
            var result = _conditions.Evaluate(monitor.Condition);
            if (result.IsError)
            {
                // Error → True is not a False → True edge: it never fires.
                ReportConditionError(_events[monitor.Event].Definition.Source, _events[monitor.Event].Definition.Id, result);
            }

            return result.Outcome;
        }

        /// <summary>Baseline from current State, without firing (monitor registration, Restore).</summary>
        private void RecomputeBaselines()
        {
            foreach (var monitor in _monitors)
            {
                monitor.Previous = EvaluateMonitor(monitor);
            }
        }

        private static void MergeSorted(List<int> into, List<int> more)
        {
            foreach (var index in more)
            {
                var position = into.BinarySearch(index);
                if (position < 0)
                {
                    into.Insert(~position, index);
                }
            }
        }

        private void LaunchAutomatic(int index)
        {
            var check = Check(index);
            switch (check.Availability)
            {
                case Availability.Available:
                    if (_events[index].Definition.ExecutionType == ExecutionType.Background)
                    {
                        if (CountLaunch())
                        {
                            RunBackground(index);
                        }
                    }
                    else
                    {
                        EnqueueExclusive(index);
                    }

                    break;

                case Availability.Blocked:
                    ReportBlocked(index);
                    break;

                case Availability.ConditionError:
                    ReportConditionError(_events[index].Definition.Source, _events[index].Definition.Id, check.Condition);
                    break;

                // Running: a repeated request is ignored (0.4 §5). Exhausted / ConditionFalse: not eligible.
            }
        }

        private void EnqueueExclusive(int index)
        {
            if (!_exclusiveQueue.Contains(index))
            {
                _exclusiveQueue.Add(index);
            }
        }

        /// <summary>FIFO head: re-checked before launch (0.4 §7.2). Waits while Game Flow refuses control.</summary>
        private bool TryStartQueued()
        {
            var index = _exclusiveQueue[0];
            var check = Check(index);
            if (check.Availability != Availability.Available)
            {
                _exclusiveQueue.RemoveAt(0);
                if (check.Availability == Availability.Blocked)
                {
                    ReportBlocked(index);
                }
                else if (check.Availability == Availability.ConditionError)
                {
                    ReportConditionError(_events[index].Definition.Source, _events[index].Definition.Id, check.Condition);
                }

                return true;
            }

            if (!_ports.GameFlow.TryAcquireScenarioControl(out var token))
            {
                return false; // stays queued until Game Flow gives control
            }

            _exclusiveQueue.RemoveAt(0);
            if (!CountLaunch())
            {
                _ports.GameFlow.ReleaseScenarioControl(token, ReleaseReason.Aborted);
                return true;
            }

            StartExclusive(index, token);
            return true;
        }

        // ======================================================================
        // player action (A10, Р3, Р6)
        // ======================================================================

        private void ResolvePlayerAction(in Trigger trigger, List<int> candidates)
        {
            var eligible = new List<int>();
            foreach (var index in candidates)
            {
                var check = Check(index);
                switch (check.Availability)
                {
                    case Availability.Available:
                        eligible.Add(index);
                        break;
                    case Availability.Blocked:
                        // Р3 + Р0: a blocked scene is not "no scene"; never hide it behind a fallback response.
                        ReportBlocked(index);
                        return;
                    case Availability.ConditionError:
                        ReportConditionError(_events[index].Definition.Source, _events[index].Definition.Id, check.Condition);
                        return;
                }
            }

            if (eligible.Count > 1)
            {
                var names = new List<string>();
                foreach (var index in eligible)
                {
                    names.Add(_events[index].Definition.Id.Value);
                }

                Report(DiagnosticCode.AmbiguousPlayerAction, Severity.Error, SourceLocation.None,
                    $"Player action {trigger} matches several eligible events: {string.Join(", ", names)}. Nothing was started.");
                return;
            }

            if (eligible.Count == 1)
            {
                LaunchForPlayer(eligible[0], trigger);
                return;
            }

            ResolveResponse(trigger);
        }

        private void LaunchForPlayer(int index, in Trigger trigger)
        {
            if (_events[index].Definition.ExecutionType == ExecutionType.Background)
            {
                if (CountLaunch())
                {
                    RunBackground(index);
                }

                return;
            }

            // A player request is never queued (0.4 §7.3).
            if (_active != null || !_ports.GameFlow.TryAcquireScenarioControl(out var token))
            {
                Report(DiagnosticCode.PlayerActionRejected, Severity.Warning, _events[index].Definition.Source,
                    $"Player action {trigger} rejected: another exclusive execution holds control.");
                return;
            }

            if (!CountLaunch())
            {
                _ports.GameFlow.ReleaseScenarioControl(token, ReleaseReason.Aborted);
                return;
            }

            StartExclusive(index, token);
        }

        /// <summary>
        /// A player action without a scene (Р6, Р10): specific → contextual (a prepared monologue that is said out no longer
        /// matches) → stubs (the target's own pool, else the common pool; each round closes with "…") → fallback.
        /// Everything is said as a monologue: shown at once, nothing waits for it, no control is taken.
        /// </summary>
        private void ResolveResponse(in Trigger trigger)
        {
            var interaction = trigger.Interaction.Value;
            foreach (var tier in new[] { ResponseTier.Specific, ResponseTier.Contextual })
            {
                var matched = new List<ResponseDefinition>();
                foreach (var response in _responses)
                {
                    if (response.Tier != tier || !MatchesResponse(response, trigger) || IsSaidOut(response))
                    {
                        continue;
                    }

                    var condition = ResponseCondition(response);
                    if (condition == ConditionOutcome.Error)
                    {
                        return;
                    }

                    if (condition == ConditionOutcome.True)
                    {
                        matched.Add(response);
                    }
                }

                if (matched.Count > 1)
                {
                    Report(DiagnosticCode.AmbiguousResponse, Severity.Error, matched[1].Source,
                        $"Player action {trigger} matches several {tier} responses: {matched[0].Id}, {matched[1].Id}. Nothing was shown.");
                    return;
                }

                if (matched.Count == 1)
                {
                    SayResponse(matched[0], trigger);
                    return;
                }
            }

            // Stubs: the target's own pool first, the common pool only for a target without one (Р10).
            List<ResponseDefinition> pool = null;
            EntityId? poolTarget = null;
            if (trigger.Target.HasValue)
            {
                poolTarget = trigger.Target;
                if (!CollectStubs(trigger, poolTarget, out pool))
                {
                    return;
                }
            }

            if (pool == null || pool.Count == 0)
            {
                poolTarget = null;
                if (!CollectStubs(trigger, null, out pool))
                {
                    return;
                }
            }

            if (pool.Count > 0)
            {
                SayStub(interaction, poolTarget, pool, trigger);
                return;
            }

            foreach (var response in _responses)
            {
                if (response.Tier == ResponseTier.Fallback && response.Interaction == interaction)
                {
                    if (!response.IsNoReaction)
                    {
                        SayResponse(response, trigger);
                    }

                    return;
                }
            }

            // Content validation guarantees one fallback per interaction type.
            throw new InvalidOperationException($"No fallback response for '{InteractionTypes.Name(interaction)}'.");
        }

        private static bool MatchesResponse(ResponseDefinition response, in Trigger trigger)
        {
            if (response.Interaction != trigger.Interaction)
            {
                return false;
            }

            if (response.Target.HasValue && response.Target != trigger.Target)
            {
                return false;
            }

            return !response.Item.HasValue || response.Item == trigger.Item;
        }

        private bool IsSaidOut(ResponseDefinition response) => response.IsPrepared && _spoken[response.Id] >= response.Lines.Count;

        /// <summary>True / False of the response condition; Error is reported here.</summary>
        private ConditionOutcome ResponseCondition(ResponseDefinition response)
        {
            if (response.Condition == null)
            {
                return ConditionOutcome.True;
            }

            var result = _conditions.Evaluate(response.Condition);
            if (result.IsError)
            {
                ReportConditionError(response.Source, response.Id, result);
            }

            return result.Outcome;
        }

        /// <summary>
        /// Generic responses of one pool whose condition holds, in ID order: <paramref name="target"/> set — that target's
        /// own pool; null — the common pool (generic responses without a target). False when a condition failed.
        /// </summary>
        private bool CollectStubs(in Trigger trigger, EntityId? target, out List<ResponseDefinition> pool)
        {
            pool = new List<ResponseDefinition>();
            foreach (var response in _responses)
            {
                if (response.Tier != ResponseTier.Generic || response.Interaction != trigger.Interaction
                    || response.Target != target || (response.Item.HasValue && response.Item != trigger.Item))
                {
                    continue;
                }

                var condition = ResponseCondition(response);
                if (condition == ConditionOutcome.Error)
                {
                    return false;
                }

                if (condition == ConditionOutcome.True)
                {
                    pool.Add(response);
                }
            }

            return true;
        }

        /// <summary>
        /// Next stub of the round, in ID order; after the last one the system says "…", then the round starts over (Р10).
        /// </summary>
        private void SayStub(InteractionType interaction, EntityId? target, List<ResponseDefinition> pool, in Trigger trigger)
        {
            var key = InteractionTypes.Name(interaction) + "|" + (target.HasValue ? target.Value.Value : "*");
            ResponseDefinition next = null;
            if (_lastStub.TryGetValue(key, out var last) && last.HasValue)
            {
                foreach (var response in pool)
                {
                    if (response.Id.CompareTo(last.Value) > 0)
                    {
                        next = response;
                        break;
                    }
                }

                if (next == null)
                {
                    // The round is over: "…" is said by whoever said the stubs.
                    if (Say(SpeakerOf(pool[pool.Count - 1], trigger), EndOfStubs, pool[pool.Count - 1].Source))
                    {
                        _lastStub[key] = null;
                    }

                    return;
                }
            }
            else
            {
                next = pool[0]; // a new round: never started, or the last one was closed by "…"
            }

            if (Say(SpeakerOf(next, trigger), next.Lines[0], next.Source))
            {
                _lastStub[key] = next.Id;
            }
        }

        /// <summary>A text response, or the next line of a prepared monologue.</summary>
        private void SayResponse(ResponseDefinition response, in Trigger trigger)
        {
            var line = response.IsPrepared ? _spoken[response.Id] : 0;
            var speaker = SpeakerOf(response, trigger);
            if (Say(speaker, response.Lines[line], response.Source) && response.IsPrepared)
            {
                _spoken[response.Id] = line + 1;
                if (speaker == _registry.Game.PlayerCharacter)
                {
                    _thought = response.Id;
                }
            }
        }

        /// <summary>
        /// The player moves the hero's shown thought on (right click on empty space, Р10): the next line of the prepared
        /// monologue it came from, if one is left and its condition holds. False when there is none (the caller closes the
        /// bubble) or the line could not be said (reported).
        /// </summary>
        public bool ContinueThought()
        {
            var id = _thought;
            _thought = null;
            if (!id.HasValue)
            {
                return false;
            }

            var response = _responses.Find(r => r.Id == id.Value);
            if (IsSaidOut(response) || ResponseCondition(response) != ConditionOutcome.True)
            {
                return false;
            }

            var line = _spoken[response.Id];
            if (!Say(_registry.Game.PlayerCharacter, response.Lines[line], response.Source))
            {
                return false;
            }

            _spoken[response.Id] = line + 1;
            _thought = response.Id;
            return true;
        }

        private EntityId SpeakerOf(ResponseDefinition response, in Trigger trigger) =>
            response.Speaker
            ?? (trigger.Interaction == InteractionType.Talk && trigger.Target.HasValue && trigger.Target.Value.Kind == EntityKind.Character
                ? trigger.Target.Value
                : _registry.Game.PlayerCharacter);

        /// <summary>Shows a monologue line; a refusal is an Error (Р0). The line counts as said only when shown.</summary>
        private bool Say(EntityId speaker, string text, SourceLocation source)
        {
            var error = _ports.Dialogue.Say(speaker, text);
            if (error == null)
            {
                if (speaker == _registry.Game.PlayerCharacter)
                {
                    _thought = null; // set again by the caller when the line belongs to a prepared monologue
                }

                return true;
            }

            Report(DiagnosticCode.MonologueFailed, Severity.Error, source, $"{speaker} could not say \"{text}\": {error}");
            return false;
        }

        // ======================================================================
        // availability (Р3, Р4, repeat policies)
        // ======================================================================

        private enum Availability
        {
            Available = 1,
            Running,
            Blocked,
            Exhausted,
            ConditionFalse,
            ConditionError,
        }

        private readonly struct AvailabilityCheck
        {
            public AvailabilityCheck(Availability availability, ConditionResult condition)
            {
                Availability = availability;
                Condition = condition;
            }

            public Availability Availability { get; }

            public ConditionResult Condition { get; }
        }

        private AvailabilityCheck Check(int index)
        {
            var runtime = _events[index];
            if (runtime.Status == EventStatus.Running)
            {
                return new AvailabilityCheck(Availability.Running, default);
            }

            var exhausted = (runtime.Definition.RepeatPolicy == RepeatPolicy.Once && runtime.EverCompleted)
                || (runtime.Definition.RepeatPolicy == RepeatPolicy.OncePerLocationVisit && runtime.LastCompletedVisit == CurrentVisit);

            // Exhausted is checked before Failed: a completed once-event is simply done, not blocked.
            if (exhausted)
            {
                return new AvailabilityCheck(Availability.Exhausted, default);
            }

            var condition = runtime.Definition.LaunchCondition == null
                ? ConditionResult.True
                : _conditions.Evaluate(runtime.Definition.LaunchCondition);
            if (condition.IsError)
            {
                return new AvailabilityCheck(Availability.ConditionError, condition);
            }

            if (!condition.IsTrue)
            {
                return new AvailabilityCheck(Availability.ConditionFalse, condition);
            }

            // Blocked only matters when the event would otherwise run.
            return runtime.Status == EventStatus.Failed
                ? new AvailabilityCheck(Availability.Blocked, condition)
                : new AvailabilityCheck(Availability.Available, condition);
        }

        private string Describe(Availability availability)
        {
            switch (availability)
            {
                case Availability.Running: return "it is already running";
                case Availability.Blocked: return "it failed earlier and is blocked until a save is loaded";
                case Availability.Exhausted: return "its repeat policy is exhausted";
                case Availability.ConditionFalse: return "its launch condition is false";
                default: return availability.ToString();
            }
        }

        // ======================================================================
        // execution
        // ======================================================================

        private void StartExclusive(int index, ControlToken token)
        {
            var chain = Chain.ForEvent(ChainKind.Exclusive, token);
            PushFrame(chain, index);
            _active = chain;
        }

        /// <summary>Background events never wait (A6): they run to the end right now.</summary>
        private void RunBackground(int index)
        {
            var chain = Chain.ForEvent(ChainKind.Background, null);
            PushFrame(chain, index);
            while (chain.IsAlive)
            {
                if (chain.IsWaiting)
                {
                    // Excluded by content validation; reaching it is a defect, not a content problem.
                    throw new InvalidOperationException($"Background event {_events[index].Definition.Id} started an asynchronous operation.");
                }

                Step(chain);
            }
        }

        private bool PushFrame(Chain chain, int index)
        {
            if (chain.Frames.Count >= _limits.MaxCallDepth)
            {
                return false;
            }

            _events[index].Status = EventStatus.Running;
            chain.Frames.Add(new Frame(index, CurrentVisit));
            return true;
        }

        /// <summary>Executes one instruction of the chain's top frame.</summary>
        private void Step(Chain chain)
        {
            var frame = chain.Top;
            var runtime = _events[frame.Event];
            if (frame.Pc >= runtime.Program.Length)
            {
                CompleteTopFrame(chain);
                return;
            }

            var instruction = runtime.Program[frame.Pc];
            switch (instruction.Op)
            {
                case OpCode.End:
                    CompleteTopFrame(chain);
                    return;

                case OpCode.Jump:
                    frame.Pc = instruction.Target;
                    return;

                case OpCode.JumpIfFalse:
                    var result = _conditions.Evaluate(instruction.Condition);
                    if (result.IsError)
                    {
                        // An Error never takes the else branch (0.4 §9.1).
                        Fail(chain, DiagnosticCode.ConditionEvaluationFailed, instruction.Action.Source,
                            $"Condition of 'if' in {runtime.Definition.Id} failed: {result}");
                        return;
                    }

                    frame.Pc = result.IsTrue ? frame.Pc + 1 : instruction.Target;
                    return;
            }

            Execute(chain, frame, runtime, instruction.Action);
        }

        private void Execute(Chain chain, Frame frame, EventRuntime runtime, ActionDefinition action)
        {
            switch (action)
            {
                case SetStateAction set:
                    var mutation = _state.Set(set.Target, set.Value, MutationSource.Scenario);
                    if (mutation.Status == MutationStatus.Rejected)
                    {
                        Fail(chain, DiagnosticCode.MutationRejected, set.Source, $"{runtime.Definition.Id}: setting {set.Target} = {set.Value} was rejected ({mutation.Error}).");
                        return;
                    }

                    frame.Pc++;
                    return;

                case InventoryAction inventory:
                    var moved = _ports.Inventory.MoveItem(inventory.Item, inventory.To);
                    if (!moved.Succeeded)
                    {
                        Fail(chain, DiagnosticCode.InventoryOperationFailed, inventory.Source, $"{runtime.Definition.Id}: moving {inventory.Item} to {inventory.To} failed: {moved.FailureReason}");
                        return;
                    }

                    frame.Pc++;
                    return;

                case ClearSelectionAction _:
                    _ports.Inventory.ClearSelection();
                    frame.Pc++;
                    return;

                case SayAction say:
                    // A monologue line: the event never waits for it (Р10). A refusal fails the event (Р0).
                    var refused = _ports.Dialogue.Say(say.Speaker, say.Text);
                    if (refused == null && say.Speaker == _registry.Game.PlayerCharacter)
                    {
                        _thought = null; // the hero says something else: the bubble no longer shows that thought
                    }

                    if (refused != null)
                    {
                        Fail(chain, DiagnosticCode.MonologueFailed, say.Source, $"{runtime.Definition.Id}: {say.Speaker} could not say \"{say.Text}\": {refused}");
                        return;
                    }

                    frame.Pc++;
                    return;

                case CallAction call:
                    ExecuteCall(chain, frame, runtime, call);
                    return;

                case StartAction start:
                    ExecuteStart(chain, frame, runtime, start);
                    return;

                case DialogueAction _:
                case MoveAction _:
                case AnimationAction _:
                case TransitionAction _:
                    StartOperation(chain, frame, runtime, action);
                    return;

                default:
                    throw new InvalidOperationException($"Unsupported action {action.GetType().Name}.");
            }
        }

        private void ExecuteCall(Chain chain, Frame frame, EventRuntime runtime, CallAction call)
        {
            var target = IndexOf(call.Event);
            var check = Check(target);
            if (check.Availability != Availability.Available)
            {
                if (check.Availability == Availability.ConditionError)
                {
                    ReportConditionError(_events[target].Definition.Source, call.Event, check.Condition);
                }

                Fail(chain, DiagnosticCode.CallUnavailable, call.Source, $"{runtime.Definition.Id}: CALL {call.Event} is not possible: {Describe(check.Availability)}.");
                return;
            }

            if (!CountLaunch())
            {
                Fail(chain, DiagnosticCode.CascadeLimitExceeded, call.Source, $"{runtime.Definition.Id}: CALL {call.Event} refused by the cascade limit.");
                return;
            }

            frame.Pc++; // the caller resumes after the CALL once the callee completes
            if (!PushFrame(chain, target))
            {
                Fail(chain, DiagnosticCode.CallDepthExceeded, call.Source, $"{runtime.Definition.Id}: CALL {call.Event} exceeds the maximum depth {_limits.MaxCallDepth}.");
            }
        }

        private void ExecuteStart(Chain chain, Frame frame, EventRuntime runtime, StartAction start)
        {
            var target = IndexOf(start.Event);
            var check = Check(target);
            if (check.Availability != Availability.Available)
            {
                if (check.Availability == Availability.ConditionError)
                {
                    ReportConditionError(_events[target].Definition.Source, start.Event, check.Condition);
                }

                Fail(chain, DiagnosticCode.StartUnavailable, start.Source, $"{runtime.Definition.Id}: START {start.Event} is not possible: {Describe(check.Availability)}.");
                return;
            }

            frame.Pc++;
            if (_events[target].Definition.ExecutionType == ExecutionType.Exclusive)
            {
                EnqueueExclusive(target); // A5: runs after the current exclusive chain
                return;
            }

            if (!CountLaunch())
            {
                Fail(chain, DiagnosticCode.CascadeLimitExceeded, start.Source, $"{runtime.Definition.Id}: START {start.Event} refused by the cascade limit.");
                return;
            }

            RunBackground(target);
        }

        private void StartOperation(Chain chain, Frame frame, EventRuntime runtime, ActionDefinition action)
        {
            var handle = NewHandle();
            OperationStart start;
            switch (action)
            {
                case DialogueAction dialogue:
                    start = _ports.Dialogue.Show(handle, dialogue.Dialogue);
                    break;
                case MoveAction move:
                    start = _ports.Movement.MoveTo(handle, move.Character, move.To);
                    break;
                case AnimationAction animation:
                    start = _ports.Animation.Play(handle, animation.Target, animation.Clip);
                    break;
                default:
                    var transition = (TransitionAction)action;
                    start = _ports.Location.Transition(handle, transition.Location, transition.Entry);
                    break;
            }

            switch (start.Status)
            {
                case OperationStartStatus.Started:
                    chain.Pending = handle;
                    chain.PendingAction = action;
                    return;

                case OperationStartStatus.CompletedImmediately:
                    OnActionOperationCompleted(frame, action);
                    return;

                default:
                    Fail(chain, DiagnosticCode.OperationFailed, action.Source, $"{runtime.Definition.Id}: operation could not start: {start.FailureReason}");
                    return;
            }
        }

        private void OnOperationCompleted(OperationHandle handle, OperationOutcome outcome)
        {
            if (_abandoned.Remove(handle))
            {
                return; // its chain was aborted; the late completion carries no meaning
            }

            var chain = _active;
            if (chain == null || !chain.Pending.HasValue || !chain.Pending.Value.Equals(handle))
            {
                Report(DiagnosticCode.OperationContractViolation, Severity.Error, SourceLocation.None,
                    $"Operation {handle} completed ({outcome}) but nothing is waiting for it.");
                return;
            }

            var action = chain.PendingAction;
            chain.Pending = null;
            chain.PendingAction = null;

            if (outcome != OperationOutcome.Completed)
            {
                Fail(chain,
                    outcome == OperationOutcome.Cancelled ? DiagnosticCode.OperationCancelled : DiagnosticCode.OperationFailed,
                    action.Source,
                    $"{_events[chain.Top.Event].Definition.Id}: operation {handle} ended with {outcome}.");
                return;
            }

            OnActionOperationCompleted(chain.Top, action);
        }

        private void OnActionOperationCompleted(Frame frame, ActionDefinition action)
        {
            frame.Pc++;
            if (action is DialogueAction dialogue)
            {
                _inbox.Enqueue(InboxItem.ForTrigger(Trigger.DialogueCompleted(dialogue.Dialogue)));
            }
        }

        private void CompleteTopFrame(Chain chain)
        {
            var frame = chain.Top;
            chain.Frames.RemoveAt(chain.Frames.Count - 1);

            var runtime = _events[frame.Event];
            runtime.Status = EventStatus.Completed;
            runtime.EverCompleted = true;
            runtime.LastCompletedVisit = frame.VisitAtStart; // T4: the Visit in which it was launched
            _inbox.Enqueue(InboxItem.ForTrigger(Trigger.EventCompleted(runtime.Definition.Id)));

            if (chain.Frames.Count == 0 && chain.Kind == ChainKind.Exclusive)
            {
                _active = null;
                _ports.GameFlow.ReleaseScenarioControl(chain.Token.Value, ReleaseReason.Completed);
            }
        }

        /// <summary>
        /// Failure policy (0.4 §11.2, Addendum §3): every event of the chain becomes Failed, nothing continues,
        /// applied mutations stay, control is released, queued requests of these events are dropped.
        /// </summary>
        private void Fail(Chain chain, DiagnosticCode code, SourceLocation location, string message)
        {
            Report(code, Severity.Error, location, message);

            foreach (var frame in chain.Frames)
            {
                _events[frame.Event].Status = EventStatus.Failed;
                _exclusiveQueue.Remove(frame.Event);
            }

            chain.Frames.Clear();
            chain.Pending = null;
            chain.PendingAction = null;

            if (chain.Kind == ChainKind.Exclusive)
            {
                if (_active == chain)
                {
                    _active = null;
                }

                _ports.GameFlow.ReleaseScenarioControl(chain.Token.Value, ReleaseReason.Failed);
            }
        }

        // ======================================================================
        // helpers
        // ======================================================================

        /// <summary>Counts an event launch against the cascade limit (T3). False: refuse the launch.</summary>
        private bool CountLaunch()
        {
            if (_cascadeTripped)
            {
                return false;
            }

            _launchesThisCascade++;
            if (_launchesThisCascade <= _limits.MaxLaunchesPerCascade)
            {
                return true;
            }

            _cascadeTripped = true;
            _exclusiveQueue.Clear();
            Report(DiagnosticCode.CascadeLimitExceeded, Severity.Error, SourceLocation.None,
                $"More than {_limits.MaxLaunchesPerCascade} event launches in one cascade: events keep triggering each other. Automatic launches stopped.");
            return false;
        }

        private void ReportBlocked(int index)
        {
            var definition = _events[index].Definition;
            Report(DiagnosticCode.EventBlockedByFailure, Severity.Error, definition.Source,
                $"{definition.Id} would run now, but it failed earlier and stays blocked until a save is loaded (Р3).");
        }

        private void ReportConditionError(SourceLocation location, EntityId owner, ConditionResult result) =>
            Report(DiagnosticCode.ConditionEvaluationFailed, Severity.Error, location, $"Condition of {owner} failed: {result}");

        private void Report(DiagnosticCode code, Severity severity, SourceLocation location, string message) =>
            _diagnostics.Report(new Diagnostic(code, severity, message, location));

        private OperationHandle NewHandle() => new OperationHandle(_nextHandle++);

        private int IndexOf(EntityId eventId)
        {
            if (!_eventIndex.TryGetValue(eventId, out var index))
            {
                throw new KeyNotFoundException($"No event '{eventId}'.");
            }

            return index;
        }

        private void EnsureStarted()
        {
            if (!_started)
            {
                throw new InvalidOperationException("ScenarioRuntime.Start must be called first.");
            }
        }

        // ======================================================================
        // runtime records
        // ======================================================================

        private sealed class EventRuntime
        {
            public EventRuntime(EventDefinition definition, Instruction[] program)
            {
                Definition = definition;
                Program = program;
            }

            public EventDefinition Definition { get; }

            public Instruction[] Program { get; }

            public EventStatus Status { get; set; } = EventStatus.Idle;

            public bool EverCompleted { get; set; }

            public int LastCompletedVisit { get; set; } = -1;
        }

        private sealed class Monitor
        {
            public Monitor(int @event, ConditionNode condition)
            {
                Event = @event;
                Condition = condition;
            }

            public int Event { get; }

            public ConditionNode Condition { get; }

            /// <summary>Result at the last evaluation (True, False or Error); owned by Scenario, not by Condition System.</summary>
            public ConditionOutcome Previous { get; set; }
        }

        private sealed class Frame
        {
            public Frame(int @event, int visitAtStart)
            {
                Event = @event;
                VisitAtStart = visitAtStart;
            }

            public int Event { get; }

            public int VisitAtStart { get; }

            public int Pc { get; set; }
        }

        private enum ChainKind
        {
            Exclusive = 1,
            Background,
        }

        /// <summary>One execution chain: the root event plus nested CALL frames; it holds control at most once (Addendum §2).</summary>
        private sealed class Chain
        {
            private Chain(ChainKind kind, ControlToken? token)
            {
                Kind = kind;
                Token = token;
            }

            public static Chain ForEvent(ChainKind kind, ControlToken? token) => new Chain(kind, token);

            public ChainKind Kind { get; }

            public ControlToken? Token { get; }

            public List<Frame> Frames { get; } = new List<Frame>();

            public OperationHandle? Pending { get; set; }

            public ActionDefinition PendingAction { get; set; }

            public Frame Top => Frames[Frames.Count - 1];

            public bool IsWaiting => Pending.HasValue;

            public bool IsAlive => Frames.Count > 0;
        }

        private readonly struct InboxItem
        {
            private InboxItem(bool isOperation, Trigger trigger, OperationHandle handle, OperationOutcome outcome)
            {
                IsOperation = isOperation;
                Trigger = trigger;
                Handle = handle;
                Outcome = outcome;
            }

            public static InboxItem ForTrigger(Trigger trigger) => new InboxItem(false, trigger, default, default);

            public static InboxItem ForOperation(OperationHandle handle, OperationOutcome outcome) => new InboxItem(true, default, handle, outcome);

            public bool IsOperation { get; }

            public Trigger Trigger { get; }

            public OperationHandle Handle { get; }

            public OperationOutcome Outcome { get; }
        }
    }
}
