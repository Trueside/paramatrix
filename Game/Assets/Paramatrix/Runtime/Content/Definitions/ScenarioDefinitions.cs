using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Paramatrix.Content
{
    /// <summary>Architecture 0.4 §6.</summary>
    public enum ExecutionType
    {
        Exclusive = 1,
        Background,
    }

    /// <summary>Architecture 0.4 §4.3, Semantic Addendum §1.</summary>
    public enum RepeatPolicy
    {
        Once = 1,
        Repeatable,
        OncePerLocationVisit,
    }

    /// <summary>
    /// MVP trigger types (decision A11, T15). Item use and combination are <see cref="Interaction"/>
    /// with interaction type use / apply_item / combine_items.
    /// </summary>
    public enum TriggerType
    {
        Interaction = 1,
        ItemAcquired,
        StateChanged,
        LocationEntered,
        DialogueCompleted,
        EventCompleted,
        ConditionsMet,
    }

    /// <summary>
    /// Mechanical filter of incoming triggers (0.4 §3.4). A filter left out matches any value.
    /// Which filters a trigger type allows is checked by the loader.
    /// </summary>
    public sealed class TriggerDefinition
    {
        public TriggerDefinition(
            TriggerType type,
            SourceLocation source,
            InteractionType? interaction = null,
            EntityId? target = null,
            EntityId? item = null,
            EntityId? location = null,
            EntityId? dialogue = null,
            EntityId? @event = null,
            StateAddress? state = null,
            StateValue? to = null,
            ConditionNode condition = null)
        {
            Type = type;
            Source = source;
            Interaction = interaction;
            Target = target;
            Item = item;
            Location = location;
            Dialogue = dialogue;
            Event = @event;
            State = state;
            To = to;
            Condition = condition;
        }

        public TriggerType Type { get; }

        public SourceLocation Source { get; }

        /// <summary>Interaction: required interaction type.</summary>
        public InteractionType? Interaction { get; }

        /// <summary>Interaction: target entity.</summary>
        public EntityId? Target { get; }

        /// <summary>Interaction (use / apply_item / combine_items) and ItemAcquired: the item.</summary>
        public EntityId? Item { get; }

        /// <summary>LocationEntered.</summary>
        public EntityId? Location { get; }

        /// <summary>DialogueCompleted.</summary>
        public EntityId? Dialogue { get; }

        /// <summary>EventCompleted.</summary>
        public EntityId? Event { get; }

        /// <summary>StateChanged: required State address.</summary>
        public StateAddress? State { get; }

        /// <summary>StateChanged: new value filter.</summary>
        public StateValue? To { get; }

        /// <summary>ConditionsMet: the monitored condition (fires on False → True).</summary>
        public ConditionNode Condition { get; }
    }

    // ---------- Actions ----------

    /// <summary>One scenario operation (0.4 §8). Closed hierarchy; Scenario System dispatches on the concrete type.</summary>
    public abstract class ActionDefinition
    {
        protected ActionDefinition(SourceLocation source)
        {
            Source = source;
        }

        public SourceLocation Source { get; }

        /// <summary>True for actions that start an operation and wait for its completion (A7).</summary>
        public virtual bool IsAsync => false;
    }

    public sealed class SetStateAction : ActionDefinition
    {
        public SetStateAction(StateAddress target, StateValue value, SourceLocation source)
            : base(source)
        {
            Target = target;
            Value = value;
        }

        public StateAddress Target { get; }

        public StateValue Value { get; }
    }

    public sealed class IfAction : ActionDefinition
    {
        public IfAction(ConditionNode condition, IEnumerable<ActionDefinition> then, IEnumerable<ActionDefinition> @else, SourceLocation source)
            : base(source)
        {
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            Then = new ReadOnlyCollection<ActionDefinition>(new List<ActionDefinition>(then));
            Else = new ReadOnlyCollection<ActionDefinition>(new List<ActionDefinition>(@else ?? Array.Empty<ActionDefinition>()));
        }

        public ConditionNode Condition { get; }

        public IReadOnlyList<ActionDefinition> Then { get; }

        /// <summary>Runs on False only; an Error fails the event (0.4 §9.1).</summary>
        public IReadOnlyList<ActionDefinition> Else { get; }
    }

    /// <summary>Successful early end of the event (A8: the end of the list is an implicit END).</summary>
    public sealed class EndAction : ActionDefinition
    {
        public EndAction(SourceLocation source)
            : base(source)
        {
        }
    }

    public sealed class CallAction : ActionDefinition
    {
        public CallAction(EntityId @event, SourceLocation source)
            : base(source)
        {
            Event = @event;
        }

        public EntityId Event { get; }

        public override bool IsAsync => true;
    }

    public sealed class StartAction : ActionDefinition
    {
        public StartAction(EntityId @event, SourceLocation source)
            : base(source)
        {
            Event = @event;
        }

        public EntityId Event { get; }
    }

    /// <summary>Moves an item: to inventory, consumed, a location (lies there) or a character (Р7, T14).</summary>
    public sealed class InventoryAction : ActionDefinition
    {
        public InventoryAction(EntityId item, string to, SourceLocation source)
            : base(source)
        {
            Item = item;
            To = to ?? throw new ArgumentNullException(nameof(to));
        }

        public EntityId Item { get; }

        /// <summary>A value of the item's built-in <c>holder</c> property.</summary>
        public string To { get; }
    }

    public sealed class ClearSelectionAction : ActionDefinition
    {
        public ClearSelectionAction(SourceLocation source)
            : base(source)
        {
        }
    }

    public sealed class DialogueAction : ActionDefinition
    {
        public DialogueAction(EntityId dialogue, SourceLocation source)
            : base(source)
        {
            Dialogue = dialogue;
        }

        public EntityId Dialogue { get; }

        public override bool IsAsync => true;
    }

    /// <summary>Walks a character to the approach point of an entity's representation.</summary>
    public sealed class MoveAction : ActionDefinition
    {
        public MoveAction(EntityId character, EntityId to, SourceLocation source)
            : base(source)
        {
            Character = character;
            To = to;
        }

        public EntityId Character { get; }

        public EntityId To { get; }

        public override bool IsAsync => true;
    }

    public sealed class AnimationAction : ActionDefinition
    {
        public AnimationAction(EntityId target, string clip, SourceLocation source)
            : base(source)
        {
            Target = target;
            Clip = clip ?? throw new ArgumentNullException(nameof(clip));
        }

        public EntityId Target { get; }

        public string Clip { get; }

        public override bool IsAsync => true;
    }

    public sealed class TransitionAction : ActionDefinition
    {
        public TransitionAction(EntityId location, string entry, SourceLocation source)
            : base(source)
        {
            Location = location;
            Entry = entry;
        }

        public EntityId Location { get; }

        /// <summary>Entry point name in the location scene; null = the default entry point.</summary>
        public string Entry { get; }

        public override bool IsAsync => true;
    }

    // ---------- Event, dialogue, response ----------

    public sealed class EventDefinition : Definition
    {
        public EventDefinition(
            EntityId id,
            SourceLocation source,
            ExecutionType executionType,
            RepeatPolicy repeatPolicy,
            IEnumerable<TriggerDefinition> triggers,
            ConditionNode launchCondition,
            IEnumerable<ActionDefinition> actions)
            : base(id, source)
        {
            ExecutionType = executionType;
            RepeatPolicy = repeatPolicy;
            Triggers = new ReadOnlyCollection<TriggerDefinition>(new List<TriggerDefinition>(triggers));
            LaunchCondition = launchCondition;
            Actions = new ReadOnlyCollection<ActionDefinition>(new List<ActionDefinition>(actions));
        }

        public ExecutionType ExecutionType { get; }

        public RepeatPolicy RepeatPolicy { get; }

        /// <summary>May be empty: such an event runs only through CALL / START.</summary>
        public IReadOnlyList<TriggerDefinition> Triggers { get; }

        /// <summary>Null means always allowed.</summary>
        public ConditionNode LaunchCondition { get; }

        public IReadOnlyList<ActionDefinition> Actions { get; }
    }

    public enum DialogueMode
    {
        Minimal = 1,
        VisualNovel,
    }

    public sealed class DialogueLine
    {
        public DialogueLine(EntityId speaker, string text, SourceLocation source)
        {
            Speaker = speaker;
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Source = source;
        }

        public EntityId Speaker { get; }

        public string Text { get; }

        public SourceLocation Source { get; }
    }

    /// <summary>Linear dialogue (Architecture 0.3 §2). Presentation mode is chosen by content.</summary>
    public sealed class DialogueDefinition : Definition
    {
        public DialogueDefinition(EntityId id, SourceLocation source, DialogueMode mode, IEnumerable<DialogueLine> lines)
            : base(id, source)
        {
            Mode = mode;
            Lines = new ReadOnlyCollection<DialogueLine>(new List<DialogueLine>(lines));
        }

        public DialogueMode Mode { get; }

        public IReadOnlyList<DialogueLine> Lines { get; }
    }

    /// <summary>Resolution levels of a player action without a matching event (Architecture 0.3 §2.5, §5.6; Р6).</summary>
    public enum ResponseTier
    {
        Specific = 1,
        Contextual,
        Generic,
        Fallback,
    }

    /// <summary>A one-line reaction without actions (Р6).</summary>
    public sealed class ResponseDefinition : Definition
    {
        public ResponseDefinition(
            EntityId id,
            SourceLocation source,
            ResponseTier tier,
            InteractionType interaction,
            EntityId? target,
            EntityId? item,
            ConditionNode condition,
            EntityId? speaker,
            string text)
            : base(id, source)
        {
            Tier = tier;
            Interaction = interaction;
            Target = target;
            Item = item;
            Condition = condition;
            Speaker = speaker;
            Text = text;
        }

        public ResponseTier Tier { get; }

        public InteractionType Interaction { get; }

        /// <summary>Required for Specific, absent otherwise.</summary>
        public EntityId? Target { get; }

        public EntityId? Item { get; }

        public ConditionNode Condition { get; }

        /// <summary>Null: the player character, or the target for <c>talk</c>.</summary>
        public EntityId? Speaker { get; }

        /// <summary>Null only for a fallback declared as "no reaction".</summary>
        public string Text { get; }

        public bool IsNoReaction => Text == null;
    }
}
