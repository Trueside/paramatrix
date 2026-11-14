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
    /// The locations are linked as the content says: the hero walks the whole map through its exits (clicks, approach,
    /// Scenario transitions) and arrives where each exit leads, looking the way the entry point says.
    /// </summary>
    public sealed class WorldMapPlayTests
    {
        private const float Timeout = 15f;
        private GameBootstrap _game;

        // exit clicked -> scene it leads to
        private static readonly (string Exit, string Scene)[] Route =
        {
            ("object.office_door", "Location_Corridor"),
            ("object.corridor_hall_door", "Location_Hall"),
            ("object.hall_east_edge", "Location_NiiStreet"),
            ("object.nii_east_edge", "Location_DkStreet"),
            ("object.dk_entrance", "Location_Bar"),
            ("object.bar_curtain", "Location_DkStreet"),
            ("object.dk_east_edge", "Location_FactoryStreet"),
            ("object.factory_gates", "Location_ShopStreet"),
            ("object.shop_door", "Location_Workshop"),
            ("object.workshop_door", "Location_ShopStreet"),
            ("object.shop_gates", "Location_FactoryStreet"),
            ("object.factory_west_edge", "Location_DkStreet"),
            ("object.dk_west_edge", "Location_NiiStreet"),
            ("object.nii_west_edge", "Location_Hall"),
            ("object.hall_door", "Location_Corridor"),
            ("object.corridor_door", "Location_Office"),
            ("object.office_door", "Location_Corridor"),
            ("object.corridor_hall_door", "Location_Hall"),
            ("object.hall_east_edge", "Location_NiiStreet"),
            ("object.nii_entrance", "Location_Office"),
        };

        [UnitySetUp]
        public IEnumerator LoadBootstrap()
        {
            SceneManager.LoadScene("Bootstrap", LoadSceneMode.Single);
            yield return null;
            yield return WaitUntil(() => (_game = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>()) != null && (_game.IsReady || _game.HasFailed), "game start");
            Assert.That(_game.HasFailed, Is.False, string.Join(Environment.NewLine, Errors()));
            yield return FinishDialogue();
            yield return WaitUntil(PlayerHasControl, "intro finished");
        }

        [UnityTearDown]
        public IEnumerator RestoreTime()
        {
            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTest]
        public IEnumerator HeroWalksTheWholeMapThroughItsExits()
        {
            // The room's door is locked: take the key from the floor and open the door with it first.
            var key = EntityId.Parse("item.key");
            _game.StandBy(_game.Representations.Find(key));
            _game.Input.ClickAt(Middle(_game.Representations.Find(key)), PointerButton.Primary);
            yield return WaitUntil(() => _game.Session.Inventory.IsCarried(key) && PlayerHasControl(), "key taken");
            _game.Session.Interaction.ClickInventoryItem(key, PointerButton.Primary);
            _game.StandBy("object.office_door");
            _game.Input.ClickAt(Middle(_game.Representations.Find(EntityId.Parse("object.office_door"))), PointerButton.Primary);
            yield return WaitUntil(() => !_game.Session.State.Get(StateAddress.Parse("object.office_door", "locked")).AsBoolean && PlayerHasControl(), "door unlocked");

            foreach (var (exit, scene) in Route)
            {
                var view = _game.Representations.Find(EntityId.Parse(exit));
                Assert.That(view, Is.Not.Null, exit + " is not in " + _game.Scenes.CurrentScene);
                var middle = Middle(view);
                Assert.That(_game.Input.ViewAt(middle), Is.SameAs(view), exit + " takes the click");
                _game.StandBy(view); // the player walks up to the exit
                _game.Input.ClickAt(middle, PointerButton.Primary);
                yield return WaitUntil(() => _game.Scenes.CurrentScene == scene && PlayerHasControl(), exit + " -> " + scene);
                yield return FinishDialogue();

                var root = SceneStructure.Inspect(SceneManager.GetSceneByName(scene)).Root;
                Assert.That(_game.Hero.X, Is.InRange(root.FloorMinX, root.FloorMaxX), "on the floor of " + scene);
            }

            Assert.That(Errors(), Is.Empty);
        }

        /// <summary>The middle of a view's click area, as a player clicks (its bottom edge may lie on a fraction of a unit).</summary>
        private static Vector2 Middle(EntityView view) => view.GetComponentInChildren<SpriteRenderer>().bounds.center;

        private bool PlayerHasControl() => _game.Session.Flow.Owner == ControlOwner.Player;

        private IEnumerator FinishDialogue()
        {
            while (_game.DialogueView.CurrentLine.HasValue)
            {
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
