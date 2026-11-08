using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Paramatrix.Content;

namespace Paramatrix.Scenario
{
    /// <summary>Saved completion history of one event (Р1, Semantic Addendum §1). Failed is never saved (Р3).</summary>
    public readonly struct EventRecord
    {
        public EventRecord(EntityId @event, bool everCompleted, int lastCompletedVisit)
        {
            Event = @event;
            EverCompleted = everCompleted;
            LastCompletedVisit = lastCompletedVisit;
        }

        public EntityId Event { get; }

        public bool EverCompleted { get; }

        /// <summary>Visit in which the event last completed; -1 when never.</summary>
        public int LastCompletedVisit { get; }
    }

    /// <summary>
    /// Scenario runtime state for Save (Р1): the current Visit and completion history of every event.
    /// Active execution, queue and monitors are not saved: saving is allowed only when Scenario is idle (T6),
    /// and monitor baselines are recomputed from restored State.
    /// </summary>
    public sealed class ScenarioSnapshot
    {
        public ScenarioSnapshot(int currentVisit, IEnumerable<EventRecord> events)
        {
            CurrentVisit = currentVisit;
            var list = new List<EventRecord>(events ?? throw new ArgumentNullException(nameof(events)));
            list.Sort((a, b) => a.Event.CompareTo(b.Event));
            Events = new ReadOnlyCollection<EventRecord>(list);
        }

        public int CurrentVisit { get; }

        /// <summary>Sorted by event ID.</summary>
        public IReadOnlyList<EventRecord> Events { get; }
    }

    public sealed class ScenarioRestoreResult
    {
        internal ScenarioRestoreResult(IReadOnlyList<Diagnostic> diagnostics)
        {
            Diagnostics = diagnostics;
        }

        /// <summary>False means Scenario state was not changed at all.</summary>
        public bool Succeeded => Diagnostics.Count == 0;

        public IReadOnlyList<Diagnostic> Diagnostics { get; }
    }
}
