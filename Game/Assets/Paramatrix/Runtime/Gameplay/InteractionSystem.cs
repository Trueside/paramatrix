using System;
using Paramatrix.Content;
using Paramatrix.Scenario;

namespace Paramatrix.Gameplay
{
    public enum PointerButton
    {
        /// <summary>Left mouse button.</summary>
        Primary = 1,

        /// <summary>Right mouse button.</summary>
        Secondary,
    }

    /// <summary>
    /// Single routing point of player interactions (Architecture 0.3 §5). Turns clicks into interaction types (T12) and
    /// hands the trigger to Scenario. The player walks the hero himself (Р11): a left click counts only on a target within
    /// the hero's reach and does nothing otherwise; a right click (inspect, a thought) works from any distance.
    /// No story logic here.
    /// </summary>
    public sealed class InteractionSystem
    {
        private readonly ContentRegistry _registry;
        private readonly GameFlow _flow;
        private readonly InventorySystem _inventory;
        private readonly DialogueSystem _dialogue;
        private readonly IHeroReach _reach;
        private ScenarioRuntime _scenario;

        public InteractionSystem(ContentRegistry registry, GameFlow flow, InventorySystem inventory, DialogueSystem dialogue, IHeroReach reach)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _flow = flow ?? throw new ArgumentNullException(nameof(flow));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _dialogue = dialogue ?? throw new ArgumentNullException(nameof(dialogue));
            _reach = reach ?? throw new ArgumentNullException(nameof(reach));
        }

        internal void Attach(ScenarioRuntime scenario) => _scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));

        private bool CanInteract => (_flow.Permissions & PlayerPermissions.Interact) != 0;

        /// <summary>Click on a character, object or item lying in the world.</summary>
        public void ClickWorld(EntityId target, PointerButton button)
        {
            if (!CanInteract)
            {
                return; // input belongs to someone else right now; not an error
            }

            Trigger trigger;
            if (button == PointerButton.Secondary)
            {
                trigger = Trigger.PlayerInteraction(InteractionType.Inspect, target); // from any distance
            }
            else
            {
                if (!_reach.CanReach(target))
                {
                    return; // out of the hero's reach: nothing happens, the player walks closer himself (Р11)
                }

                var selected = _inventory.Selected;
                trigger = selected.HasValue
                    ? Trigger.PlayerInteraction(InteractionType.ApplyItem, target, selected.Value)
                    : Trigger.PlayerInteraction(PrimaryOf(target), target);
            }

            // A scene stops the hero and turns him to the target. A monologue does not: he keeps doing what he did (Р10).
            if (_scenario.WouldStartEvent(trigger))
            {
                _reach.FaceForInteraction(target);
            }

            Submit(trigger);
        }

        /// <summary>Click on an item icon in the inventory UI.</summary>
        public void ClickInventoryItem(EntityId item, PointerButton button)
        {
            if (!CanInteract)
            {
                return;
            }

            if (button == PointerButton.Secondary)
            {
                Submit(Trigger.PlayerInteraction(InteractionType.Inspect, item));
                return;
            }

            var selected = _inventory.Selected;
            if (!selected.HasValue)
            {
                _inventory.Select(item);
            }
            else if (selected.Value == item)
            {
                _inventory.ClearSelection();
            }
            else
            {
                Submit(Trigger.PlayerInteraction(InteractionType.CombineItems, item, selected.Value));
            }
        }

        /// <summary>Uses an inventory item on its own (e.g. "swallow the pill").</summary>
        public void UseInventoryItem(EntityId item)
        {
            if (CanInteract)
            {
                Submit(Trigger.PlayerInteraction(InteractionType.Use, null, item));
            }
        }

        /// <summary>
        /// Click on empty space. Right click cancels the item selection (0.3 §5.3) and moves the hero's thought on (Р10):
        /// a shown thought goes to its next prepared line, and closes when it has none.
        /// </summary>
        public void ClickEmpty(PointerButton button)
        {
            if (!CanInteract || button != PointerButton.Secondary)
            {
                return;
            }

            _inventory.ClearSelection();
            var hero = _registry.Game.PlayerCharacter;
            if (_dialogue.IsSaying(hero) && !_scenario.ContinueThought())
            {
                _dialogue.Hush(hero);
            }
        }

        private InteractionType PrimaryOf(EntityId target)
        {
            switch (target.Kind)
            {
                case EntityKind.Item:
                    return InteractionType.Take;
                case EntityKind.Character:
                    return _registry.Get<CharacterDefinition>(target).PrimaryInteraction;
                case EntityKind.Object:
                    return _registry.Get<ObjectDefinition>(target).PrimaryInteraction;
                default:
                    throw new ArgumentException($"{target} is not something the player can click.", nameof(target));
            }
        }

        private void Submit(in Trigger trigger) => _scenario.Submit(trigger);
    }
}
