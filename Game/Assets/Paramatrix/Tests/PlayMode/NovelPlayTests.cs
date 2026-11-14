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
    /// <summary>The conversation with the girl in the Bar: choices in the hero's bubble, then the novel and back (Р12).</summary>
    public sealed class NovelPlayTests
    {
        private const float Timeout = 10f;
        private static readonly EntityId Girl = EntityId.Parse("character.beautiful_girl");
        private GameBootstrap _game;

        [UnitySetUp]
        public IEnumerator LoadBar()
        {
            SceneManager.LoadScene("Bootstrap", LoadSceneMode.Single);
            yield return null;
            yield return WaitUntil(() => (_game = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>()) != null && (_game.IsReady || _game.HasFailed), "game start");
            Assert.That(_game.HasFailed, Is.False, string.Join(Environment.NewLine, Errors()));
            while (_game.DialogueView.CurrentLine.HasValue)
            {
                _game.Input.AdvanceDialogue();
                yield return null;
            }

            yield return WaitUntil(() => _game.Session.Flow.Owner == ControlOwner.Player, "intro finished");

            bool? ok = null;
            _game.Scenes.LoadLocationScene("Location_Bar", null, (success, _) => ok = success);
            yield return WaitUntil(() => ok.HasValue, "bar");
            Assert.That(ok, Is.True);
            var hero = new StateAddress(_game.Session.Registry.Game.PlayerCharacter, BuiltInStates.Location);
            _game.Session.State.Set(hero, StateValue.Enum("location.bar"), MutationSource.System);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator RestoreTime()
        {
            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTest]
        public IEnumerator ChoicesThenTheNovelThenBackToTheBar()
        {
            var view = _game.DialogueView;
            var dialogue = _game.Session.Dialogue;
            _game.StandBy(Girl.Value);
            _game.Input.ClickAt(_game.Representations.Find(Girl).transform.position + Vector3.up, PointerButton.Primary);
            yield return WaitUntil(() => view.IsConversationShown, "the conversation");

            // Three lines, then the hero's choice in his bubble by his avatar.
            yield return AdvanceUntil(() => dialogue.IsChoosing, "the first choice");
            yield return null; // placed in LateUpdate
            Assert.That(view.CurrentChoices.Mode, Is.EqualTo(DialogueMode.Normal));
            Assert.That(view.ConversationText, Does.Contain("1. [Отшутиться]").And.Contain("2. [Признаться]"));
            Assert.That(view.AvatarSprite.name, Does.Contain("default"), "the hero's avatar by his bubble");
            Assert.That(view.IsTyping, Is.False, "options are shown whole");
            _game.Input.AdvanceDialogue();
            Assert.That(dialogue.IsChoosing, Is.True, "advancing does not answer a choice");

            // A click on the second option takes it.
            var text = view.ConversationText;
            var bubbleText = GameObject.Find("Conversation").GetComponentInChildren<TMPro.TextMeshProUGUI>();
            bubbleText.ForceMeshUpdate();
            var link = bubbleText.textInfo.linkInfo[1];
            var first = bubbleText.textInfo.characterInfo[link.linkTextfirstCharacterIndex];
            var point = (Vector2)bubbleText.transform.TransformPoint((first.bottomLeft + first.topRight) / 2f);
            Assert.That(view.ChoiceAt(point), Is.EqualTo(1), "a click on '" + text + "' finds the second option");
            Assert.That(_game.Input.ChooseOption(view.ChoiceAt(point)), Is.True);
            Assert.That(view.CurrentLine.Value.Text, Is.EqualTo("За мной правда следят."), "the hero says what was behind the label");

            // On to the second choice: "Да" goes into the novel.
            yield return AdvanceUntil(() => dialogue.IsChoosing, "the second choice");
            Assert.That(view.ConversationText, Does.Contain("1. [Да]"));
            Assert.That(_game.Input.ChooseOption(0), Is.True);
            yield return AdvanceUntil(() => view.IsNovelShown, "the novel");

            Assert.That(_game.Session.Flow.Presentation, Is.EqualTo(DialoguePresentation.Novel));
            Assert.That(_game.WorldPause.IsPaused, Is.True, "the world stands");
            Assert.That(view.IsConversationShown, Is.False, "no bubble in the novel");
            Assert.That(view.NovelPicture, Is.Not.Null, "her picture in the middle");
            Assert.That(view.NovelPictureColour.a, Is.LessThan(0.5f), "she is still in the shadow when the dream comes in");
            var since = Time.realtimeSinceStartup;
            yield return WaitUntil(() => Time.realtimeSinceStartup - since > 1.6f, "the novel coming in");
            Assert.That(view.NovelPictureColour, Is.EqualTo(Color.white), "out of the shadow: her own colours, opaque");
            Assert.That(view.NovelDream, Is.Not.Null, "the bar as a dream behind her");
            Assert.That(view.NovelName, Is.EqualTo("Красавица"));
            Assert.That(view.NovelText, Is.EqualTo("Вот так. Теперь здесь только мы."));
            Assert.That(_game.Session.Save.CanSave, Is.False, "no saving in a novel");

            // Her emotion changes: the smirk fades in over the calm picture, then the calm one is gone.
            Assert.That(view.NovelPicture.name, Does.Contain("default"));
            yield return AdvanceUntil(() => view.NovelText == "Не исчез. Ты просто перестал его замечать.", "her smirk");
            Assert.That(view.NovelPicture.name, Does.Contain("smirk"), "the picture of the line's emotion");
            Assert.That(view.NovelPreviousPicture, Is.Not.Null, "the calm picture still fading out");
            Assert.That(view.NovelPreviousPicture.name, Does.Contain("default"));
            Assert.That(view.NovelPictureColour.a, Is.LessThan(1f), "the smirk is fading in, not shown at once");
            since = Time.realtimeSinceStartup;
            yield return WaitUntil(() => Time.realtimeSinceStartup - since > 0.7f, "the change of pictures");
            Assert.That(view.NovelPreviousPicture, Is.Null, "the old picture is gone");
            Assert.That(view.NovelPictureColour, Is.EqualTo(Color.white));

            // Through the novel, its choices included, and back to the bar.
            yield return AdvanceUntil(() => dialogue.IsChoosing, "a novel choice");
            Assert.That(view.NovelText, Does.Contain("1. [Сделать комплимент]"));
            Assert.That(view.NovelName, Is.EqualTo("Параноик"), "the hero chooses");
            Assert.That(_game.Input.ChooseOption(0), Is.True);
            yield return AdvanceUntil(() => dialogue.IsChoosing, "the last novel choice");
            Assert.That(_game.Input.ChooseOption(1), Is.True);
            yield return AdvanceUntil(() => !dialogue.IsOpen, "the end of the conversation");

            Assert.That(view.IsNovelShown, Is.False, "the novel fades out");
            Assert.That(view.NovelPictureColour.r, Is.LessThan(1f), "she goes back into the shadow first");
            Assert.That(_game.Session.Flow.Presentation, Is.EqualTo(DialoguePresentation.None));
            yield return WaitUntil(() => _game.Session.Flow.Owner == ControlOwner.Player, "control back");
            Assert.That(_game.WorldPause.IsPaused, Is.False);
            Assert.That(Errors(), Is.Empty);
        }

        /// <summary>Advances (finishing typing first, as a player's presses do) until the condition holds.</summary>
        private IEnumerator AdvanceUntil(Func<bool> condition, string what)
        {
            for (var guard = 0; !condition(); guard++)
            {
                Assert.That(guard, Is.LessThan(40), "too many lines before " + what);
                Assert.That(_game.Session.Dialogue.IsChoosing, Is.False, "a choice came before " + what);
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
