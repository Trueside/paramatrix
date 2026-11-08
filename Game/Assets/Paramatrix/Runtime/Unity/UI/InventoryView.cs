using System.Collections.Generic;
using Paramatrix.Content;
using Paramatrix.Gameplay;
using TMPro;
using UnityEngine;
using EntityId = Paramatrix.Content.EntityId;

namespace Paramatrix.Unity
{
    /// <summary>
    /// Inventory bar (Architecture 0.3 §4.4): compact icons, a hover panel with name and description,
    /// the selected item stays highlighted. Shows InventorySystem; clicks go through InteractionSystem.
    /// </summary>
    public sealed class InventoryView : MonoBehaviour
    {
        [SerializeField]
        private RectTransform slots;

        [SerializeField]
        private RectTransform hoverPanel;

        [SerializeField]
        private TextMeshProUGUI hoverName;

        [SerializeField]
        private TextMeshProUGUI hoverDescription;

        [SerializeField]
        [Tooltip("Shown while an item is selected: what it is and how to use or cancel it.")]
        private TextMeshProUGUI selectionHint;

        private readonly List<InventorySlot> _slots = new List<InventorySlot>();
        private GameSession _session;
        private TMP_FontAsset _font;
        private EntityId? _hovered;

        public IReadOnlyList<InventorySlot> Slots => _slots;

        public bool IsHoverShown => hoverPanel.gameObject.activeSelf;

        public string HoverName => hoverName.text;

        public string HoverDescription => hoverDescription.text;

        public string SelectionHint => selectionHint.gameObject.activeSelf ? selectionHint.text : null;

        public void Bind(GameSession session, TMP_FontAsset font)
        {
            _session = session;
            _font = font;
            hoverName.font = font;
            hoverDescription.font = font;
            selectionHint.font = font;
            hoverPanel.gameObject.SetActive(false);
            selectionHint.gameObject.SetActive(false);

            session.State.Changed += change =>
            {
                if (change.Address.Property == BuiltInStates.Holder)
                {
                    Rebuild();
                }
            };
            session.State.Restored += Rebuild;
            session.Inventory.SelectionChanged += RefreshSelection;
            Rebuild();
        }

        /// <summary>Same entry point as a pointer click on a slot.</summary>
        public void Click(EntityId item, PointerButton button) => _session.Interaction.ClickInventoryItem(item, button);

        /// <summary>Same entry point as the pointer entering / leaving a slot.</summary>
        public void Hover(EntityId item, bool entered)
        {
            if (!entered)
            {
                if (_hovered == item)
                {
                    _hovered = null;
                    hoverPanel.gameObject.SetActive(false);
                }

                return;
            }

            var definition = _session.Registry.Get<ItemDefinition>(item);
            _hovered = item;
            hoverName.text = definition.Name;
            hoverDescription.text = definition.Description;
            hoverPanel.gameObject.SetActive(true);
        }

        private void Rebuild()
        {
            foreach (var slot in _slots)
            {
                Destroy(slot.gameObject);
            }

            _slots.Clear();
            foreach (var item in _session.Inventory.Items)
            {
                _slots.Add(InventorySlot.Create(slots, item.Id, item.Name, ColorOf(item.Id), _font, Click, Hover));
            }

            if (_hovered.HasValue && !_session.Inventory.IsCarried(_hovered.Value))
            {
                Hover(_hovered.Value, false);
            }

            RefreshSelection();
        }

        private void RefreshSelection()
        {
            var selected = _session.Inventory.Selected;
            foreach (var slot in _slots)
            {
                slot.SetSelected(selected == slot.Item);
            }

            selectionHint.gameObject.SetActive(selected.HasValue);
            if (selected.HasValue)
            {
                var name = _session.Registry.Get<ItemDefinition>(selected.Value).Name;
                selectionHint.text = $"Выбрано: {name}. Кликните по тому, к чему применить (ПКМ — отменить).";
            }
        }

        /// <summary>Stable placeholder colour per item until icons exist.</summary>
        private static Color ColorOf(EntityId item)
        {
            unchecked
            {
                var hash = 17;
                foreach (var c in item.Value)
                {
                    hash = (hash * 31) + c;
                }

                return Color.HSVToRGB((hash & 0xFFFF) / 65535f, 0.45f, 0.95f);
            }
        }
    }
}
