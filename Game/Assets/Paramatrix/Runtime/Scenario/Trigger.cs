using Paramatrix.Content;
using Paramatrix.State;

namespace Paramatrix.Scenario
{
    public enum TriggerOrigin
    {
        /// <summary>Direct player action: never queued (0.4 §7.3).</summary>
        Player = 1,

        Automatic,
    }

    /// <summary>
    /// Notification that something potentially relevant happened (0.4 §3). Carries context, not a copy of State.
    /// Flat by design: few types, mechanical matching.
    /// </summary>
    public readonly struct Trigger
    {
        private Trigger(
            TriggerType type,
            TriggerOrigin origin,
            InteractionType? interaction = null,
            EntityId? target = null,
            EntityId? item = null,
            EntityId? location = null,
            EntityId? dialogue = null,
            EntityId? @event = null,
            StateChange? change = null)
        {
            Type = type;
            Origin = origin;
            Interaction = interaction;
            Target = target;
            Item = item;
            Location = location;
            Dialogue = dialogue;
            Event = @event;
            Change = change;
        }

        public TriggerType Type { get; }

        public TriggerOrigin Origin { get; }

        public InteractionType? Interaction { get; }

        public EntityId? Target { get; }

        public EntityId? Item { get; }

        public EntityId? Location { get; }

        public EntityId? Dialogue { get; }

        public EntityId? Event { get; }

        public StateChange? Change { get; }

        /// <summary>A player interaction. <paramref name="item"/> is the selected item for use / apply_item / combine_items.</summary>
        public static Trigger PlayerInteraction(InteractionType interaction, EntityId? target, EntityId? item = null) =>
            new Trigger(TriggerType.Interaction, TriggerOrigin.Player, interaction: interaction, target: target, item: item);

        public static Trigger ItemAcquired(EntityId item) => new Trigger(TriggerType.ItemAcquired, TriggerOrigin.Automatic, item: item);

        internal static Trigger StateChanged(StateChange change) => new Trigger(TriggerType.StateChanged, TriggerOrigin.Automatic, change: change);

        internal static Trigger LocationEntered(EntityId location) => new Trigger(TriggerType.LocationEntered, TriggerOrigin.Automatic, location: location);

        internal static Trigger DialogueCompleted(EntityId dialogue) => new Trigger(TriggerType.DialogueCompleted, TriggerOrigin.Automatic, dialogue: dialogue);

        internal static Trigger EventCompleted(EntityId @event) => new Trigger(TriggerType.EventCompleted, TriggerOrigin.Automatic, @event: @event);

        public override string ToString()
        {
            switch (Type)
            {
                case TriggerType.Interaction: return $"interaction {InteractionTypes.Name(Interaction.Value)} {Target} {Item}";
                case TriggerType.StateChanged: return "state_changed " + Change;
                default: return Type + " " + (Item ?? Location ?? Dialogue ?? Event);
            }
        }
    }

    /// <summary>Mechanical matching of a trigger against a trigger definition (0.4 §3.4). Not a condition.</summary>
    internal static class TriggerMatcher
    {
        public static bool Matches(TriggerDefinition definition, in Trigger trigger)
        {
            if (definition.Type != trigger.Type)
            {
                return false;
            }

            switch (definition.Type)
            {
                case TriggerType.Interaction:
                    if (definition.Interaction != trigger.Interaction)
                    {
                        return false;
                    }

                    if (definition.Interaction == InteractionType.CombineItems)
                    {
                        return MatchesPair(definition.Target, definition.Item, trigger.Target, trigger.Item);
                    }

                    return Filter(definition.Target, trigger.Target) && Filter(definition.Item, trigger.Item);

                case TriggerType.ItemAcquired:
                    return Filter(definition.Item, trigger.Item);

                case TriggerType.StateChanged:
                    var change = trigger.Change.Value;
                    return definition.State.Value == change.Address
                        && (!definition.To.HasValue || definition.To.Value == change.NewValue);

                case TriggerType.LocationEntered:
                    return Filter(definition.Location, trigger.Location);

                case TriggerType.DialogueCompleted:
                    return Filter(definition.Dialogue, trigger.Dialogue);

                case TriggerType.EventCompleted:
                    return Filter(definition.Event, trigger.Event);

                default:
                    // ConditionsMet is driven by monitors, never by incoming triggers.
                    return false;
            }
        }

        private static bool Filter(EntityId? wanted, EntityId? actual) => !wanted.HasValue || wanted == actual;

        /// <summary>combine_items is symmetric: "key + note" and "note + key" are the same action (T15).</summary>
        private static bool MatchesPair(EntityId? a, EntityId? b, EntityId? x, EntityId? y)
        {
            if (a.HasValue && b.HasValue)
            {
                return (a == x && b == y) || (a == y && b == x);
            }

            var single = a ?? b;
            return !single.HasValue || single == x || single == y;
        }
    }
}
