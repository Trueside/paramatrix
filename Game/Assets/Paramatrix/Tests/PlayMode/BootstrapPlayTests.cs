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
            yield return null;
            Assert.That(_game.Scenes.CurrentScene, Is.EqualTo("Location_Office"));
            Assert.That(_game.Hero.X, Is.EqualTo(-6f).Within(0.01f), "default entry point");

            // The intro thought is a monologue (Р10): a bubble over the hero, and the player has control at once.
            Assert.That(_game.DialogueView.MonologueText(Hero), Is.EqualTo("Опять этот офис."));
            Assert.That(_game.DialogueView.CurrentLine.HasValue, Is.False, "no conversation");
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

            Assert.That(_game.Hero.X, Is.EqualTo(-9f).Within(0.01f), "clamped to the floor");
        }

        [UnityTest]
        public IEnumerator ClickOnPosterReachesScenario()
        {
            yield return FinishIntro();

            // Out of the hero's reach a left click does nothing, and he does not walk (Р11).
            _game.Hero.Walker.PlaceAt(PixelGrid.Units(400));
            var before = _game.DialogueView.MonologueText(Hero); // the intro thought may still be shown
            _game.Input.ClickAt(ViewPosition("object.office_poster"), PointerButton.Primary);
            yield return null;
            Assert.That(_game.DialogueView.MonologueText(Hero), Is.EqualTo(before), "too far: nothing happens");
            Assert.That(_game.Hero.X, Is.EqualTo(PixelGrid.Units(400)).Within(0.01f), "no walk to the target");

            // Within reach the response answers as a monologue (Р10).
            _game.StandBy("object.office_poster");
            _game.Input.ClickAt(ViewPosition("object.office_poster"), PointerButton.Primary);
            yield return null;
            Assert.That(_game.DialogueView.MonologueText(Hero), Is.EqualTo("Паутина из шестиугольников. Не помню, когда я её повесил."));
            Assert.That(Errors(), Is.Empty);
        }

        [UnityTest]
        public IEnumerator HeroInFrontNeverTakesTheClick()
        {
            yield return FinishIntro();

            // Stand in front of Trinity, then click her: the hero is drawn on top but must not be the target.
            var trinity = ViewPosition("character.trinity");
            _game.Hero.Walker.PlaceAt(trinity.x);
            Assert.That(_game.Input.ViewAt(trinity).Id, Is.EqualTo(EntityId.Parse("character.trinity")));

            _game.Input.ClickAt(trinity, PointerButton.Primary);
            yield return WaitUntil(() => _game.DialogueView.CurrentLine.HasValue, "Trinity dialogue");
            Assert.That(_game.DialogueView.CurrentLine.Value.Speaker, Is.EqualTo(EntityId.Parse("character.trinity")));
        }

        [UnityTest]
        public IEnumerator ClickAreaFollowsTheSpriteRightAfterATeleport()
        {
            yield return FinishIntro();

            // No physics in picking: a character moved this very frame is clickable at its new place at once.
            var trinity = _game.Representations.Find(EntityId.Parse("character.trinity"));
            var oldPosition = (Vector2)trinity.transform.position;
            trinity.GetComponent<Walker>().PlaceAt(-3f);

            Assert.That(_game.Input.ViewAt(new Vector2(-3f, oldPosition.y)), Is.SameAs(trinity));
            Assert.That(_game.Input.ViewAt(oldPosition), Is.Not.SameAs(trinity));
        }

        [UnityTest]
        public IEnumerator RightClickInspectsWithoutWalking()
        {
            yield return FinishIntro();
            _game.Hero.Walker.PlaceAt(PixelGrid.Units(400)); // far from the poster: a right click works from any distance
            var start = _game.Hero.X;

            _game.Input.ClickAt(ViewPosition("object.office_poster"), PointerButton.Secondary);
            yield return null;

            Assert.That(_game.DialogueView.MonologueText(Hero), Is.EqualTo("Линии сходятся в центре, как будто там кто-то сидит."));
            Assert.That(_game.Hero.X, Is.EqualTo(start).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator TakeKeyUnlockDoorAndLeave()
        {
            yield return FinishIntro();

            _game.StandBy("item.key");
            _game.Input.ClickAt(ViewPosition("item.key"), PointerButton.Primary);
            yield return WaitUntil(() => _game.Session.Inventory.IsCarried(EntityId.Parse("item.key")), "key taken");
            yield return WaitUntil(() => _game.Session.Flow.Owner == ControlOwner.Player, "take scene finished");
            Assert.That(_game.Representations.Find(EntityId.Parse("item.key")).IsShown, Is.False, "key left the world");

            _game.Session.Interaction.ClickInventoryItem(EntityId.Parse("item.key"), PointerButton.Primary);
            _game.StandBy("object.office_door");
            _game.Input.ClickAt(ViewPosition("object.office_door"), PointerButton.Primary);
            yield return WaitUntil(() => !_game.Session.State.Get(StateAddress.Parse("object.office_door", "locked")).AsBoolean, "door unlocked");
            Assert.That(_game.Session.Inventory.IsCarried(EntityId.Parse("item.key")), Is.False, "the key is used up");
            yield return WaitUntil(() => _game.Session.Flow.Owner == ControlOwner.Player, "unlock scene finished");

            // Record what the player would see at every Game Flow change during the transition.
            var coveredDuringTransition = true;
            var transitionSeen = false;
            float? heroXWhenUncovered = null;
            void Watch()
            {
                var flow = _game.Session.Flow;
                if (flow.InTransition)
                {
                    transitionSeen = true;
                    coveredDuringTransition &= _game.TransitionCurtain.IsShown;
                }
                else if (transitionSeen && heroXWhenUncovered == null)
                {
                    heroXWhenUncovered = _game.Hero.X;
                }
            }

            _game.Session.Flow.Changed += Watch;
            _game.Input.ClickAt(ViewPosition("object.office_door"), PointerButton.Primary);
            yield return WaitUntil(() => _game.Scenes.CurrentScene == "Location_Corridor" && _game.Session.Flow.Owner == ControlOwner.Player, "transition");
            _game.Session.Flow.Changed -= Watch;

            Assert.That(transitionSeen, Is.True);
            Assert.That(coveredDuringTransition, Is.True, "the screen is covered for the whole transition");
            Assert.That(heroXWhenUncovered, Is.EqualTo(-7f).Within(0.01f), "the hero is at the entry point before the cover is lifted");
            Assert.That(_game.TransitionCurtain.IsShown, Is.False);
            Assert.That(_game.Session.Location.CurrentLocation.Value, Is.EqualTo("location.corridor"));
            Assert.That(_game.Hero.X, Is.EqualTo(-7f).Within(0.01f), "entry point from_office");
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

        private static readonly EntityId Hero = EntityId.Parse("character.paranoik");

        private Vector2 ViewPosition(string id)
        {
            var view = _game.Representations.Find(EntityId.Parse(id));
            Assert.That(view, Is.Not.Null, id + " has no representation");
            return view.GetComponentInChildren<SpriteRenderer>().bounds.center; // the middle of the click area, as a player clicks
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
