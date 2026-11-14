using System;
using Paramatrix.Content;

namespace Paramatrix.Scenario
{
    /// <summary>Identifies one asynchronous operation. Allocated by Scenario and passed to the port that runs it.</summary>
    public readonly struct OperationHandle : IEquatable<OperationHandle>
    {
        public OperationHandle(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public bool Equals(OperationHandle other) => Value == other.Value;

        public override bool Equals(object obj) => obj is OperationHandle other && Equals(other);

        public override int GetHashCode() => Value;

        public override string ToString() => "op#" + Value;
    }

    public enum OperationStartStatus
    {
        /// <summary>Running; the port will call <see cref="ScenarioRuntime.CompleteOperation"/> exactly once.</summary>
        Started = 1,

        /// <summary>Done synchronously; the port must not call CompleteOperation for this handle.</summary>
        CompletedImmediately,

        /// <summary>Could not start; the waiting event fails.</summary>
        Failed,
    }

    public readonly struct OperationStart
    {
        private OperationStart(OperationStartStatus status, string failureReason)
        {
            Status = status;
            FailureReason = failureReason;
        }

        public static OperationStart Started => new OperationStart(OperationStartStatus.Started, null);

        public static OperationStart CompletedImmediately => new OperationStart(OperationStartStatus.CompletedImmediately, null);

        public static OperationStart Failed(string reason) =>
            new OperationStart(OperationStartStatus.Failed, string.IsNullOrEmpty(reason) ? "no reason given" : reason);

        public OperationStartStatus Status { get; }

        public string FailureReason { get; }
    }

    public enum OperationOutcome
    {
        /// <summary>Finished normally (a dialogue reached its end, the character arrived).</summary>
        Completed = 1,

        Failed,

        /// <summary>Technical cancellation (Loading, Recovery). Never counts as completion (Architecture 0.3 §2.4).</summary>
        Cancelled,
    }

    /// <summary>Synchronous result of an inventory command.</summary>
    public readonly struct InventoryResult
    {
        private InventoryResult(bool succeeded, string failureReason)
        {
            Succeeded = succeeded;
            FailureReason = failureReason;
        }

        public static InventoryResult Success => new InventoryResult(true, null);

        public static InventoryResult Failure(string reason) => new InventoryResult(false, reason ?? "no reason given");

        public bool Succeeded { get; }

        public string FailureReason { get; }
    }

    public readonly struct ControlToken : IEquatable<ControlToken>
    {
        public ControlToken(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public bool Equals(ControlToken other) => Value == other.Value;

        public override bool Equals(object obj) => obj is ControlToken other && Equals(other);

        public override int GetHashCode() => Value;
    }

    public enum ReleaseReason
    {
        Completed = 1,
        Failed,
        Aborted,
    }

    /// <summary>Exclusive control belongs to Game Flow (Architecture 0.3 §1, 0.4 §12). Scenario only asks for it.</summary>
    public interface IGameFlowPort
    {
        bool TryAcquireScenarioControl(out ControlToken token);

        void ReleaseScenarioControl(ControlToken token, ReleaseReason reason);
    }

    public interface IDialoguePort
    {
        /// <summary>A conversation in normal mode; completes when the player advances past its last line.</summary>
        OperationStart Show(OperationHandle handle, EntityId dialogue);

        /// <summary>
        /// A monologue line (Р10): shown at once, never waited for, takes no control.
        /// Returns null when shown, otherwise why it could not be shown.
        /// </summary>
        string Say(EntityId speaker, string text);
    }

    public interface IMovementPort
    {
        /// <summary>Walks a character to the approach point of an entity's representation.</summary>
        OperationStart MoveTo(OperationHandle handle, EntityId character, EntityId target);
    }

    public interface IAnimationPort
    {
        OperationStart Play(OperationHandle handle, EntityId target, string clip);
    }

    public interface ILocationPort
    {
        /// <summary>Ordinary transition; the Location System begins the new Visit before completing the operation.</summary>
        OperationStart Transition(OperationHandle handle, EntityId location, string entry);
    }

    public interface IInventoryPort
    {
        /// <summary>Moves an item to a holder value (inventory, consumed, a location or a character ID).</summary>
        InventoryResult MoveItem(EntityId item, string to);

        void ClearSelection();
    }

    /// <summary>The Gameplay Systems Scenario talks to. All are required.</summary>
    public sealed class ScenarioPorts
    {
        public ScenarioPorts(
            IGameFlowPort gameFlow,
            IDialoguePort dialogue,
            IMovementPort movement,
            IAnimationPort animation,
            ILocationPort location,
            IInventoryPort inventory)
        {
            GameFlow = gameFlow ?? throw new ArgumentNullException(nameof(gameFlow));
            Dialogue = dialogue ?? throw new ArgumentNullException(nameof(dialogue));
            Movement = movement ?? throw new ArgumentNullException(nameof(movement));
            Animation = animation ?? throw new ArgumentNullException(nameof(animation));
            Location = location ?? throw new ArgumentNullException(nameof(location));
            Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        }

        public IGameFlowPort GameFlow { get; }

        public IDialoguePort Dialogue { get; }

        public IMovementPort Movement { get; }

        public IAnimationPort Animation { get; }

        public ILocationPort Location { get; }

        public IInventoryPort Inventory { get; }
    }

    /// <summary>Safety limits (0.4 §10, T3).</summary>
    public sealed class ScenarioLimits
    {
        public static readonly ScenarioLimits Default = new ScenarioLimits(8, 64);

        public ScenarioLimits(int maxCallDepth, int maxLaunchesPerCascade)
        {
            if (maxCallDepth < 1 || maxLaunchesPerCascade < 1)
            {
                throw new ArgumentOutOfRangeException();
            }

            MaxCallDepth = maxCallDepth;
            MaxLaunchesPerCascade = maxLaunchesPerCascade;
        }

        /// <summary>Frames in one execution chain: the root event plus nested CALLs.</summary>
        public int MaxCallDepth { get; }

        /// <summary>Event launches between two moments when Scenario has nothing left to do.</summary>
        public int MaxLaunchesPerCascade { get; }
    }
}
