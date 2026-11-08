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
    /// Single routing point of player interactions (Architecture 0.3 §5). Turns clicks into interaction types (T12),
    /// walks the hero to the target first (T13) and hands the trigger to Scenario. No story logic here.
    /// </summary>
    public sealed class InteractionSystem
    {
        private readonly ContentRegistry _registry;
        private readonly GameFlow _flow;
        private readonly InventorySystem _inventory;
        private readonly IHeroNavigation _navigation;
        private readonly IDiagnosticsSink _diagnostics;
        private ScenarioRuntime _scenario;
        private int _approachId;
        private bool _approaching;

        public InteractionSystem(ContentRegistry registry, GameFlow flow, InventorySystem inventory, IHeroNavigation navigation, IDiagnosticsSink diagnostics)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _flow = flow ?? throw new ArgumentNullException(nameof(flow));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _flow.Recovering += CancelApproach;
        }

        public bool IsApproaching => _approaching;

        internal void Attach(ScenarioRuntime scenario) => _scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));

        private bool CanInteract => (_flow.Permissions & PlayerPermissions.Interact) != 0;

        /// <summary>Click on a character, object or item lying in the world.</summary>
        public void ClickWorld(EntityId target, PointerButton button)
        {
            if (!CanInteract)
            {
                return; // input belongs to someone else right now; not an error
            }

            if (button == PointerButton.Secondary)
            {
                CancelApproach();
                Submit(Trigger.PlayerInteraction(InteractionType.Inspect, target)); // inspect needs no approach
                return;
            }

            var selected = _inventory.Selected;
            var trigger = selected.HasValue
                ? Trigger.PlayerInteraction(InteractionType.ApplyItem, target, selected.Value)
                : Trigger.PlayerInteraction(PrimaryOf(target), target);
            Approach(target, trigger);
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

        /// <summary>Click on empty space. Right click cancels the item selection (0.3 §5.3).</summary>
        public void ClickEmpty(PointerButton button)
        {
            if (CanInteract && button == PointerButton.Secondary)
            {
                _inventory.ClearSelection();
            }
        }

        /// <summary>The player took over movement: the pending interaction is dropped (T13).</summary>
        public void CancelApproach()
        {
            if (_approaching)
            {
                _approaching = false;
                _approachId++;
                _navigation.Cancel();
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

        private void Approach(EntityId target, Trigger trigger)
        {
            CancelApproach();
            var id = ++_approachId;
            _approaching = true;
            _navigation.Approach(target, (outcome, error) =>
            {
                if (id != _approachId)
                {
                    return; // superseded by a newer click or cancelled
                }

                _approaching = false;
                switch (outcome)
                {
                    case ApproachOutcome.Arrived:
                        if (CanInteract)
                        {
                            Submit(trigger);
                        }

                        break;
                    case ApproachOutcome.Failed:
                        _diagnostics.Report(new Diagnostic(DiagnosticCode.ApproachFailed, Severity.Error,
                            $"Hero could not approach {target}: {error}", SourceLocation.None));
                        break;
                }
            });
        }

        private void Submit(in Trigger trigger) => _scenario.Submit(trigger);
    }
}
