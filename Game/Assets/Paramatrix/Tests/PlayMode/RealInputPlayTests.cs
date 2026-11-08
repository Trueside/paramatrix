using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Paramatrix.Content;
using Paramatrix.Gameplay;
using Paramatrix.State;
using Paramatrix.Unity;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using EntityId = Paramatrix.Content.EntityId;

namespace Paramatrix.PlayTests
{
    /// <summary>
    /// Input through the real paths a player uses: a keyboard device for dialogue, the UI event system for inventory
    /// icons. Other tests call entry points directly; these catch what lies between the device and those entry points.
    /// </summary>
    public sealed class RealInputPlayTests
    {
        private const float Timeout = 10f;
        private GameBootstrap _game;
        private Keyboard _keyboard;
        private InputSettings _originalSettings;

        [UnitySetUp]
        public IEnumerator LoadBootstrap()
        {
            // A batch run has no focused Game view; by default the editor then keeps keyboard input away from the game.
            // A separate settings instance is used so the project's input settings are never modified.
            _originalSettings = InputSystem.settings;
            var settings = ScriptableObject.CreateInstance<InputSettings>();
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings = settings;

            SceneManager.LoadScene("Bootstrap", LoadSceneMode.Single);
            yield return null;
            yield return WaitUntil(() => (_game = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>()) != null && (_game.IsReady || _game.HasFailed), "game start");
            Assert.That(_game.HasFailed, Is.False, string.Join(Environment.NewLine, Errors()));
        }

        [UnityTearDown]
        public IEnumerator RemoveDevices()
        {
            if (_keyboard != null)
            {
                InputSystem.RemoveDevice(_keyboard);
                _keyboard = null;
            }

            InputSystem.settings = _originalSettings;
            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTest]
        public IEnumerator ArrowKeyAdvancesDialogue()
        {
            Assert.That(_game.DialogueView.CurrentLine.HasValue, Is.True, "intro line");
            _keyboard = InputSystem.AddDevice<Keyboard>();

            yield return Press(Key.RightArrow);

            Assert.That(_game.DialogueView.CurrentLine.HasValue, Is.False, "an arrow key closed the last line");
            Assert.That(_game.Session.Flow.Owner, Is.EqualTo(ControlOwner.Player));
        }

        [UnityTest]
        public IEnumerator AnyLetterKeyAdvancesDialogue()
        {
            _keyboard = InputSystem.AddDevice<Keyboard>();

            yield return Press(Key.Q);

            Assert.That(_game.DialogueView.CurrentLine.HasValue, Is.False);
        }

        [UnityTest]
        public IEnumerator KeyIconClickSelectsAndUnlocksTheDoor()
        {
            yield return FinishDialogue();
            Click("item.key");
            yield return WaitUntil(() => _game.InventoryView.Slots.Count == 1 && _game.Session.Flow.Owner == ControlOwner.Player, "key in the bar");
            yield return null; // layout of the new slot

            // A real pointer click on the icon through the UI event system.
            var slot = _game.InventoryView.Slots.Single();
            var target = UiTarget(slot.transform.position);
            Assert.That(target != null && target.GetComponentInParent<InventorySlot>() == slot, Is.True,
                $"the pointer over the key icon hits '{(target == null ? "nothing" : target.name)}'");
            ExecuteEvents.ExecuteHierarchy(target, Pointer(slot.transform.position, PointerEventData.InputButton.Left), ExecuteEvents.pointerClickHandler);

            Assert.That(slot.IsSelected, Is.True);
            Assert.That(_game.InventoryView.SelectionHint, Does.StartWith("Выбрано: Ключ."));

            Click("object.office_door");
            yield return WaitUntil(() => _game.DialogueView.CurrentLine.HasValue, "unlock line");
            Assert.That(_game.DialogueView.BubbleText, Is.EqualTo("Замок щёлкнул. Дверь открыта."));
            Assert.That(_game.Session.State.Get(StateAddress.Parse("object.office_door", "locked")).AsBoolean, Is.False);
            Assert.That(_game.InventoryView.SelectionHint, Is.Null, "selection cleared by the scene");
            yield return null; // destroyed slots disappear at the end of the frame
            Assert.That(_game.InventoryView.Slots, Is.Empty, "the key is used up");
            Assert.That(_game.Session.Inventory.IsCarried(EntityId.Parse("item.key")), Is.False);
            yield return FinishDialogue();

            Click("object.office_door");
            yield return WaitUntil(() => _game.Scenes.CurrentScene == "Location_Corridor" && _game.Session.Flow.Owner == ControlOwner.Player, "corridor");
            Assert.That(Errors(), Is.Empty);
        }

        [UnityTest]
        public IEnumerator RightClickOnEmptySpaceCancelsSelection()
        {
            yield return FinishDialogue();
            Click("item.key");
            yield return WaitUntil(() => _game.InventoryView.Slots.Count == 1 && _game.Session.Flow.Owner == ControlOwner.Player, "key in the bar");
            _game.InventoryView.Click(EntityId.Parse("item.key"), PointerButton.Primary);
            Assert.That(_game.InventoryView.SelectionHint, Is.Not.Null);

            _game.Input.ClickAt(new Vector2(-6.5f, 4f), PointerButton.Secondary); // empty wall

            Assert.That(_game.Session.Inventory.Selected, Is.Null);
            Assert.That(_game.InventoryView.SelectionHint, Is.Null);
        }

        // ---------------- helpers ----------------

        private IEnumerator Press(Key key)
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
        }

        private static PointerEventData Pointer(Vector2 screen, PointerEventData.InputButton button) =>
            new PointerEventData(EventSystem.current) { position = screen, button = button };

        private static GameObject UiTarget(Vector2 screen)
        {
            var events = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            var results = new List<RaycastResult>();
            events.RaycastAll(new PointerEventData(events) { position = screen }, results);
            return results.Count == 0 ? null : results[0].gameObject;
        }

        private void Click(string id)
        {
            var view = _game.Representations.Find(EntityId.Parse(id));
            Assert.That(view, Is.Not.Null, id + " has no representation");
            _game.Input.ClickAt(view.transform.position, PointerButton.Primary);
        }

        private IEnumerator FinishDialogue()
        {
            for (var guard = 0; _game.DialogueView.CurrentLine.HasValue; guard++)
            {
                Assert.That(guard, Is.LessThan(20));
                _game.Input.AdvanceDialogue();
                yield return null;
            }
        }

        private string[] Errors() =>
            _game == null || _game.Diagnostics == null
                ? Array.Empty<string>()
                : _game.Diagnostics.Items.Where(d => d.Severity == Severity.Error).Select(d => d.ToString()).ToArray();

        private IEnumerator WaitUntil(Func<bool> condition, string what)
        {
            var deadline = Time.realtimeSinceStartup + Timeout;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail($"Timed out waiting for {what}. Errors:\n{string.Join("\n", Errors())}");
                }

                yield return null;
            }
        }
    }
}
