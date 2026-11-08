using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Paramatrix.Content;
using Paramatrix.Unity;
using Paramatrix.Unity.Editor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Paramatrix.EditorTests
{
    /// <summary>Stage 12: editor validation of scenes against content, one broken setup per rule.</summary>
    public sealed class ProjectValidatorTests
    {
        private readonly List<Scene> _scenes = new List<Scene>();

        private static readonly string[] Fallbacks =
            new[] { "inspect", "primary", "take", "talk", "use", "apply_item", "combine_items", "exit" }
                .Select(t => "{ 'id': 'response.fallback_" + t + "', 'tier': 'fallback', 'interaction': '" + t + "', 'none': true }")
                .ToArray();

        private static ContentRegistry Content(params string[] extra)
        {
            var definitions = new List<string>
            {
                "{ 'id': 'game.settings', 'player_character': 'character.hero' }",
                "{ 'id': 'character.hero', 'name': 'Герой', 'initial_location': 'location.office', 'primary_interaction': 'talk' }",
                "{ 'id': 'location.office', 'scene': 'Office' }",
            };
            definitions.AddRange(Fallbacks);
            definitions.AddRange(extra);
            var json = "{ 'format_version': 1, 'definitions': [ " + string.Join(", ", definitions) + " ] }";
            var result = ContentLoader.Load(new[] { new ContentSource("test.json", json) });
            Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Diagnostics.Select(d => d.ToString())));
            return result.Registry;
        }

        [TearDown]
        public void CloseScenes()
        {
            foreach (var scene in _scenes)
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }

            _scenes.Clear();
        }

        // ---------------- scene building ----------------

        private Scene NewScene()
        {
            var scene = EditorSceneManager.NewPreviewScene(); // isolated, never saved, no clash with the open scene
            _scenes.Add(scene);
            return scene;
        }

        private static GameObject Make(Scene scene, string name)
        {
            var gameObject = new GameObject(name);
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            return gameObject;
        }

        private static void Location(Scene scene, string id, params (string Name, bool IsDefault)[] entries)
        {
            var root = Make(scene, "Location").AddComponent<LocationRoot>();
            SerializedFields.Set(root, "locationId", id);
            foreach (var (name, isDefault) in entries.Length == 0 ? new[] { ("default", true) } : entries)
            {
                var entry = Make(scene, "Entry " + name).AddComponent<EntryPoint>();
                SerializedFields.Set(entry, "entryName", name);
                SerializedFields.Set(entry, "isDefault", isDefault);
            }
        }

        private static T View<T>(Scene scene, string id, params string[] clips)
            where T : EntityView
        {
            var gameObject = Make(scene, "View " + id);
            if (typeof(T) == typeof(CharacterView))
            {
                gameObject.AddComponent<Walker>();
            }

            var view = gameObject.AddComponent<T>();
            SerializedFields.Set(view, "entityId", id);
            if (clips.Length > 0)
            {
                gameObject.AddComponent<EntityAnimations>().SetClips(clips.Select(c => new EntityAnimations.Clip { name = c, duration = 0.1f }));
            }

            return view;
        }

        private ValidationReport Validate(ContentRegistry content, Scene? bootstrap, params (string Name, Scene Scene)[] scenes)
        {
            var report = new ValidationReport();
            ProjectValidator.ValidateScenes(content, bootstrap, scenes.ToDictionary(s => s.Name, s => s.Scene), report);
            return report;
        }

        private static void AssertErrors(ValidationReport report, params string[] fragments)
        {
            var errors = report.Errors.Select(e => e.Message).ToArray();
            Assert.That(errors.Length, Is.EqualTo(fragments.Length), "errors:\n" + string.Join("\n", errors));
            foreach (var fragment in fragments)
            {
                Assert.That(errors.Any(e => e.Contains(fragment)), Is.True, $"no error containing '{fragment}':\n" + string.Join("\n", errors));
            }
        }

        private Scene Office(ContentRegistry content)
        {
            var office = NewScene();
            Location(office, "location.office");
            return office;
        }

        // ---------------- the real project ----------------

        [Test]
        public void RealProjectHasNoErrors()
        {
            var report = ProjectValidator.ValidateProject();
            Assert.That(report.ErrorCount, Is.Zero, string.Join("\n", report.Problems.Select(p => p.ToString())));
        }

        // ---------------- structure ----------------

        [Test]
        public void ValidSceneHasNoProblems()
        {
            var content = Content();
            AssertErrors(Validate(content, null, ("Office", Office(content))));
        }

        [Test]
        public void LocationSceneMustBeInBuildSettings() =>
            AssertErrors(Validate(Content(), null), "scene 'Office' is not in Build Settings");

        [Test]
        public void LocationRootRequired()
        {
            var scene = NewScene();
            var entry = Make(scene, "Entry").AddComponent<EntryPoint>();
            SerializedFields.Set(entry, "isDefault", true);

            AssertErrors(Validate(Content(), null, ("Office", scene)), "has no LocationRoot");
        }

        [Test]
        public void LocationRootMustMatchContent()
        {
            var scene = NewScene();
            Location(scene, "location.corridor");

            AssertErrors(Validate(Content(), null, ("Office", scene)), "says it is 'location.corridor'");
        }

        [Test]
        public void ExactlyOneDefaultEntryPoint()
        {
            var scene = NewScene();
            Location(scene, "location.office", ("a", true), ("b", true));

            AssertErrors(Validate(Content(), null, ("Office", scene)), "exactly one default entry point, found 2");
        }

        [Test]
        public void EntryNamesAreUnique()
        {
            var scene = NewScene();
            Location(scene, "location.office", ("door", true), ("door", false));

            AssertErrors(Validate(Content(), null, ("Office", scene)), "'door' is used twice");
        }

        // ---------------- representations ----------------

        [Test]
        public void UnknownEntityIdIsError()
        {
            var content = Content();
            var scene = Office(content);
            View<ObjectView>(scene, "object.lamp");

            AssertErrors(Validate(content, null, ("Office", scene)), "'object.lamp', which is not defined");
        }

        [Test]
        public void InvalidEntityIdIsError()
        {
            var content = Content();
            var scene = Office(content);
            View<ObjectView>(scene, "Lamp");

            AssertErrors(Validate(content, null, ("Office", scene)), "invalid entity ID");
        }

        [Test]
        public void ViewTypeMustMatchKind()
        {
            var content = Content("{ 'id': 'object.lamp', 'primary_interaction': 'primary' }");
            var scene = Office(content);
            View<ItemView>(scene, "object.lamp");

            AssertErrors(Validate(content, null, ("Office", scene)), "is a ItemView but 'object.lamp' is a object");
        }

        [Test]
        public void DuplicateRepresentationIsError()
        {
            var content = Content("{ 'id': 'object.lamp', 'primary_interaction': 'primary' }");
            var scene = Office(content);
            View<ObjectView>(scene, "object.lamp");
            View<ObjectView>(scene, "object.lamp");

            AssertErrors(Validate(content, null, ("Office", scene)), "two representations of 'object.lamp'");
        }

        [Test]
        public void HeroBelongsToBootstrap()
        {
            var content = Content();
            var scene = Office(content);
            var hero = View<CharacterView>(scene, "character.hero");
            SerializedFields.Set(hero, "isPlayer", true);

            AssertErrors(Validate(content, null, ("Office", scene)), "contains the player character");
        }

        [Test]
        public void BootstrapHeroMustBeThePlayerCharacter()
        {
            var content = Content("{ 'id': 'character.other', 'name': 'Другой', 'initial_location': 'none', 'primary_interaction': 'talk' }");
            var bootstrap = NewScene();
            Make(bootstrap, "Systems").AddComponent<GameBootstrap>();
            var hero = View<CharacterView>(bootstrap, "character.other");
            SerializedFields.Set(hero, "isPlayer", true);

            AssertErrors(Validate(content, bootstrap, ("Office", Office(content))), "but game.settings names 'character.hero'");
        }

        // ---------------- what content needs from scenes ----------------

        [Test]
        public void ItemNeedsViewWhereItLies()
        {
            var content = Content("{ 'id': 'item.key', 'name': 'Ключ', 'description': 'К.', 'initial_holder': 'location.office' }");

            AssertErrors(Validate(content, null, ("Office", Office(content))), "item.key can lie in location.office");
        }

        [Test]
        public void ItemNeedsViewWhereAnActionPutsIt()
        {
            var content = Content(
                "{ 'id': 'location.roof', 'scene': 'Roof' }",
                "{ 'id': 'item.key', 'name': 'Ключ', 'description': 'К.', 'initial_holder': 'inventory' }",
                "{ 'id': 'event.drop', 'execution': 'background', 'repeat': 'once', 'triggers': [], 'actions': [ { 'action': 'inventory', 'item': 'item.key', 'to': 'location.roof' } ] }");
            var roof = NewScene();
            Location(roof, "location.roof");

            AssertErrors(Validate(content, null, ("Office", Office(content)), ("Roof", roof)), "item.key can lie in location.roof");
        }

        [Test]
        public void CharacterNeedsViewWhereContentPutsIt()
        {
            var content = Content(
                "{ 'id': 'location.roof', 'scene': 'Roof' }",
                "{ 'id': 'character.trinity', 'name': 'Тринити', 'initial_location': 'location.office', 'primary_interaction': 'talk' }",
                "{ 'id': 'event.leave', 'execution': 'background', 'repeat': 'once', 'triggers': [], 'actions': [ { 'action': 'set_state', 'target': 'character.trinity', 'property': 'location', 'value': 'location.roof' } ] }");
            var office = Office(content);
            View<CharacterView>(office, "character.trinity");
            var roof = NewScene();
            Location(roof, "location.roof");

            AssertErrors(Validate(content, null, ("Office", office), ("Roof", roof)), "character.trinity can be in location.roof");
        }

        [Test]
        public void TransitionEntryMustExist()
        {
            var content = Content("{ 'id': 'event.go', 'execution': 'exclusive', 'repeat': 'once', 'triggers': [], 'actions': [ { 'action': 'transition', 'location': 'location.office', 'entry': 'window' } ] }");

            AssertErrors(Validate(content, null, ("Office", Office(content))), "uses entry point 'window'");
        }

        [Test]
        public void AnimationClipMustExist()
        {
            var content = Content(
                "{ 'id': 'object.lamp', 'primary_interaction': 'primary' }",
                "{ 'id': 'event.blink', 'execution': 'exclusive', 'repeat': 'once', 'triggers': [], 'actions': [ { 'action': 'animation', 'target': 'object.lamp', 'clip': 'blink' } ] }");
            var office = Office(content);
            View<ObjectView>(office, "object.lamp", "glow");

            AssertErrors(Validate(content, null, ("Office", office)), "has no animation 'blink'");
        }

        [Test]
        public void AnimatedEntityMustHaveARepresentation()
        {
            var content = Content(
                "{ 'id': 'object.lamp', 'primary_interaction': 'primary' }",
                "{ 'id': 'event.blink', 'execution': 'exclusive', 'repeat': 'once', 'triggers': [], 'actions': [ { 'action': 'if', 'condition': { 'target': 'character.hero', 'property': 'location', 'operator': 'equals', 'value': 'location.office' }, 'then': [ { 'action': 'animation', 'target': 'object.lamp', 'clip': 'blink' } ] } ] }");

            var report = Validate(content, null, ("Office", Office(content)));

            AssertErrors(report, "no representation in any scene");
            Assert.That(report.Problems.Count(p => p.Severity == Severity.Warning), Is.EqualTo(1), "the unreachable lamp is also a warning");
        }

        [Test]
        public void MoveNeedsRepresentations()
        {
            var content = Content(
                "{ 'id': 'object.lamp', 'primary_interaction': 'primary' }",
                "{ 'id': 'event.walk', 'execution': 'exclusive', 'repeat': 'once', 'triggers': [], 'actions': [ { 'action': 'move', 'character': 'character.hero', 'to': 'object.lamp' } ] }");

            AssertErrors(Validate(content, null, ("Office", Office(content))),
                "move needs character.hero", "move needs object.lamp");
        }
    }
}
