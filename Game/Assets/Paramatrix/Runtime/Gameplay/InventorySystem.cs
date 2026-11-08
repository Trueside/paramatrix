using System;
using System.Collections.Generic;
using Paramatrix.Content;
using Paramatrix.Scenario;
using Paramatrix.State;

namespace Paramatrix.Gameplay
{
    /// <summary>
    /// Item ownership and selection (Architecture 0.3 §4). The source of truth is each item's built-in
    /// <c>holder</c> State; this system only executes scenario commands (Р7) and keeps the UI selection,
    /// which is presentation state and is never saved.
    /// </summary>
    public sealed class InventorySystem : IInventoryPort
    {
        private static readonly StateValue InInventory = StateValue.Enum(BuiltInStates.Inventory);

        private readonly ContentRegistry _registry;
        private readonly StateSystem _state;
        private ScenarioRuntime _scenario;

        public InventorySystem(ContentRegistry registry, StateSystem state)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _state.Restored += () => SetSelection(null);
        }

        public EntityId? Selected { get; private set; }

        public event Action SelectionChanged;

        internal void Attach(ScenarioRuntime scenario) => _scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));

        /// <summary>Items the player carries, in content declaration order (T18).</summary>
        public IReadOnlyList<ItemDefinition> Items
        {
            get
            {
                var result = new List<ItemDefinition>();
                foreach (var item in _registry.ItemsInDeclarationOrder)
                {
                    if (IsCarried(item.Id))
                    {
                        result.Add(item);
                    }
                }

                return result;
            }
        }

        public bool IsCarried(EntityId item) => _state.Get(new StateAddress(item, BuiltInStates.Holder)) == InInventory;

        public void Select(EntityId item)
        {
            if (!IsCarried(item))
            {
                throw new InvalidOperationException($"{item} is not in the inventory and cannot be selected.");
            }

            SetSelection(item);
        }

        public void ClearSelection() => SetSelection(null);

        public InventoryResult MoveItem(EntityId item, string to)
        {
            if (!_registry.TryGet<ItemDefinition>(item, out _))
            {
                return InventoryResult.Failure($"{item} is not an item");
            }

            var result = _state.Set(new StateAddress(item, BuiltInStates.Holder), StateValue.Enum(to), MutationSource.System);
            if (result.Status == MutationStatus.Rejected)
            {
                return InventoryResult.Failure($"holder of {item} cannot become '{to}' ({result.Error})");
            }

            if (Selected == item && to != BuiltInStates.Inventory)
            {
                SetSelection(null);
            }

            if (result.Status == MutationStatus.Applied && to == BuiltInStates.Inventory)
            {
                _scenario.Submit(Trigger.ItemAcquired(item));
            }

            return InventoryResult.Success;
        }

        private void SetSelection(EntityId? item)
        {
            if (Selected == item)
            {
                return;
            }

            Selected = item;
            SelectionChanged?.Invoke();
        }
    }
}
