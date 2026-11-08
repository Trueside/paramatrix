using System;
using System.Collections.Generic;
using Paramatrix.Content;

namespace Paramatrix.State
{
    /// <summary>
    /// The single owner of runtime Logical State (Architecture 0.3.1 §5).
    /// Validates and commits mutations, publishes <see cref="Changed"/> after commit,
    /// exports persistent State and restores snapshots. Contains no game logic and never calls Scenario directly.
    /// </summary>
    public sealed class StateSystem : IStateReader
    {
        private readonly ContentRegistry _registry;
        private readonly IDiagnosticsSink _diagnostics;
        private readonly Dictionary<StateAddress, int> _index = new Dictionary<StateAddress, int>();
        private readonly StateDefinition[] _definitions;
        private readonly StateValue[] _values;
        private readonly Queue<StateChange> _pending = new Queue<StateChange>();
        private bool _dispatching;

        /// <summary>Initializes every property with its default. No notifications: initialization is not a mutation.</summary>
        public StateSystem(ContentRegistry registry, IDiagnosticsSink diagnostics)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

            var states = registry.States;
            _definitions = new StateDefinition[states.Count];
            _values = new StateValue[states.Count];
            for (var i = 0; i < states.Count; i++)
            {
                _definitions[i] = states[i];
                _values[i] = states[i].Default;
                _index.Add(states[i].Address, i);
            }
        }

        /// <summary>Published after a committed change, in commit order. Never for Unchanged or Rejected.</summary>
        public event Action<StateChange> Changed;

        /// <summary>Published after a successful <see cref="Restore"/>. Restore never publishes <see cref="Changed"/>.</summary>
        public event Action Restored;

        public StateReadStatus TryGet(StateAddress address, out StateValue value)
        {
            if (_index.TryGetValue(address, out var slot))
            {
                value = _values[slot];
                return StateReadStatus.Ok;
            }

            value = default;
            return IsKnownEntity(address.Entity) ? StateReadStatus.UnknownProperty : StateReadStatus.UnknownEntity;
        }

        /// <summary>Reads an address known to exist (validated content). Throws otherwise.</summary>
        public StateValue Get(StateAddress address)
        {
            if (!_index.TryGetValue(address, out var slot))
            {
                throw new KeyNotFoundException($"No State property {address}.");
            }

            return _values[slot];
        }

        /// <summary>
        /// Validates in the order of Architecture 0.3.1 §5.7, then commits atomically.
        /// Setting the current value returns Unchanged and publishes nothing (§5.6).
        /// </summary>
        public MutationResult Set(StateAddress address, StateValue value, MutationSource source)
        {
            if (!_index.TryGetValue(address, out var slot))
            {
                return MutationResult.Rejected(IsKnownEntity(address.Entity) ? MutationError.UnknownProperty : MutationError.UnknownEntity);
            }

            var definition = _definitions[slot];
            switch (definition.Check(value))
            {
                case ValueCheck.TypeMismatch: return MutationResult.Rejected(MutationError.TypeMismatch);
                case ValueCheck.OutOfRange: return MutationResult.Rejected(MutationError.OutOfRange);
                case ValueCheck.InvalidEnumValue: return MutationResult.Rejected(MutationError.InvalidEnumValue);
            }

            var required = source == MutationSource.Scenario ? MutationPolicy.ScenarioOnly : MutationPolicy.SystemOnly;
            if (!definition.Allows(required))
            {
                return MutationResult.Rejected(MutationError.MutationNotAllowed);
            }

            var old = _values[slot];
            if (old == value)
            {
                return MutationResult.Unchanged;
            }

            _values[slot] = value;
            Publish(new StateChange(address, old, value, source));
            return MutationResult.Applied;
        }

        public StateSnapshot ExportPersistent()
        {
            var entries = new List<StateSnapshotEntry>();
            for (var i = 0; i < _definitions.Length; i++)
            {
                if (_definitions[i].Persistence == Persistence.Persistent)
                {
                    entries.Add(new StateSnapshotEntry(_definitions[i].Address, _values[i]));
                }
            }

            return new StateSnapshot(entries);
        }

        /// <summary>
        /// Checks the whole snapshot against current content before applying anything (decision A15):
        /// it must contain exactly the persistent properties, each with a valid value. Any mismatch rejects
        /// the restore and leaves State unchanged. On success persistent values are applied, transient ones reset
        /// to defaults, and only <see cref="Restored"/> is published.
        /// </summary>
        public RestoreResult Restore(StateSnapshot snapshot, string sourceName)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (_dispatching)
            {
                throw new InvalidOperationException("Restore is not allowed from a State Changed handler.");
            }

            var location = new SourceLocation(sourceName ?? "<snapshot>", null, 0, 0);
            var errors = new DiagnosticBag();
            var restored = new StateValue[_values.Length];
            var seen = new bool[_values.Length];

            foreach (var entry in snapshot.Entries)
            {
                if (!_index.TryGetValue(entry.Address, out var slot))
                {
                    var code = IsKnownEntity(entry.Address.Entity) ? DiagnosticCode.SnapshotUnknownProperty : DiagnosticCode.SnapshotUnknownEntity;
                    errors.Error(code, location, $"Saved property {entry.Address} does not exist in current content.");
                    continue;
                }

                var definition = _definitions[slot];
                if (definition.Persistence != Persistence.Persistent)
                {
                    errors.Error(DiagnosticCode.SnapshotNotPersistent, location, $"Saved property {entry.Address} is not persistent in current content.");
                    continue;
                }

                if (seen[slot])
                {
                    errors.Error(DiagnosticCode.SnapshotDuplicateEntry, location, $"Property {entry.Address} is saved twice.");
                    continue;
                }

                seen[slot] = true;
                var check = definition.Check(entry.Value);
                if (check != ValueCheck.Ok)
                {
                    errors.Error(
                        DiagnosticCode.SnapshotInvalidValue,
                        location,
                        $"Saved value {entry.Value} of {entry.Address} is not valid for current content ({check}).");
                    continue;
                }

                restored[slot] = entry.Value;
            }

            for (var i = 0; i < _definitions.Length; i++)
            {
                if (_definitions[i].Persistence == Persistence.Persistent && !seen[i])
                {
                    errors.Error(DiagnosticCode.SnapshotMissingEntry, location, $"Property {_definitions[i].Address} is missing from the save.");
                }
            }

            if (errors.Items.Count > 0)
            {
                return new RestoreResult(errors.Items);
            }

            for (var i = 0; i < _definitions.Length; i++)
            {
                _values[i] = _definitions[i].Persistence == Persistence.Persistent ? restored[i] : _definitions[i].Default;
            }

            InvokeSafely(Restored, handler => handler());
            return new RestoreResult(Array.Empty<Diagnostic>());
        }

        private bool IsKnownEntity(EntityId id) => _registry.TryGet<WorldEntityDefinition>(id, out _);

        /// <summary>
        /// Notifications are delivered in commit order. A Set made from inside a handler is committed at once,
        /// but its notification is queued until the current one reached every subscriber.
        /// </summary>
        private void Publish(StateChange change)
        {
            _pending.Enqueue(change);
            if (_dispatching)
            {
                return;
            }

            _dispatching = true;
            try
            {
                while (_pending.Count > 0)
                {
                    var next = _pending.Dequeue();
                    InvokeSafely(Changed, handler => handler(next));
                }
            }
            finally
            {
                _dispatching = false;
            }
        }

        /// <summary>A failing subscriber does not undo the commit and does not stop delivery to the others.</summary>
        private void InvokeSafely<T>(T multicast, Action<T> invoke)
            where T : Delegate
        {
            if (multicast == null)
            {
                return;
            }

            foreach (var handler in multicast.GetInvocationList())
            {
                try
                {
                    invoke((T)handler);
                }
                catch (Exception exception)
                {
                    _diagnostics.Report(new Diagnostic(
                        DiagnosticCode.StateSubscriberFailed,
                        Severity.Error,
                        $"State notification handler {handler.Method.DeclaringType?.Name}.{handler.Method.Name} failed: {exception}",
                        SourceLocation.None));
                }
            }
        }
    }
}
