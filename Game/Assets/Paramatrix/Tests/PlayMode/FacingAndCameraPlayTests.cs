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
    /// <summary>
    /// Who looks where (arrival at a character, entering a location through a door) and the camera of a scrolling
    /// location with its parallax layers (В-19).
    /// </summary>
    public sealed class FacingAndCameraPlayTests
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
            while (_game.DialogueView.CurrentLine.HasValue)
            {
                _game.Input.AdvanceDialogue(); // the intro line
                yield return null;
            }

            yield return WaitUntil(() => _game.Session.Flow.Owner == ControlOwner.Player, "intro finished");
        }

        [UnityTearDown]
        public IEnumerator RestoreTime()
        {
            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTest]
        public IEnumerator HeroTalksFromWhereHeStandsAndBothTurn()
        {
            // Trinity looks left; the hero stands on her right within reach: he talks from there, both turn (Р11).
            var trinity = (CharacterView)_game.Representations.Find(EntityId.Parse("character.trinity"));
            Assert.That(trinity.Walker.Facing, Is.EqualTo(Facing.Left));
            var stand = trinity.CenterX + PixelGrid.Units(96);
            _game.Hero.Walker.PlaceAt(stand);
            yield return null;

            _game.Input.ClickAt(trinity.transform.position, PointerButton.Primary);
            yield return WaitUntil(() => _game.DialogueView.IsConversationShown, "Trinity dialogue");

            Assert.That(_game.Hero.X, Is.EqualTo(stand).Within(0.001f), "the hero does not walk");
            Assert.That(_game.Hero.Walker.Facing, Is.EqualTo(Facing.Left), "the hero looks at her");
            Assert.That(trinity.Walker.Facing, Is.EqualTo(Facing.Right), "she turned to the hero");
            Assert.That(trinity.GetComponent<SpriteRenderer>().flipX, Is.False, "drawn looking right, so looking right is not mirrored");
            yield return null; // the avatar is placed in LateUpdate
            Assert.That(_game.DialogueView.AvatarLooksLeft, Is.False, "her avatar looks where she looks: at the hero (Р10)");
            Assert.That(_game.DialogueView.ConversationScreenRect.xMin, Is.GreaterThanOrEqualTo(_game.DialogueView.AvatarScreenRect.xMax - 1f),
                "her bubble is on the side she looks at");
            var herX = Camera.main.WorldToScreenPoint(trinity.transform.position).x;
            Assert.That(herX, Is.InRange(_game.DialogueView.AvatarScreenRect.xMax - 1f, _game.DialogueView.ConversationScreenRect.xMin + 1f),
                "the gap between avatar and bubble is over her");
            Assert.That(Errors(), Is.Empty);
        }

        [UnityTest]
        public IEnumerator FromHerLeftBothTurnToEachOther()
        {
            var trinity = (CharacterView)_game.Representations.Find(EntityId.Parse("character.trinity"));
            trinity.Walker.Face(Facing.Right); // looking away from where the hero stands
            var stand = trinity.CenterX - PixelGrid.Units(96);
            _game.Hero.Walker.PlaceAt(stand);
            _game.Hero.Walker.Face(Facing.Left); // and he looks away from her
            yield return null;

            _game.Input.ClickAt(trinity.transform.position, PointerButton.Primary);
            yield return WaitUntil(() => _game.DialogueView.IsConversationShown, "Trinity dialogue");

            Assert.That(_game.Hero.X, Is.EqualTo(stand).Within(0.001f));
            Assert.That(_game.Hero.Walker.Facing, Is.EqualTo(Facing.Right));
            Assert.That(trinity.Walker.Facing, Is.EqualTo(Facing.Left), "she turned to the hero");
            Assert.That(Errors(), Is.Empty);
        }

        [UnityTest]
        public IEnumerator OutOfReachALeftClickDoesNothing()
        {
            var trinity = (CharacterView)_game.Representations.Find(EntityId.Parse("character.trinity"));
            var stand = trinity.CenterX - PixelGrid.Units(HeroController.InteractionRadius + 60);
            _game.Hero.Walker.PlaceAt(stand);
            yield return null;
            var thought = _game.DialogueView.MonologueText(_game.Hero.View.Id); // the intro thought may still be shown

            _game.Input.ClickAt(trinity.transform.position, PointerButton.Primary);
            for (var i = 0; i < 5; i++)
            {
                yield return null;
            }

            Assert.That(_game.DialogueView.IsConversationShown, Is.False, "too far: no conversation (Р11)");
            Assert.That(_game.DialogueView.MonologueText(_game.Hero.View.Id), Is.EqualTo(thought), "and no monologue");
            Assert.That(_game.DialogueView.MonologueText(trinity.Id), Is.Null);
            Assert.That(_game.Hero.X, Is.EqualTo(stand).Within(0.001f), "the hero is not walked to her");
            Assert.That(_game.Session.Flow.Owner, Is.EqualTo(ControlOwner.Player));
            Assert.That(Errors(), Is.Empty);
        }

        [UnityTest]
        public IEnumerator AWalkingHeroStopsWhenASceneStarts()
        {
            // The hero still has speed from walking when the click comes: he stops at once and turns (the crash at Mr Srit).
            var trinity = (CharacterView)_game.Representations.Find(EntityId.Parse("character.trinity"));
            _game.Hero.Walker.PlaceAt(trinity.CenterX - PixelGrid.Units(96));
            yield return null;
            _game.Input.Walk(-1f, 0.1f); // walking away from her, at speed
            Assert.That(_game.Hero.Walker.IsMoving, Is.True);

            _game.Input.ClickAt(trinity.transform.position, PointerButton.Primary);

            Assert.That(_game.Hero.Walker.IsMoving, Is.False, "stopped where he was");
            Assert.That(_game.Hero.Walker.Facing, Is.EqualTo(Facing.Right), "turned to her");
            yield return WaitUntil(() => _game.DialogueView.IsConversationShown, "Trinity dialogue");
            Assert.That(Errors(), Is.Empty);
        }

        [UnityTest]
        public IEnumerator EnteringThroughADoorTheHeroLooksIntoTheRoom()
        {
            yield return Load("Location_Corridor", "from_hall");
            Assert.That(_game.Hero.X, Is.EqualTo(PixelGrid.Units(336)).Within(0.001f));
            Assert.That(_game.Hero.Walker.Facing, Is.EqualTo(Facing.Left), "back to the hall door on the right");
            Assert.That(_game.WorldCamera.X, Is.EqualTo(0f).Within(0.001f), "a one-screen location does not scroll");
            Assert.That(_game.WorldCamera.transform.position.y, Is.EqualTo(PixelGrid.Units(270 - 72)).Within(0.001f), "placeholder floor: 72 grid pixels");

            yield return Load("Location_Corridor", "from_office");
            Assert.That(_game.Hero.Walker.Facing, Is.EqualTo(Facing.Right), "back to the office door on the left");
            Assert.That(Errors(), Is.Empty);
        }

        [UnityTest]
        public IEnumerator LongLocationScrollsWithTheHeroAndMovesItsLayers()
        {
            yield return Load("Location_Hall", "from_corridor");
            var half = PixelGrid.Units(PixelGrid.Width) / 2f;
            var structure = SceneStructure.Inspect(SceneManager.GetSceneByName("Location_Hall"));
            var root = structure.Root;
            Assert.That(_game.WorldCamera.X, Is.EqualTo(root.ViewMinX + half).Within(0.001f), "at the left end the camera stops at the edge");

            Assert.That(_game.WorldCamera.transform.position.y, Is.EqualTo(PixelGrid.Units(270 - root.FloorDepth)).Within(0.001f), "the floor line at the location's floor depth");
            Assert.That(root.FloorDepth, Is.EqualTo(100), "the street's pavement");

            var near = structure.Layers.Single(l => Mathf.Approximately(l.Follow, -0.5f));
            var nearAtCenter = 0f; // the builder places layers for the camera in the middle of the view (x = 0)

            // The middle of the screen is a dead zone: the camera stands while the hero walks there.
            var start = _game.WorldCamera.X;
            var reach = half - PixelGrid.Units(WorldCamera.PushMargin);
            _game.Hero.Walker.PlaceAt(start + reach - PixelGrid.Units(10));
            yield return null;
            Assert.That(_game.WorldCamera.X, Is.EqualTo(start).Within(0.001f), "inside the dead zone the screen does not move");

            // Past it the hero pushes the screen: the camera keeps him a push margin from the edge.
            _game.Hero.Walker.PlaceAt(start + reach + PixelGrid.Units(144));
            yield return null;
            Assert.That(_game.WorldCamera.X, Is.EqualTo(start + PixelGrid.Units(144)).Within(0.001f), "pushed by as much as he went past");
            var shift = _game.WorldCamera.X - root.ViewCenterX;
            Assert.That(near.transform.position.x, Is.EqualTo(nearAtCenter - (Mathf.Round(shift * 0.5f * PixelGrid.PixelsPerUnit) / PixelGrid.PixelsPerUnit)).Within(0.001f),
                "a -0.5 layer moves half the camera's way back");

            // Walking back inside the dead zone does not pull the screen back.
            _game.Hero.Walker.PlaceAt(_game.WorldCamera.X);
            var pushed = _game.WorldCamera.X;
            yield return null;
            Assert.That(_game.WorldCamera.X, Is.EqualTo(pushed).Within(0.001f), "back in the middle: the screen stays");

            _game.Hero.Walker.PlaceAt(root.FloorMaxX);
            yield return null;
            Assert.That(_game.WorldCamera.X, Is.EqualTo(root.ViewMaxX - half).Within(0.001f), "at the right end the camera stops at the edge");
            Assert.That(_game.WorldCamera.X * PixelGrid.PixelsPerUnit, Is.EqualTo(Mathf.Round(_game.WorldCamera.X * PixelGrid.PixelsPerUnit)).Within(0.001f), "on whole grid pixels");
            Assert.That(Errors(), Is.Empty);
        }

        [UnityTest]
        public IEnumerator MrSritLooksAtTheHeroFromEitherSide()
        {
            yield return Load("Location_Hall", "from_corridor");
            var srit = (CharacterView)Find("character.mr_srit");
            foreach (var (offsetPixels, side) in new[] { (96, Facing.Right), (-96, Facing.Left) })
            {
                _game.Hero.Walker.PlaceAt(srit.CenterX + PixelGrid.Units(offsetPixels));
                yield return null;
                _game.Input.ClickAt(srit.transform.position, PointerButton.Primary);
                yield return WaitUntil(() => _game.DialogueView.IsConversationShown, "Mr Srit dialogue");

                Assert.That(srit.Walker.Facing, Is.EqualTo(side), "Mr Srit looks at the hero");
                Assert.That(srit.GetComponent<SpriteRenderer>().flipX, Is.EqualTo(side == Facing.Left), "drawn looking right: looking left is mirrored");
                Assert.That(_game.Hero.Walker.Facing, Is.Not.EqualTo(side), "the hero looks back at him");
                while (_game.DialogueView.CurrentLine.HasValue)
                {
                    _game.Input.AdvanceDialogue();
                    yield return null;
                }

                yield return WaitUntil(() => _game.Session.Flow.Owner == ControlOwner.Player, "dialogue finished");
            }

            Assert.That(Errors(), Is.Empty);
        }

        [UnityTest]
        public IEnumerator CharactersAreDrawnWholeAndTallerThanTheHero()
        {
            var hero = _game.Hero.GetComponent<SpriteRenderer>().bounds.size.y;
            foreach (var (scene, id) in new[] { ("Location_Office", "character.trinity"), ("Location_Corridor", "character.morpheus"), ("Location_Hall", "character.mr_srit"), ("Location_Hall", "character.fem_paranoik") })
            {
                yield return Load(scene, "default");
                var sprite = Find(id).GetComponent<SpriteRenderer>().sprite;
                Assert.That(sprite.rect.size, Is.EqualTo(new Vector2(sprite.texture.width, sprite.texture.height)), id + ": the sprite is the whole picture");
                Assert.That(sprite.bounds.size.y, Is.GreaterThan(hero), id + " is taller than the hero");
                Assert.That(Find(id).GetComponent<SpriteRenderer>().bounds.min.y, Is.EqualTo(0f).Within(0.001f), id + " stands on the floor line");
            }
        }

        // ---------------- helpers ----------------

        private EntityView Find(string id)
        {
            var view = _game.Representations.Find(EntityId.Parse(id));
            Assert.That(view, Is.Not.Null, id + " has no representation");
            return view;
        }

        /// <summary>Loads a location scene through the scene loader alone (presentation only, Scenario is not involved).</summary>
        private IEnumerator Load(string scene, string entry)
        {
            bool? ok = null;
            string error = null;
            _game.Scenes.LoadLocationScene(scene, entry, (success, message) =>
            {
                ok = success;
                error = message;
            });
            yield return WaitUntil(() => ok.HasValue, scene);
            Assert.That(ok, Is.True, error);

            // The scene was loaded directly: put the hero there in Logical State too, as a transition does, so that
            // conversations find their speakers in the hero's location (Р10).
            var location = _game.Session.Registry.GetByKind(EntityKind.Location).Cast<LocationDefinition>().Single(l => l.Scene == scene);
            var hero = new StateAddress(_game.Session.Registry.Game.PlayerCharacter, BuiltInStates.Location);
            _game.Session.State.Set(hero, StateValue.Enum(location.Id.Value), Paramatrix.State.MutationSource.System);
            yield return null;
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
