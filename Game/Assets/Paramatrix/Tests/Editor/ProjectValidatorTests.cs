using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Paramatrix.Content;
using Paramatrix.Unity;
using Paramatrix.Unity.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
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
            SerializedFields.Set(root, "dialogueLine", DemoSceneBuilder.DialogueLine);
            foreach (var (name, isDefault) in entries.Length == 0 ? new[] { ("default", true) } : entries)
            {
                var entry = Make(scene, "Entry " + name).AddComponent<EntryPoint>();
                SerializedFields.Set(entry, "entryName", name);
                SerializedFields.Set(entry, "isDefault", isDefault);
                SerializedFields.Set(entry, "facing", (int)Facing.Right);
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
            SerializedFields.Set(entry, "facing", (int)Facing.Left);

            AssertErrors(Validate(Content(), null, ("Office", scene)), "has no LocationRoot");
        }

        [Test]
        public void EntryPointMustSayWhereTheHeroLooks()
        {
            var scene = NewScene();
            Location(scene, "location.office");
            var entry = Make(scene, "Entry door").AddComponent<EntryPoint>();
            SerializedFields.Set(entry, "entryName", "door");

            AssertErrors(Validate(Content(), null, ("Office", scene)), "'door' does not say where the hero looks");
        }

        [Test]
        public void EntryPointMustBeOnTheFloor()
        {
            var scene = NewScene();
            Location(scene, "location.office", ("default", true), ("far", false));
            scene.GetRootGameObjects().Single(g => g.name == "Entry far").transform.position = new Vector3(9f, 0f, 0f);

            AssertErrors(Validate(Content(), null, ("Office", scene)), "'far' at X 9 is off the floor");
        }

        [Test]
        public void ViewIsAtLeastOneScreenAndHoldsTheFloor()
        {
            var narrow = NewScene();
            Location(narrow, "location.office");
            var root = narrow.GetRootGameObjects().Single(g => g.name == "Location").GetComponent<LocationRoot>();
            SerializedFields.Set(root, "viewMinX", -9f);
            SerializedFields.Set(root, "viewMaxX", 9f);
            SerializedFields.Set(root, "floorMinX", -9f);
            SerializedFields.Set(root, "floorMaxX", 9f);
            AssertErrors(Validate(Content(), null, ("Office", narrow)), "narrower than one screen");

            var wide = NewScene();
            Location(wide, "location.office");
            root = wide.GetRootGameObjects().Single(g => g.name == "Location").GetComponent<LocationRoot>();
            SerializedFields.Set(root, "floorMaxX", 11f);
            AssertErrors(Validate(Content(), null, ("Office", wide)), "floor reaches past its view");
        }

        [Test]
        public void ParallaxLayerFollowsLessThanTheCameraAndCarriesNoEntities()
        {
            var content = Content("{ 'id': 'object.door', 'primary_interaction': 'exit' }");
            var scene = Office(content);
            var horizon = Make(scene, "Horizon").AddComponent<ParallaxLayer>();
            SerializedFields.Set(horizon, "follow", 1f);
            var far = Make(scene, "Far").AddComponent<ParallaxLayer>();
            SerializedFields.Set(far, "follow", 0.5f);
            View<ObjectView>(scene, "object.door").transform.SetParent(far.transform);

            AssertErrors(Validate(content, null, ("Office", scene)), "'Horizon' follows the camera by 1", "'Far' carries an entity");
        }

        [TestCase(0)]
        [TestCase(72)]
        [TestCase(PixelGrid.Height - DialogueView.AvatarHeightGridPixels + 1)]
        public void ConversationLineIsAboveTheFloorAndLeavesRoomForTheAvatar(int line)
        {
            var scene = NewScene();
            Location(scene, "location.office");
            SerializedFields.Set(scene.GetRootGameObjects().Single(g => g.name == "Location").GetComponent<LocationRoot>(), "dialogueLine", line);

            AssertErrors(Validate(Content(), null, ("Office", scene)), $"conversation line {line} is not between");
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

        // ---------------- pixel grid ----------------

        private const string PlaceholderSprite = "Assets/Paramatrix/Art/Placeholder.png";

        private static ValidationReport ValidateGrid(Scene? bootstrap, params Scene[] scenes)
        {
            var report = new ValidationReport();
            ProjectValidator.ValidatePixelGrid(bootstrap, scenes, report);
            return report;
        }

        private static PixelPerfectCamera GridCamera(Scene scene)
        {
            var gameObject = Make(scene, "Main Camera");
            gameObject.tag = "MainCamera";
            var camera = gameObject.AddComponent<PixelPerfectCamera>();
            camera.assetsPPU = PixelGrid.PixelsPerUnit;
            camera.refResolutionX = PixelGrid.Width;
            camera.refResolutionY = PixelGrid.Height;
            camera.gridSnapping = PixelPerfectCamera.GridSnapping.UpscaleRenderTexture;
            camera.cropFrame = PixelPerfectCamera.CropFrame.StretchFill;
            SerializedFields.Set(camera, "m_FilterMode", (int)PixelPerfectCamera.PixelPerfectFilterMode.RetroAA);
            return camera;
        }

        [Test]
        public void GridCameraAsTheStandardSaysIsValid()
        {
            var bootstrap = NewScene();
            GridCamera(bootstrap);
            AssertErrors(ValidateGrid(bootstrap, bootstrap));
        }

        [Test]
        public void BootstrapNeedsAPixelPerfectCamera()
        {
            var bootstrap = NewScene();
            Make(bootstrap, "Main Camera").AddComponent<Camera>();
            AssertErrors(ValidateGrid(bootstrap, bootstrap), "exactly one PixelPerfectCamera");
        }

        [Test]
        public void GridCameraSettingsMustMatchTheStandard()
        {
            var bootstrap = NewScene();
            var camera = GridCamera(bootstrap);
            camera.assetsPPU = 100;
            camera.refResolutionX = 640;
            camera.refResolutionY = 360;
            camera.gridSnapping = PixelPerfectCamera.GridSnapping.PixelSnapping;
            camera.cropFrame = PixelPerfectCamera.CropFrame.Windowbox;
            SerializedFields.Set(camera, "m_FilterMode", (int)PixelPerfectCamera.PixelPerfectFilterMode.Point);
            camera.gameObject.tag = "Untagged";

            AssertErrors(ValidateGrid(bootstrap, bootstrap),
                "assets pixels per unit", "reference resolution", "grid snapping", "crop frame", "filter mode", "not on the MainCamera");
        }

        [Test]
        public void DynamicLightIsAnError()
        {
            var office = NewScene();
            Make(office, "Lamp").AddComponent<Light2D>();
            AssertErrors(ValidateGrid(null, office), "has a 2D light 'Lamp'");
        }

        [Test]
        public void ArtFolderSpriteIsOnTheGrid()
        {
            var office = NewScene();
            Make(office, "Door").AddComponent<SpriteRenderer>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PlaceholderSprite);
            AssertErrors(ValidateGrid(null, office));
        }

        [Test]
        public void HandPlacedSpritesStayOnTheGrid()
        {
            var office = NewScene();
            var art = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Paramatrix/Art/Paranoik_Stand.png");
            var placeholder = AssetDatabase.LoadAssetAtPath<Sprite>(PlaceholderSprite);

            var good = Make(office, "Desk");
            good.AddComponent<SpriteRenderer>().sprite = art;
            good.transform.position = new Vector3(5f / PixelGrid.PixelsPerUnit, -3f / PixelGrid.PixelsPerUnit, 0f);
            var hotspot = Make(office, "Hotspot");
            hotspot.AddComponent<SpriteRenderer>().sprite = placeholder;
            hotspot.transform.localScale = new Vector3(4f, 7f, 1f); // the placeholder may be stretched

            var between = Make(office, "Lamp");
            between.AddComponent<SpriteRenderer>().sprite = art;
            between.transform.position = new Vector3(0.01f, 0f, 0f);
            var scaled = Make(office, "Shelf");
            scaled.AddComponent<SpriteRenderer>().sprite = art;
            scaled.transform.localScale = new Vector3(1.5f, 1.5f, 1f);
            var rotated = Make(office, "Picture");
            rotated.AddComponent<SpriteRenderer>().sprite = art;
            rotated.transform.rotation = Quaternion.Euler(0f, 0f, 10f);

            AssertErrors(ValidateGrid(null, office), "'Lamp' in scene", "'Shelf' in scene", "'Picture' in scene");
        }

        [Test]
        public void SpriteWithoutPixelArtImportIsAnError()
        {
            var office = NewScene();
            var texture = new Texture2D(4, 4);
            var renderer = Make(office, "Door").AddComponent<SpriteRenderer>();
            renderer.sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.one / 2f, PixelGrid.PixelsPerUnit);
            try
            {
                AssertErrors(ValidateGrid(null, office), "Sprite of 'Door' in scene");
            }
            finally
            {
                Object.DestroyImmediate(renderer.sprite);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void ArtFolderImportRuleMatchesItsCheck()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(PlaceholderSprite);
            Assert.That(PixelArtImport.Problem(importer), Is.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<Sprite>(PlaceholderSprite).pixelsPerUnit, Is.EqualTo(PixelGrid.PixelsPerUnit));
        }
    }
}
