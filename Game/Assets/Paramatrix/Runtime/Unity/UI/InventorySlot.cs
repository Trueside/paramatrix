using System;
using Paramatrix.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using EntityId = Paramatrix.Content.EntityId;

namespace Paramatrix.Unity
{
    /// <summary>One item icon in the inventory bar. Created by <see cref="InventoryView"/>.</summary>
    public sealed class InventorySlot : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private Image _background;
        private Outline _outline;
        private Action<EntityId, PointerButton> _clicked;
        private Action<EntityId, bool> _hovered;

        public EntityId Item { get; private set; }

        public bool IsSelected { get; private set; }

        public static InventorySlot Create(Transform parent, EntityId item, string name, Color color, TMP_FontAsset font,
            Action<EntityId, PointerButton> clicked, Action<EntityId, bool> hovered)
        {
            var gameObject = new GameObject("Slot " + item.Value, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            ((RectTransform)gameObject.transform).sizeDelta = new Vector2(72f, 72f);

            var slot = gameObject.AddComponent<InventorySlot>();
            slot.Item = item;
            slot._clicked = clicked;
            slot._hovered = hovered;
            slot._background = gameObject.AddComponent<Image>();
            slot._background.color = color;
            slot._outline = gameObject.AddComponent<Outline>();
            slot._outline.effectColor = new Color(1f, 0.85f, 0.2f);
            slot._outline.effectDistance = new Vector2(4f, -4f);
            slot._outline.enabled = false;

            // Placeholder icon until item art exists: the first letter of the item name.
            var label = new GameObject("Letter", typeof(RectTransform));
            label.transform.SetParent(gameObject.transform, false);
            var rect = (RectTransform)label.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var text = label.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = name.Substring(0, 1);
            text.fontSize = 40f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.black;
            text.raycastTarget = false;
            return slot;
        }

        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            _outline.enabled = selected;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                _clicked(Item, PointerButton.Primary);
            }
            else if (eventData.button == PointerEventData.InputButton.Right)
            {
                _clicked(Item, PointerButton.Secondary);
            }
        }

        public void OnPointerEnter(PointerEventData eventData) => _hovered(Item, true);

        public void OnPointerExit(PointerEventData eventData) => _hovered(Item, false);
    }
}
