using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Paramatrix.Content;

namespace Paramatrix.State
{
    public enum StateReadStatus
    {
        Ok = 1,
        UnknownEntity,
        UnknownProperty,
    }

    /// <summary>
    /// Read-only view of Logical State. Condition System gets only this interface,
    /// so "Conditions do not change State" is enforced by types (Architecture 0.3.2 inv. 2).
    /// </summary>
    public interface IStateReader
    {
        StateReadStatus TryGet(StateAddress address, out StateValue value);
    }

    /// <summary>
    /// Initiator of an ordinary mutation (Architecture 0.3.1 §5.4). Initialization and Restore are not
    /// mutations of this API: they are internal paths of <see cref="StateSystem"/> (T9).
    /// </summary>
    public enum MutationSource
    {
        Scenario = 1,
        System,
    }

    public enum MutationStatus
    {
        Applied = 1,
        Unchanged,
        Rejected,
    }

    public enum MutationError
    {
        None = 0,
        UnknownEntity,
        UnknownProperty,
        TypeMismatch,
        OutOfRange,
        InvalidEnumValue,
        MutationNotAllowed,
    }

    public readonly struct MutationResult
    {
        private MutationResult(MutationStatus status, MutationError error)
        {
            Status = status;
            Error = error;
        }

        public static MutationResult Applied => new MutationResult(MutationStatus.Applied, MutationError.None);

        public static MutationResult Unchanged => new MutationResult(MutationStatus.Unchanged, MutationError.None);

        public static MutationResult Rejected(MutationError error) => new MutationResult(MutationStatus.Rejected, error);

        public MutationStatus Status { get; }

        /// <summary>Reason of rejection; <see cref="MutationError.None"/> otherwise.</summary>
        public MutationError Error { get; }

        public override string ToString() => Status == MutationStatus.Rejected ? $"Rejected ({Error})" : Status.ToString();
    }

    /// <summary>Payload of a State Changed notification (Architecture 0.3.1 §6).</summary>
    public readonly struct StateChange
    {
        public StateChange(StateAddress address, StateValue oldValue, StateValue newValue, MutationSource source)
        {
            Address = address;
            OldValue = oldValue;
            NewValue = newValue;
            Source = source;
        }

        public StateAddress Address { get; }

        public StateValue OldValue { get; }

        public StateValue NewValue { get; }

        public MutationSource Source { get; }

        public override string ToString() => $"{Address}: {OldValue} → {NewValue} ({Source})";
    }

    public readonly struct StateSnapshotEntry
    {
        public StateSnapshotEntry(StateAddress address, StateValue value)
        {
            Address = address;
            Value = value;
        }

        public StateAddress Address { get; }

        public StateValue Value { get; }
    }

    /// <summary>Persistent State values for Save (Architecture 0.3.1 §9). Entries sorted by address.</summary>
    public sealed class StateSnapshot
    {
        public StateSnapshot(IEnumerable<StateSnapshotEntry> entries)
        {
            var list = new List<StateSnapshotEntry>(entries ?? throw new ArgumentNullException(nameof(entries)));
            list.Sort((a, b) => a.Address.CompareTo(b.Address));
            Entries = new ReadOnlyCollection<StateSnapshotEntry>(list);
        }

        public IReadOnlyList<StateSnapshotEntry> Entries { get; }
    }

    public sealed class RestoreResult
    {
        internal RestoreResult(IReadOnlyList<Diagnostic> diagnostics)
        {
            Diagnostics = diagnostics;
        }

        /// <summary>True when the snapshot was applied. False means State was not changed at all.</summary>
        public bool Succeeded => Diagnostics.Count == 0;

        public IReadOnlyList<Diagnostic> Diagnostics { get; }
    }
}
