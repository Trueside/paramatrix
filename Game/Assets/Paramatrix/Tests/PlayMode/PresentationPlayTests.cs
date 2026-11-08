using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Paramatrix.Content;
using Paramatrix.Gameplay;
using Paramatrix.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using EntityId = Paramatrix.Content.EntityId;

namespace Paramatrix.PlayTests
{
    /// <summary>Stage 10 (plan 0.2, II.8): speech bubbles, VN panel with world pause, inventory bar and hover panel.</summary>
    public sealed class PresentationPlayTests
    {
        private const float Timeout = 10f;
        private GameBootstrap _game;

        [UnitySetUp]
        public IEnumerator LoadBootstrap()
        {
            SceneManager.LoadScene("Bootstrap", LoadSceneMode.Single);
            yield return null;
            yield return WaitUntil(() => (_game = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>()) != null && (_game.IsReady || _game.HasFailed), "game start");
            Assert.That(_game.HasFailed, Is.False, string.Join(Environment.NewLine, Errors()));
        }

        [UnityTearDown]
        public IEnumerator RestoreTime()
        {
            Time.timeScale = 1f;
            yield return null;
        }

        [Test]
        public void FontRendersCyrillic()
        {
            Assert.That(_game.Font, Is.Not.Null);
            Assert.That(_game.Font.HasCharacters(UiFonts.CyrillicProbe + "Опять этот офис.", out _, true, true), Is.True);
        }

        [UnityTest]
        public IEnumerator IntroLineIsABubbleAboveTheHero()
        {
            yield return null; // let LateUpdate place the bubble

            var view = _game.DialogueView;
            Assert.That(view.IsBubbleShown, Is.True);
            Assert.That(view.IsNovelPanelShown, Is.False);
            Assert.That(view.BubbleText, Is.EqualTo("Опять этот офис."));

            var camera = Camera.main;
            var heroRenderer = _game.Hero.GetComponent<SpriteRenderer>();
            var heroTop = camera.WorldToScreenPoint(new Vector3(heroRenderer.bounds.center.x, heroRenderer.bounds.max.y, 0f));
            Assert.That(view.BubbleScreenPosition.x, Is.EqualTo(heroTop.x).Within(2f), "centred over the speaker");
            Assert.That(view.BubbleScreenPosition.y, Is.GreaterThan(heroTop.y), "above the speaker's head");

            _game.Input.AdvanceDialogue();
            yield return null;
            Assert.That(view.IsBubbleShown, Is.False);
        }

        [UnityTest]
        public IEnumerator ResponseIsABubbleToo()
        {
            yield return FinishDialogue();

            _game.Input.ClickAt(Position("object.office_window"), PointerButton.Secondary);
            yield return null;

            Assert.That(_game.DialogueView.IsBubbleShown, Is.True);
            Assert.That(_game.DialogueView.BubbleText, Is.EqualTo("Капли ползут по стеклу слишком ровно."));
        }

        [UnityTest]
        public IEnumerator NovelDialoguePausesTheWorld()
        {
            yield return FinishDialogue();
            Assert.That(_game.WorldPause.IsPaused, Is.False);

            _game.Input.ClickAt(Position("character.trinity"), PointerButton.Primary);
            yield return WaitUntil(() => _game.DialogueView.IsNovelPanelShown, "VN panel");

            Assert.That(_game.DialogueView.NovelName, Is.EqualTo("Тринити"));
            Assert.That(_game.DialogueView.NovelText, Is.EqualTo("Ты ведь чувствуешь это?"));
            Assert.That(_game.DialogueView.IsBubbleShown, Is.False);
            Assert.That(_game.WorldPause.IsPaused, Is.True, "VN pauses the world (0.3 §2.2)");

            _game.Input.AdvanceDialogue();
            Assert.That(_game.DialogueView.NovelName, Is.EqualTo("Параноик"));
            yield return FinishDialogue();

            Assert.That(_game.DialogueView.IsNovelPanelShown, Is.False);
            Assert.That(_game.WorldPause.IsPaused, Is.False);
        }

        [UnityTest]
        public IEnumerator InventoryBarShowsSelectsAndDescribes()
        {
            yield return FinishDialogue();
            Assert.That(_game.InventoryView.Slots, Is.Empty);

            _game.Input.ClickAt(Position("item.key"), PointerButton.Primary);
            yield return WaitUntil(() => _game.InventoryView.Slots.Count == 1 && _game.Session.Flow.Owner == ControlOwner.Player, "key in the bar");

            var key = EntityId.Parse("item.key");
            var slot = _game.InventoryView.Slots.Single();
            Assert.That(slot.Item, Is.EqualTo(key));

            _game.InventoryView.Hover(key, true);
            Assert.That(_game.InventoryView.IsHoverShown, Is.True);
            Assert.That(_game.InventoryView.HoverName, Is.EqualTo("Ключ"));
            Assert.That(_game.InventoryView.HoverDescription, Is.EqualTo("Маленький латунный ключ."));
            _game.InventoryView.Hover(key, false);
            Assert.That(_game.InventoryView.IsHoverShown, Is.False);

            _game.InventoryView.Click(key, PointerButton.Primary);
            Assert.That(slot.IsSelected, Is.True, "the selected item stays highlighted");
            _game.InventoryView.Click(key, PointerButton.Primary);
            Assert.That(slot.IsSelected, Is.False);
            Assert.That(Errors(), Is.Empty);
        }

        // ---------------- helpers ----------------

        private IEnumerator FinishDialogue()
        {
            for (var guard = 0; _game.DialogueView.CurrentLine.HasValue; guard++)
            {
                Assert.That(guard, Is.LessThan(20));
                _game.Input.AdvanceDialogue();
                yield return null;
            }
        }

        private Vector2 Position(string id)
        {
            var view = _game.Representations.Find(EntityId.Parse(id));
            Assert.That(view, Is.Not.Null, id + " has no representation");
            return view.transform.position;
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
