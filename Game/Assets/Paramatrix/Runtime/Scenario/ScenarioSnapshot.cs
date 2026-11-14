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

    /// <summary>Saved progress of one prepared monologue (Р10): how many of its lines were already said.</summary>
    public readonly struct MonologueRecord
    {
        public MonologueRecord(EntityId response, int spoken)
        {
            Response = response;
            Spoken = spoken;
        }

        public EntityId Response { get; }

        public int Spoken { get; }
    }

    /// <summary>
    /// Scenario runtime state for Save (Р1): the current Visit, completion history of every event and progress of every
    /// prepared monologue (Р10). Active execution, queue and monitors are not saved: saving is allowed only when
    /// Scenario is idle (T6), and monitor baselines are recomputed from restored State. The position in stub rounds is
    /// not saved: a round starts over after a load.
    /// </summary>
    public sealed class ScenarioSnapshot
    {
        public ScenarioSnapshot(int currentVisit, IEnumerable<EventRecord> events, IEnumerable<MonologueRecord> monologues)
        {
            CurrentVisit = currentVisit;
            var list = new List<EventRecord>(events ?? throw new ArgumentNullException(nameof(events)));
            list.Sort((a, b) => a.Event.CompareTo(b.Event));
            Events = new ReadOnlyCollection<EventRecord>(list);

            var spoken = new List<MonologueRecord>(monologues ?? throw new ArgumentNullException(nameof(monologues)));
            spoken.Sort((a, b) => a.Response.CompareTo(b.Response));
            Monologues = new ReadOnlyCollection<MonologueRecord>(spoken);
        }

        public int CurrentVisit { get; }

        /// <summary>Sorted by event ID.</summary>
        public IReadOnlyList<EventRecord> Events { get; }

        /// <summary>Sorted by response ID.</summary>
        public IReadOnlyList<MonologueRecord> Monologues { get; }
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
