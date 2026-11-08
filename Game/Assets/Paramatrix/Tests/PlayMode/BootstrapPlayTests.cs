using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Paramatrix.Content;
using Paramatrix.Gameplay;
using Paramatrix.State;
using Paramatrix.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using EntityId = Paramatrix.Content.EntityId;

namespace Paramatrix.PlayTests
{
    /// <summary>
    /// Stage 9 acceptance (plan 0.2, II.8): the real Bootstrap scene starts a new game, the hero walks,
    /// a click on an object reaches Scenario, a transition loads the next scene. Drives the same InputRouter
    /// entry points the keyboard and mouse use.
    /// </summary>
    public sealed class BootstrapPlayTests
    {
        private const float Timeout = 10f;
        private GameBootstrap _game;

        [UnitySetUp]
        public IEnumerator LoadBootstrap()
        {
            SceneManager.LoadScene("Bootstrap", LoadSceneMode.Single);
            yield return null; // the load completes at the end of the frame; never pick up the previous test's objects
            yield return WaitUntil(() => (_game = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>()) != null && (_game.IsReady || _game.HasFailed), "game start");
            Assert.That(_game.HasFailed, Is.False, string.Join(Environment.NewLine, Errors()));
        }

        [UnityTest]
        public IEnumerator NewGameLoadsOfficeAndPlaysIntro()
        {
            Assert.That(_game.Scenes.CurrentScene, Is.EqualTo("Location_Office"));
            Assert.That(_game.Hero.X, Is.EqualTo(-5f).Within(0.01f), "default entry point");
            Assert.That(_game.DialogueView.CurrentLine.HasValue, Is.True, "intro line shown");
            Assert.That(_game.DialogueView.CurrentLine.Value.Text, Is.EqualTo("Опять этот офис."));

            _game.Input.AdvanceDialogue();
            yield return null;

            Assert.That(_game.DialogueView.CurrentLine.HasValue, Is.False);
            Assert.That(_game.Session.Flow.Owner, Is.EqualTo(ControlOwner.Player));
            Assert.That(Errors(), Is.Empty);
        }

        [UnityTest]
        public IEnumerator HeroWalksWithinFloor()
        {
            yield return FinishIntro();
            var start = _game.Hero.X;

            for (var i = 0; i < 10; i++)
            {
                _game.Input.Walk(1f, 0.05f);
                yield return null;
            }

            Assert.That(_game.Hero.X, Is.GreaterThan(start + 1f));

            for (var i = 0; i < 200; i++)
            {
                _game.Input.Walk(-1f, 0.1f);
            }

            Assert.That(_game.Hero.X, Is.EqualTo(-8f).Within(0.01f), "clamped to the floor");
        }

        [UnityTest]
        public IEnumerator ClickOnWindowReachesScenario()
        {
            yield return FinishIntro();

            _game.Input.ClickAt(ViewPosition("object.office_window"), PointerButton.Primary);
            yield return WaitUntil(() => _game.DialogueView.CurrentLine.HasValue, "response after the approach");

            Assert.That(_game.DialogueView.CurrentLine.Value.Text, Is.EqualTo("За окном всегда один и тот же дождь."));
            Assert.That(_game.Hero.X, Is.EqualTo(0f).Within(0.05f), "walked to the approach point first");
            Assert.That(Errors(), Is.Empty);
        }

        [UnityTest]
        public IEnumerator RightClickInspectsWithoutWalking()
        {
            yield return FinishIntro();
            var start = _game.Hero.X;

            _game.Input.ClickAt(ViewPosition("object.office_window"), PointerButton.Secondary);
            yield return null;

            Assert.That(_game.DialogueView.CurrentLine.Value.Text, Is.EqualTo("Капли ползут по стеклу слишком ровно."));
            Assert.That(_game.Hero.X, Is.EqualTo(start).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator TakeKeyUnlockDoorAndLeave()
        {
            yield return FinishIntro();

            _game.Input.ClickAt(ViewPosition("item.key"), PointerButton.Primary);
            yield return WaitUntil(() => _game.Session.Inventory.IsCarried(EntityId.Parse("item.key")), "key taken");
            yield return WaitUntil(() => _game.Session.Flow.Owner == ControlOwner.Player, "take scene finished");
            Assert.That(_game.Representations.Find(EntityId.Parse("item.key")).IsShown, Is.False, "key left the world");

            _game.Session.Interaction.ClickInventoryItem(EntityId.Parse("item.key"), PointerButton.Primary);
            _game.Input.ClickAt(ViewPosition("object.office_door"), PointerButton.Primary);
            yield return WaitUntil(() => !_game.Session.State.Get(StateAddress.Parse("object.office_door", "locked")).AsBoolean, "door unlocked");
            yield return WaitUntil(() => _game.Session.Flow.Owner == ControlOwner.Player, "unlock scene finished");

            _game.Input.ClickAt(ViewPosition("object.office_door"), PointerButton.Primary);
            yield return WaitUntil(() => _game.Scenes.CurrentScene == "Location_Corridor" && _game.Session.Flow.Owner == ControlOwner.Player, "transition");

            Assert.That(_game.Session.Location.CurrentLocation.Value, Is.EqualTo("location.corridor"));
            Assert.That(_game.Hero.X, Is.EqualTo(-6f).Within(0.01f), "entry point from_office");
            Assert.That(_game.Representations.Find(EntityId.Parse("object.office_door")), Is.Null, "office views unregistered");
            Assert.That(Errors(), Is.Empty);
        }

        // ---------------- helpers ----------------

        private IEnumerator FinishIntro()
        {
            while (_game.DialogueView.CurrentLine.HasValue)
            {
                _game.Input.AdvanceDialogue();
                yield return null;
            }
        }

        private Vector2 ViewPosition(string id)
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
