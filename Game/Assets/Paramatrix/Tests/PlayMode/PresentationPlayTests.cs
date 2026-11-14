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
    /// <summary>Stage 10 (plan 0.2, II.8), Р10: monologue bubbles, conversations with avatars and world pause, inventory bar and hover panel.</summary>
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
        public void WorldIsDrawnOnThePixelGrid()
        {
            // Г-02: the camera frames 960×540 grid pixels (В-25); the hero sprite (~144 grid pixels) is drawn at its own size, never scaled.
            var camera = Camera.main;
            Assert.That(camera.orthographicSize * 2f * PixelGrid.PixelsPerUnit, Is.EqualTo(PixelGrid.Height).Within(0.001f));
            Assert.That(camera.aspect, Is.EqualTo(16f / 9f).Within(0.01f), "16:9 frame, bars on other aspect ratios");

            var hero = _game.Hero.GetComponent<SpriteRenderer>().bounds;
            var sprite = _game.Hero.GetComponent<SpriteRenderer>().sprite;
            Assert.That(hero.size.y * PixelGrid.PixelsPerUnit, Is.EqualTo(sprite.rect.height).Within(0.001f));
            Assert.That(sprite.rect.height, Is.InRange(140f, 150f), "about 144 grid pixels tall (Г-02)");
            Assert.That(_game.Hero.transform.localScale, Is.EqualTo(Vector3.one), "one texture pixel is one grid pixel");
            Assert.That(hero.min.y, Is.EqualTo(0f).Within(0.001f), "standing on the floor line");
        }

        [UnityTest]
        public IEnumerator HeroStepsThroughWalkFramesAndStandsAtRest()
        {
            var animation = _game.Hero.GetComponent<WalkAnimation>();
            var renderer = _game.Hero.GetComponent<SpriteRenderer>();
            var standing = renderer.sprite;
            var seen = new System.Collections.Generic.HashSet<Sprite>();
            for (var i = 0; i < 90; i++)
            {
                _game.Hero.Walker.Step(1f, -9f, 9f, 1f / 60f);
                yield return null;
                seen.Add(renderer.sprite);
            }

            Assert.That(animation.IsWalking, Is.True);
            Assert.That(seen.Count, Is.GreaterThanOrEqualTo(6), "the walk cycle runs through its frames");

            for (var i = 0; i < 60; i++)
            {
                yield return null;
            }

            Assert.That(animation.IsWalking, Is.False);
            Assert.That(renderer.sprite, Is.SameAs(standing), "at rest the hero stands");
        }

        [Test]
        public void FontRendersCyrillic()
        {
            Assert.That(_game.Font, Is.Not.Null);
            Assert.That(_game.Font.HasCharacters(UiFonts.CyrillicProbe + "Опять этот офис.", out _, true, true), Is.True);
        }

        [UnityTest]
        public IEnumerator MonologueIsABubbleOverTheSpeaker()
        {
            yield return null; // let LateUpdate place the bubble

            var view = _game.DialogueView;
            Assert.That(view.MonologueText(Hero), Is.EqualTo("Опять этот офис."));
            Assert.That(view.CurrentLine.HasValue, Is.False, "a monologue is not a conversation");
            Assert.That(_game.WorldPause.IsPaused, Is.False, "a monologue never stops the world (Р10)");

            Assert.That(view.MonologueIsThought(Hero), Is.True, "the hero's monologue is a thought cloud");
            var camera = Camera.main;
            var heroRenderer = _game.Hero.GetComponent<SpriteRenderer>();
            var heroTop = camera.WorldToScreenPoint(new Vector3(_game.Hero.X, heroRenderer.bounds.max.y, 0f));
            var bubble = view.MonologueScreenRect(Hero);
            Assert.That(bubble.center.x, Is.EqualTo(heroTop.x).Within(2f), "centred over the speaker's position");
            Assert.That(bubble.yMin, Is.GreaterThan(heroTop.y), "above the speaker's head");
            Assert.That(bubble.width, Is.LessThan(Screen.width * 0.5f), "sized to the phrase, not the screen");
        }

        [UnityTest]
        public IEnumerator AThoughtFollowsAWalkingTurningHeroWithoutShaking()
        {
            yield return null;
            var view = _game.DialogueView;
            var camera = Camera.main;
            _game.Input.ClickAt(Position("object.office_poster"), PointerButton.Secondary);
            yield return null;
            var start = view.MonologueScreenRect(Hero);
            var offset = start.center.x - camera.WorldToScreenPoint(_game.Hero.transform.position).x;

            // Walk right, turn, walk left: every frame the cloud keeps its place over the hero and its height.
            foreach (var direction in new[] { 1f, -1f })
            {
                for (var i = 0; i < 20; i++)
                {
                    _game.Input.Walk(direction, Time.deltaTime);
                    yield return null;
                    var rect = view.MonologueScreenRect(Hero);
                    var heroX = camera.WorldToScreenPoint(_game.Hero.transform.position).x;
                    Assert.That(rect.center.x - heroX, Is.EqualTo(offset).Within(1f), $"frame {i} going {direction}: the cloud stays over the hero");
                    Assert.That(rect.yMin, Is.EqualTo(start.yMin).Within(0.5f), $"frame {i} going {direction}: walking frames do not shake it");
                }
            }

            Assert.That(Errors(), Is.Empty);
        }

        [UnityTest]
        public IEnumerator NextLineReplacesTheSpeakersBubbleAndTimeTakesItAway()
        {
            yield return null;
            var view = _game.DialogueView;

            _game.Input.ClickAt(Position("object.office_poster"), PointerButton.Secondary);
            Assert.That(view.MonologueText(Hero), Is.EqualTo("Линии сходятся в центре, как будто там кто-то сидит."));
            _game.Input.ClickAt(Position("object.office_poster"), PointerButton.Secondary);
            Assert.That(view.MonologueText(Hero), Is.EqualTo("Если долго смотреть, шестиугольники начинают шевелиться."));
            Assert.That(view.MonologueCount, Is.EqualTo(1), "one bubble per speaker");

            Time.timeScale = 20f; // the bubble's seconds of world time, quickly
            yield return WaitUntil(() => view.MonologueCount == 0, "the bubble goes out by itself");
            Time.timeScale = 1f;
            Assert.That(Errors(), Is.Empty);
        }

        [UnityTest]
        public IEnumerator RightClickOnEmptySpaceMovesTheThoughtOnThenClosesIt()
        {
            yield return null;
            var view = _game.DialogueView;
            var wall = new Vector2(-6.5f, 4f); // empty wall

            _game.Input.ClickAt(Position("object.office_poster"), PointerButton.Secondary);
            Assert.That(view.MonologueText(Hero), Is.EqualTo("Линии сходятся в центре, как будто там кто-то сидит."));
            Assert.That(_game.Input.ViewAt(wall), Is.Null, "the wall point is empty space");

            _game.Input.ClickAt(wall, PointerButton.Secondary);
            Assert.That(view.MonologueText(Hero), Is.EqualTo("Если долго смотреть, шестиугольники начинают шевелиться."), "the next prepared line");

            _game.Input.ClickAt(wall, PointerButton.Secondary);
            Assert.That(view.MonologueText(Hero), Is.Null, "nothing prepared is left: the thought closes");
            Assert.That(Errors(), Is.Empty);
        }

        [UnityTest]
        public IEnumerator ConversationShowsAvatarsFacingEachOther()
        {
            yield return null;
            Assert.That(_game.WorldPause.IsPaused, Is.False);

            _game.StandBy("character.trinity"); // on her left
            _game.Input.ClickAt(Position("character.trinity"), PointerButton.Primary);
            yield return WaitUntil(() => _game.DialogueView.IsConversationShown, "conversation");
            yield return null; // placed in LateUpdate

            var view = _game.DialogueView;
            Assert.That(view.MonologueCount, Is.Zero, "a conversation takes the screen");
            Assert.That(_game.WorldPause.IsPaused, Is.True, "a conversation pauses the world (0.3 §2.2)");
            Assert.That(view.CurrentLine.Value.Speaker, Is.EqualTo(EntityId.Parse("character.trinity")));
            Assert.That(view.ConversationText, Is.EqualTo("Ты ведь чувствуешь это?"));
            Assert.That(view.AvatarSprite, Is.Not.Null);
            AssertOnTheLine(view);

            // Trinity stands right of the hero and looks at him: her avatar looks left, her bubble is on her left.
            Assert.That(view.AvatarLooksLeft, Is.True);
            Assert.That(view.ConversationScreenRect.xMax, Is.LessThanOrEqualTo(view.AvatarScreenRect.xMin + 1f));
            var trinityX = Camera.main.WorldToScreenPoint(_game.Representations.Find(EntityId.Parse("character.trinity")).transform.position).x;
            Assert.That(trinityX, Is.InRange(view.ConversationScreenRect.xMax - 1f, view.AvatarScreenRect.xMin + 1f),
                "the gap between bubble and avatar is over the speaker: the bubble by her, the avatar behind her head");

            // Typing: the first press shows the line whole, the next one goes on.
            Assert.That(view.IsTyping, Is.True, "the line is typed letter by letter");
            _game.Input.AdvanceDialogue();
            Assert.That(view.IsTyping, Is.False);
            Assert.That(view.VisibleLetters, Is.EqualTo(view.ConversationText.Length));
            Assert.That(view.CurrentLine.Value.Speaker, Is.EqualTo(EntityId.Parse("character.trinity")), "still the same line");

            _game.Input.AdvanceDialogue();
            yield return null;
            Assert.That(view.CurrentLine.Value.Speaker, Is.EqualTo(Hero));
            AssertOnTheLine(view);
            Assert.That(view.AvatarLooksLeft, Is.False, "the hero looks right, at Trinity");
            Assert.That(view.ConversationScreenRect.xMin, Is.GreaterThanOrEqualTo(view.AvatarScreenRect.xMax - 1f));

            yield return FinishDialogue();
            Assert.That(view.IsConversationShown, Is.False);
            Assert.That(_game.WorldPause.IsPaused, Is.False);
            Assert.That(Errors(), Is.Empty);
        }

        /// <summary>Avatar and bubble bottoms stand on the location's conversation line, inside the screen.</summary>
        private void AssertOnTheLine(DialogueView view)
        {
            var camera = Camera.main;
            var root = _game.Representations.SceneRoot;
            var bottom = camera.transform.position.y - camera.orthographicSize;
            var line = camera.WorldToScreenPoint(new Vector3(0f, bottom + PixelGrid.Units(root.DialogueLine), 0f)).y;
            Assert.That(view.AvatarScreenRect.yMin, Is.EqualTo(line).Within(1f), "avatar on the line");
            Assert.That(view.ConversationScreenRect.yMin, Is.EqualTo(line).Within(1f), "bubble bottom on the line");
            Assert.That(view.ConversationScreenRect.xMin, Is.GreaterThanOrEqualTo(0f));
            Assert.That(view.ConversationScreenRect.xMax, Is.LessThanOrEqualTo(Screen.width));
            Assert.That(view.AvatarScreenRect.yMax, Is.LessThanOrEqualTo(Screen.height));
        }

        [UnityTest]
        public IEnumerator InventoryBarShowsSelectsAndDescribes()
        {
            yield return FinishDialogue();
            Assert.That(_game.InventoryView.Slots, Is.Empty);

            _game.StandBy("item.key");
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
                Assert.That(guard, Is.LessThan(40));
                _game.Input.AdvanceDialogue();
                yield return null;
            }
        }

        private static readonly EntityId Hero = EntityId.Parse("character.paranoik");

        private Vector2 Position(string id)
        {
            var view = _game.Representations.Find(EntityId.Parse(id));
            Assert.That(view, Is.Not.Null, id + " has no representation");
            return view.GetComponentInChildren<SpriteRenderer>().bounds.center; // the middle of the click area
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
