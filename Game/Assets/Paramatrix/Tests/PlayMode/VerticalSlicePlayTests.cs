using System;
using System.Collections;
using System.IO;
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
    /// Stage 11 acceptance (plan 0.2, II.8): the game is played from the intro to the corridor and Morpheus,
    /// saved, changed, loaded — and Load neither repeats the intro nor loses story facts.
    /// </summary>
    public sealed class VerticalSlicePlayTests
    {
        private const float Timeout = 15f;
        private const string Slot = "playtest_slice";
        private GameBootstrap _game;

        [UnitySetUp]
        public IEnumerator LoadBootstrap()
        {
            DeleteSlot();
            SceneManager.LoadScene("Bootstrap", LoadSceneMode.Single);
            yield return null;
            yield return WaitUntil(() => (_game = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>()) != null && (_game.IsReady || _game.HasFailed), "game start");
            Assert.That(_game.HasFailed, Is.False, string.Join(Environment.NewLine, Errors()));
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            DeleteSlot();
            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayFromIntroToMorpheusSaveAndLoad()
        {
            // Intro: a thought of the hero, a monologue (Р10).
            Assert.That(_game.DialogueView.MonologueText(EntityId.Parse("character.paranoik")), Is.EqualTo("Опять этот офис."));
            Assert.That(PlayerHasControl(), Is.True);

            // Key → door → corridor.
            Click("item.key");
            yield return WaitUntil(() => Carried("item.key") && PlayerHasControl(), "key taken");
            _game.InventoryView.Click(EntityId.Parse("item.key"), PointerButton.Primary);
            Click("object.office_door");
            yield return WaitUntil(() => !Flag("object.office_door", "locked") && PlayerHasControl(), "door unlocked");
            Click("object.office_door");
            yield return WaitUntil(() => _game.Scenes.CurrentScene == "Location_Corridor" && PlayerHasControl(), "corridor");

            // Morpheus.
            Click("character.morpheus");
            yield return WaitUntil(() => _game.DialogueView.IsConversationShown, "Morpheus dialogue");
            Assert.That(_game.DialogueView.CurrentLine.Value.Speaker, Is.EqualTo(EntityId.Parse("character.morpheus")));
            Assert.That(_game.DialogueView.ConversationText, Is.EqualTo("Я ждал тебя."));
            Assert.That(_game.SaveControls.QuickSave(Slot), Is.Null, "no saving during a dialogue");
            Assert.That(_game.SaveControls.Status, Is.EqualTo("Сейчас сохранить нельзя."));
            yield return FinishDialogue();
            Assert.That(Flag("story.morpheus_meeting", "occurred"), Is.True);

            // Save in the corridor.
            var saved = _game.SaveControls.QuickSave(Slot);
            Assert.That(saved.Succeeded, Is.True, string.Join(Environment.NewLine, saved.Diagnostics.Select(d => d.ToString())));
            Assert.That(_game.SaveControls.Status, Is.EqualTo("Игра сохранена."));
            var visitAtSave = _game.Session.Scenario.CurrentVisit;

            // Back to the office: Trinity now knows (Р1: the condition reads the story fact).
            Click("object.corridor_door");
            yield return WaitUntil(() => _game.Scenes.CurrentScene == "Location_Office" && PlayerHasControl(), "back in the office");
            Assert.That(_game.DialogueView.MonologueCount, Is.Zero, "the intro is once and does not replay");
            Click("character.trinity");
            yield return WaitUntil(() => _game.DialogueView.IsConversationShown, "Trinity dialogue");
            Assert.That(_game.DialogueView.ConversationText, Is.EqualTo("Ты его видел. По глазам вижу."));
            yield return FinishDialogue();

            // Load: back to the corridor at the default entry point, nothing replays (Р2, Р9).
            SaveLoadResult loaded = null;
            _game.SaveControls.QuickLoad(Slot, result => loaded = result);
            yield return WaitUntil(() => loaded != null && PlayerHasControl(), "load");
            Assert.That(loaded.Succeeded, Is.True, string.Join(Environment.NewLine, loaded.Diagnostics.Select(d => d.ToString())));
            Assert.That(_game.SaveControls.Status, Is.EqualTo("Игра загружена."));

            Assert.That(_game.Scenes.CurrentScene, Is.EqualTo("Location_Corridor"));
            Assert.That(_game.Hero.X, Is.EqualTo(-4f).Within(0.01f), "default entry point of the corridor (-192 grid pixels)");
            Assert.That(_game.Session.Scenario.CurrentVisit, Is.EqualTo(visitAtSave), "Load creates no Visit");
            Assert.That(_game.DialogueView.CurrentLine.HasValue || _game.DialogueView.MonologueCount > 0, Is.False, "no intro, no scene on load");
            Assert.That(Flag("story.morpheus_meeting", "occurred"), Is.True);
            Assert.That(_game.Session.State.Get(StateAddress.Parse("item.key", "holder")).AsEnum, Is.EqualTo("consumed"), "the key was used up on the door");
            Assert.That(_game.InventoryView.Slots, Is.Empty);

            // The once-meeting is used up: the specific response answers instead.
            Click("character.morpheus");
            yield return null;
            Assert.That(_game.DialogueView.MonologueText(EntityId.Parse("character.morpheus")), Is.EqualTo("Морфеус молча кивает."),
                "a monologue over Morpheus");

            Assert.That(Errors(), Is.Empty);
        }

        [UnityTest]
        public IEnumerator LoadingAMissingSlotIsReported()
        {
            yield return FinishDialogue();

            SaveLoadResult loaded = null;
            _game.SaveControls.QuickLoad(Slot, result => loaded = result);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("SaveFailed: Could not read the save"));
            yield return WaitUntil(() => loaded != null, "load result");

            Assert.That(loaded.Succeeded, Is.False);
            Assert.That(_game.SaveControls.Status, Does.StartWith("Не удалось загрузить."));
            Assert.That(_game.Session.Flow.Owner, Is.EqualTo(ControlOwner.Player), "the game goes on");
            Assert.That(_game.Scenes.CurrentScene, Is.EqualTo("Location_Office"));
        }

        // ---------------- helpers ----------------

        private void Click(string id)
        {
            var view = _game.Representations.Find(EntityId.Parse(id));
            Assert.That(view, Is.Not.Null, id + " has no representation");
            _game.StandBy(view);
            var picked = _game.Input.ViewAt(view.GetComponentInChildren<SpriteRenderer>().bounds.center);
            Assert.That(picked == view, Is.True,
                $"pick at {id}: picked={(picked == null ? "null" : picked.name)} shown={view.IsShown} registered={_game.Representations.Find(view.Id) == view} id={view.Id}");
            var permissions = _game.Session.Flow.Permissions;
            var selected = _game.Session.Inventory.Selected;
            var bubbles = _game.DialogueView.MonologueCount;
            _game.Input.ClickAt(view.GetComponentInChildren<SpriteRenderer>().bounds.center, PointerButton.Primary);
            // Something happened: a scene holds control, a monologue was said, or an instant scene used the selected item.
            Assert.That(_game.Session.Flow.Owner != ControlOwner.Player
                || _game.DialogueView.MonologueCount > bubbles || _game.Session.Inventory.Selected != selected, Is.True,
                $"click on {id} started nothing: permissions={permissions} selected={selected} heroX={_game.Hero.X} approachX={view.ApproachX} " +
                $"timeScale={Time.timeScale} diagnostics=[{string.Join("; ", _game.Diagnostics.Items.Select(d => d.ToString()))}]");
        }

        private bool Carried(string item) => _game.Session.Inventory.IsCarried(EntityId.Parse(item));

        private bool Flag(string entity, string property) => _game.Session.State.Get(StateAddress.Parse(entity, property)).AsBoolean;

        private bool PlayerHasControl() => _game.Session.Flow.Owner == ControlOwner.Player && !_game.Session.Flow.InTransition;

        private IEnumerator FinishDialogue()
        {
            for (var guard = 0; _game.DialogueView.CurrentLine.HasValue; guard++)
            {
                Assert.That(guard, Is.LessThan(40));
                _game.Input.AdvanceDialogue();
                yield return null;
            }
        }

        private static void DeleteSlot()
        {
            var path = Path.Combine(Application.persistentDataPath, "saves", Slot + ".json");
            if (File.Exists(path))
            {
                File.Delete(path);
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
                    var flow = _game.Session.Flow;
                    Assert.Fail($"Timed out waiting for {what}. Owner={flow.Owner} transition={flow.InTransition} heroX={_game.Hero.X} " +
                                $"scene={_game.Scenes.CurrentScene}. Diagnostics:\n" +
                                string.Join("\n", _game.Diagnostics.Items.Select(d => d.ToString())));
                }

                yield return null;
            }
        }
    }
}
