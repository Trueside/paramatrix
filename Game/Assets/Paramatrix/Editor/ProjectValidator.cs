using System.Collections.Generic;
using System.IO;
using System.Linq;
using Paramatrix.Content;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using EntityId = Paramatrix.Content.EntityId;

namespace Paramatrix.Unity.Editor
{
    public sealed class ValidationProblem
    {
        public ValidationProblem(Severity severity, string message, Object context)
        {
            Severity = severity;
            Message = message;
            Context = context;
        }

        public Severity Severity { get; }

        public string Message { get; }

        /// <summary>The asset or scene object to select when the Console entry is clicked.</summary>
        public Object Context { get; }

        public override string ToString() => (Severity == Severity.Error ? "error: " : "warning: ") + Message;
    }

    public sealed class ValidationReport
    {
        private readonly List<ValidationProblem> _problems = new List<ValidationProblem>();

        public IReadOnlyList<ValidationProblem> Problems => _problems;

        public int ErrorCount => _problems.Count(p => p.Severity == Severity.Error);

        public IEnumerable<ValidationProblem> Errors => _problems.Where(p => p.Severity == Severity.Error);

        internal void Error(string message, Object context = null) => _problems.Add(new ValidationProblem(Severity.Error, message, context));

        internal void Warning(string message, Object context = null) => _problems.Add(new ValidationProblem(Severity.Warning, message, context));
    }

    /// <summary>
    /// Checks in the editor what content validation cannot see (plan 0.2, stage 12): the manifest is in sync with the
    /// content folder, every location has a correctly built scene, every representation has a valid ID, and everything
    /// the scenes need from Unity exists — entry points, animation clips, views where characters and items can be.
    /// The same runtime rules (<see cref="SceneStructure"/>) are reused; nothing here is a second interpretation.
    /// </summary>
    public static class ProjectValidator
    {
        private const string ContentFolder = "Assets/Paramatrix/Content";
        private const string ManifestPath = "Assets/Paramatrix/ContentManifest.asset";

        [MenuItem("Paramatrix/Validate Project")]
        public static void ValidateFromMenu() => Print(ValidateProject());

        /// <summary>Batch: Unity -executeMethod Paramatrix.Unity.Editor.ProjectValidator.ValidateBatch (exit code 1 on errors).</summary>
        public static void ValidateBatch()
        {
            var report = ValidateProject();
            Print(report);
            EditorApplication.Exit(report.ErrorCount == 0 ? 0 : 1);
        }

        public static ValidationReport ValidateProject()
        {
            var report = new ValidationReport();
            var manifest = AssetDatabase.LoadAssetAtPath<ContentManifest>(ManifestPath);
            if (manifest == null)
            {
                report.Error($"Content manifest {ManifestPath} is missing: run Paramatrix > Collect Content.");
                return report;
            }

            CheckManifest(manifest, report);
            if (report.ErrorCount > 0)
            {
                return report;
            }

            var loaded = ContentLoader.Load(manifest.ToSources());
            foreach (var diagnostic in loaded.Diagnostics)
            {
                var asset = diagnostic.Location.Source == null ? null : AssetDatabase.LoadAssetAtPath<TextAsset>(diagnostic.Location.Source);
                var message = $"{diagnostic.Code}: {diagnostic.Message} [{diagnostic.Location}]";
                if (diagnostic.Severity == Severity.Error)
                {
                    report.Error(message, asset);
                }
                else
                {
                    report.Warning(message, asset);
                }
            }

            if (!loaded.Succeeded)
            {
                return report; // scene checks need valid content
            }

            CheckAvatars(loaded.Registry, manifest, report);
            CheckNovelPictures(loaded.Registry, manifest, report);

            var opened = new List<Scene>();
            try
            {
                var scenes = new Dictionary<string, Scene>();
                Scene? bootstrap = null;
                foreach (var entry in EditorBuildSettings.scenes.Where(s => s.enabled))
                {
                    var scene = SceneManager.GetSceneByPath(entry.path);
                    if (!scene.isLoaded)
                    {
                        scene = EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Additive);
                        opened.Add(scene);
                    }

                    scenes[Path.GetFileNameWithoutExtension(entry.path)] = scene;
                    if (FindInScene<GameBootstrap>(scene).Count > 0)
                    {
                        bootstrap = scene;
                    }
                }

                if (!bootstrap.HasValue)
                {
                    report.Error("No enabled Build Settings scene contains a GameBootstrap.");
                }

                ValidateScenes(loaded.Registry, bootstrap, scenes, report);
                ValidatePixelGrid(bootstrap, scenes.Values, report);
            }
            finally
            {
                foreach (var scene in opened)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }

            return report;
        }

        /// <summary>
        /// Core checks over already opened scenes. <paramref name="scenes"/> maps scene names (as content refers to them)
        /// to scenes; a location whose scene is absent is reported as not in Build Settings.
        /// </summary>
        public static void ValidateScenes(ContentRegistry content, Scene? bootstrap, IReadOnlyDictionary<string, Scene> scenes, ValidationReport report)
        {
            // Every view of every scene, by entity, for the cross-checks below.
            var viewsByEntity = new Dictionary<EntityId, List<EntityView>>();
            var viewsByLocation = new Dictionary<EntityId, HashSet<EntityId>>();
            var structures = new Dictionary<EntityId, SceneStructure>();

            if (bootstrap.HasValue)
            {
                ValidateBootstrap(content, bootstrap.Value, report, viewsByEntity);
            }

            foreach (var definition in content.GetByKind(EntityKind.Location))
            {
                var location = (LocationDefinition)definition;
                if (!scenes.TryGetValue(location.Scene, out var scene))
                {
                    report.Error($"{location.Id}: scene '{location.Scene}' is not in Build Settings (enabled).");
                    continue;
                }

                var structure = SceneStructure.Inspect(scene);
                structures[location.Id] = structure;
                foreach (var problem in structure.Problems)
                {
                    report.Error($"Scene '{location.Scene}' {problem}.", structure.Root);
                }

                if (structure.Root != null && structure.Root.LocationId != location.Id.Value)
                {
                    report.Error($"Scene '{location.Scene}' says it is '{structure.Root.LocationId}', content maps it to '{location.Id}'.", structure.Root);
                }

                var here = new HashSet<EntityId>();
                viewsByLocation[location.Id] = here;
                foreach (var view in structure.Views)
                {
                    var problem = SceneStructure.ViewProblem(view, content, out var id);
                    if (problem != null)
                    {
                        report.Error($"Representation '{view.name}' in scene '{location.Scene}' {problem}.", view);
                        continue;
                    }

                    if (!here.Add(id))
                    {
                        report.Error($"Scene '{location.Scene}' has two representations of '{id}'.", view);
                    }

                    if (view is CharacterView character && character.IsPlayer)
                    {
                        report.Error($"Scene '{location.Scene}' contains the player character; the hero lives in the Bootstrap scene.", view);
                    }

                    Add(viewsByEntity, id, view);
                }
            }

            CheckWherePresenceIsNeeded(content, viewsByLocation, report);
            CheckActionNeeds(content, structures, viewsByEntity, report);
            CheckReachability(content, viewsByEntity, report);
        }

        /// <summary>
        /// The pixel grid (Visual Production Standard 0.3, В-06, В-20, В-22): the Bootstrap camera renders the world on
        /// the 960×540 grid and scales it as the standard says; every sprite in every scene is a pixel-grid sprite;
        /// no scene has dynamic 2D lights.
        /// </summary>
        public static void ValidatePixelGrid(Scene? bootstrap, IEnumerable<Scene> scenes, ValidationReport report)
        {
            if (bootstrap.HasValue)
            {
                ValidateCamera(bootstrap.Value, report);
            }

            foreach (var scene in scenes.OrderBy(s => s.path))
            {
                foreach (var light in FindInScene<Light2D>(scene))
                {
                    report.Error($"Scene '{scene.name}' has a 2D light '{light.name}': the game has no dynamic lighting, light is drawn into the art (В-20).", light);
                }

                foreach (var renderer in FindInScene<SpriteRenderer>(scene).Where(r => r.sprite != null))
                {
                    var path = AssetDatabase.GetAssetPath(renderer.sprite);
                    var problem = PixelArtImport.Problem(AssetImporter.GetAtPath(path) as TextureImporter);
                    if (problem != null)
                    {
                        report.Error($"Sprite of '{renderer.name}' in scene '{scene.name}' is not on the pixel grid: {problem}.", renderer);
                    }

                    PlacementProblems(renderer, path, scene, report);
                }
            }

            // Characters stand on the floor line (world y = 0): the bottom of the sprite, where the feet are.
            var all = bootstrap.HasValue ? scenes.Append(bootstrap.Value) : scenes;
            foreach (var scene in all.Distinct().OrderBy(s => s.path))
            {
                foreach (var character in FindInScene<CharacterView>(scene))
                {
                    foreach (var renderer in character.GetComponentsInChildren<SpriteRenderer>(true).Where(r => r.sprite != null))
                    {
                        var feet = renderer.bounds.min.y * PixelGrid.PixelsPerUnit;
                        if (Mathf.Abs(feet) > 0.01f)
                        {
                            report.Error($"Character '{character.name}' in scene '{scene.name}' does not stand on the floor line: its feet are {feet:0.#} grid pixels off.", renderer);
                        }
                    }
                }
            }
        }

        /// <summary>The placeholder rectangle: the only sprite that may be scaled (blocks, invisible click areas).</summary>
        public const string PlaceholderPath = PixelArtImport.ArtFolder + "Placeholder.png";

        /// <summary>
        /// A sprite placed by hand must lie on the grid (В-06): at whole grid pixels, not scaled (art is drawn at its
        /// size; only the placeholder rectangle is stretched), not rotated.
        /// </summary>
        public static void PlacementProblems(SpriteRenderer renderer, string spritePath, Scene scene, ValidationReport report)
        {
            var transform = renderer.transform;
            var position = transform.position;
            if (!PixelGridTools.OnGrid(position.x) || !PixelGridTools.OnGrid(position.y))
            {
                report.Error($"'{renderer.name}' in scene '{scene.name}' is not on whole grid pixels: X {position.x * PixelGrid.PixelsPerUnit:0.##}, Y {position.y * PixelGrid.PixelsPerUnit:0.##} px (Paramatrix > Snap Selection To Pixel Grid).", renderer);
            }

            if (transform.rotation != Quaternion.identity)
            {
                report.Error($"'{renderer.name}' in scene '{scene.name}' is rotated: pixel art is never rotated.", renderer);
            }

            if (spritePath != PlaceholderPath && (transform.lossyScale - Vector3.one).sqrMagnitude > 0.000001f)
            {
                report.Error($"'{renderer.name}' in scene '{scene.name}' is scaled {transform.lossyScale}: art is drawn at its size, make the sprite the right size instead.", renderer);
            }
        }

        private static void ValidateCamera(Scene scene, ValidationReport report)
        {
            var cameras = FindInScene<PixelPerfectCamera>(scene);
            if (cameras.Count != 1)
            {
                report.Error($"Scene '{scene.name}' must have exactly one PixelPerfectCamera on the world camera, found {cameras.Count}.");
                return;
            }

            var camera = cameras[0];
            var filter = (PixelPerfectCamera.PixelPerfectFilterMode)new SerializedObject(camera).FindProperty("m_FilterMode").intValue;
            void Expect<T>(string what, T actual, T expected)
            {
                if (!EqualityComparer<T>.Default.Equals(actual, expected))
                {
                    report.Error($"PixelPerfectCamera '{camera.name}': {what} is {actual}, the pixel grid needs {expected}.", camera);
                }
            }

            Expect("assets pixels per unit", camera.assetsPPU, PixelGrid.PixelsPerUnit);
            Expect("reference resolution", new Vector2Int(camera.refResolutionX, camera.refResolutionY), new Vector2Int(PixelGrid.Width, PixelGrid.Height));
            Expect("grid snapping", camera.gridSnapping, PixelPerfectCamera.GridSnapping.UpscaleRenderTexture);
            Expect("crop frame", camera.cropFrame, PixelPerfectCamera.CropFrame.StretchFill);
            Expect("filter mode", filter, PixelPerfectCamera.PixelPerfectFilterMode.RetroAA);
            if (!camera.CompareTag("MainCamera"))
            {
                report.Error($"PixelPerfectCamera '{camera.name}' is not on the MainCamera.", camera);
            }
        }

        // ---------------- checks ----------------

        /// <summary>
        /// Avatars (Р10): the manifest lists exactly the pictures in the avatar folder; each belongs to a declared emotion
        /// of an existing character and is exactly <see cref="DialogueView.AvatarWidth"/>×<see cref="DialogueView.AvatarHeight"/> (all avatars one size); every declared emotion has a picture.
        /// </summary>
        public static void CheckAvatars(ContentRegistry content, ContentManifest manifest, ValidationReport report)
        {
            string Key(ContentManifest.Avatar a) => a.characterId + "/" + a.emotion + "/" + AssetDatabase.GetAssetPath(a.sprite);
            var onDisk = DemoSceneBuilder.FindAvatars();
            if (!onDisk.Select(Key).SequenceEqual(manifest.Avatars.Select(Key)))
            {
                report.Error($"Avatars in the content manifest differ from {DemoSceneBuilder.AvatarsFolder}: run Paramatrix > Collect Content.", manifest);
                return;
            }

            foreach (var avatar in manifest.Avatars)
            {
                if (avatar.sprite == null)
                {
                    report.Error($"Avatar '{avatar.emotion}' of {avatar.characterId} is not a sprite.", manifest);
                    continue;
                }

                var path = AssetDatabase.GetAssetPath(avatar.sprite);
                if (avatar.characterId.Length == 0)
                {
                    report.Error($"Avatar '{path}' is not in {DemoSceneBuilder.AvatarsFolder}/<name>/emotions/.", avatar.sprite);
                    continue;
                }

                if (!EntityId.TryParse(avatar.characterId, out var id, out _) || !content.TryGet<CharacterDefinition>(id, out var character))
                {
                    report.Error($"Avatar '{path}': there is no {avatar.characterId} in content (the folder name is the character ID without 'character.').", avatar.sprite);
                    continue;
                }

                if (!character.HasEmotion(avatar.emotion))
                {
                    report.Error($"Avatar '{path}': {id} does not declare emotion '{avatar.emotion}' (declared: {string.Join(", ", character.Emotions)}).", avatar.sprite);
                }

                var size = avatar.sprite.rect.size;
                if ((int)size.x != DialogueView.AvatarWidth || (int)size.y != DialogueView.AvatarHeight)
                {
                    report.Error($"Avatar '{path}' is {size.x}x{size.y}: every avatar is exactly {DialogueView.AvatarWidth}x{DialogueView.AvatarHeight} (tools/art/prepare-avatars.ps1).", avatar.sprite);
                }

                var importProblem = PixelArtImport.AvatarProblem(AssetImporter.GetAtPath(path) as TextureImporter);
                if (importProblem != null)
                {
                    report.Error($"Avatar '{path}' is not imported as an avatar: {importProblem} (reimport it).", avatar.sprite);
                }
            }

            foreach (var definition in content.GetByKind(EntityKind.Character))
            {
                var character = (CharacterDefinition)definition;
                foreach (var emotion in character.Emotions)
                {
                    if (manifest.FindAvatar(character.Id.Value, emotion) == null)
                    {
                        report.Error($"{character.Id} declares emotion '{emotion}', but {DemoSceneBuilder.AvatarsFolder}/{character.Id.Name}/emotions/{emotion}.png is missing.",
                            Asset(character.Source));
                    }
                }
            }
        }

        /// <summary>
        /// Novel pictures (Р12): the manifest lists exactly the pictures in the novel folders; each belongs to a declared
        /// emotion of an existing character and is exactly <see cref="DialogueView.NovelPictureWidth"/>×<see cref="DialogueView.NovelPictureHeight"/>;
        /// the partner of every novel has a picture for default and for every emotion of its lines.
        /// </summary>
        public static void CheckNovelPictures(ContentRegistry content, ContentManifest manifest, ValidationReport report)
        {
            string Key(ContentManifest.Avatar a) => a.characterId + "/" + a.emotion + "/" + AssetDatabase.GetAssetPath(a.sprite);
            if (!DemoSceneBuilder.FindNovelPictures().Select(Key).SequenceEqual(manifest.NovelPictures.Select(Key)))
            {
                report.Error($"Novel pictures in the content manifest differ from {DemoSceneBuilder.AvatarsFolder}/<name>/novel: run Paramatrix > Collect Content.", manifest);
                return;
            }

            foreach (var picture in manifest.NovelPictures)
            {
                if (picture.sprite == null)
                {
                    report.Error($"Novel picture '{picture.emotion}' of {picture.characterId} is not a sprite.", manifest);
                    continue;
                }

                var path = AssetDatabase.GetAssetPath(picture.sprite);
                if (!EntityId.TryParse(picture.characterId, out var id, out _) || !content.TryGet<CharacterDefinition>(id, out var character))
                {
                    report.Error($"Novel picture '{path}': there is no {picture.characterId} in content (the folder name is the character ID without 'character.').", picture.sprite);
                    continue;
                }

                if (!character.HasEmotion(picture.emotion))
                {
                    report.Error($"Novel picture '{path}': {id} does not declare emotion '{picture.emotion}' (declared: {string.Join(", ", character.Emotions)}).", picture.sprite);
                }

                var size = picture.sprite.rect.size;
                if ((int)size.x != DialogueView.NovelPictureWidth || (int)size.y != DialogueView.NovelPictureHeight)
                {
                    report.Error($"Novel picture '{path}' is {size.x}x{size.y}: every novel picture is exactly {DialogueView.NovelPictureWidth}x{DialogueView.NovelPictureHeight} (tools/art/prepare-avatars.ps1 -Kind novel).", picture.sprite);
                }

                var importProblem = PixelArtImport.AvatarProblem(AssetImporter.GetAtPath(path) as TextureImporter);
                if (importProblem != null)
                {
                    report.Error($"Novel picture '{path}' is not imported in screen pixels: {importProblem} (reimport it).", picture.sprite);
                }
            }

            foreach (var definition in content.GetByKind(EntityKind.Dialogue))
            {
                var dialogue = (DialogueDefinition)definition;
                if (dialogue.Mode != DialogueMode.Novel)
                {
                    continue;
                }

                var partner = dialogue.Partner.Value;
                var emotions = new SortedSet<string>(System.StringComparer.Ordinal) { Emotions.Default };
                CollectEmotions(dialogue.Steps, partner, emotions);
                foreach (var emotion in emotions)
                {
                    if (manifest.FindNovelPicture(partner.Value, emotion) == null)
                    {
                        report.Error($"{dialogue.Id} shows {partner} as '{emotion}', but {DemoSceneBuilder.AvatarsFolder}/{partner.Name}/novel/{emotion}.png is missing.",
                            Asset(dialogue.Source));
                    }
                }
            }
        }

        private static void CollectEmotions(IReadOnlyList<DialogueStep> steps, EntityId speaker, SortedSet<string> emotions)
        {
            foreach (var step in steps)
            {
                if (step is DialogueLine line && line.Speaker == speaker)
                {
                    emotions.Add(line.Emotion);
                }
                else if (step is DialogueChoice choice)
                {
                    foreach (var option in choice.Options)
                    {
                        CollectEmotions(option.Steps, speaker, emotions);
                    }
                }
            }
        }

        private static void CheckManifest(ContentManifest manifest, ValidationReport report)
        {
            var onDisk = AssetDatabase.FindAssets("t:TextAsset", new[] { ContentFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".json"))
                .ToHashSet();
            var listed = new HashSet<string>();
            foreach (var entry in manifest.Entries)
            {
                if (entry.asset == null)
                {
                    report.Error($"Content manifest entry '{entry.path}' has no asset: run Paramatrix > Collect Content.", manifest);
                    continue;
                }

                var path = AssetDatabase.GetAssetPath(entry.asset);
                if (path != entry.path)
                {
                    report.Error($"Content manifest entry '{entry.path}' points to '{path}': run Paramatrix > Collect Content.", manifest);
                }

                listed.Add(path);
            }

            foreach (var path in onDisk.Where(p => !listed.Contains(p)).OrderBy(p => p))
            {
                report.Error($"Content file '{path}' is not in the manifest, so the game never loads it: run Paramatrix > Collect Content.",
                    AssetDatabase.LoadAssetAtPath<TextAsset>(path));
            }
        }

        private static void ValidateBootstrap(ContentRegistry content, Scene scene, ValidationReport report, Dictionary<EntityId, List<EntityView>> viewsByEntity)
        {
            var bootstraps = FindInScene<GameBootstrap>(scene);
            if (bootstraps.Count != 1)
            {
                report.Error($"Scene '{scene.name}' must contain exactly one GameBootstrap, found {bootstraps.Count}.");
            }

            var heroes = FindInScene<CharacterView>(scene).Where(v => v.IsPlayer).ToList();
            if (heroes.Count != 1)
            {
                report.Error($"Scene '{scene.name}' must contain exactly one player CharacterView, found {heroes.Count}.");
                return;
            }

            var hero = heroes[0];
            var problem = SceneStructure.ViewProblem(hero, content, out var id);
            if (problem != null)
            {
                report.Error($"Hero '{hero.name}' {problem}.", hero);
                return;
            }

            if (id != content.Game.PlayerCharacter)
            {
                report.Error($"Hero '{hero.name}' represents '{id}', but game.settings names '{content.Game.PlayerCharacter}'.", hero);
            }

            Add(viewsByEntity, id, hero);
        }

        /// <summary>Wherever content can put a character or an item, that location's scene must be able to show it.</summary>
        private static void CheckWherePresenceIsNeeded(ContentRegistry content, Dictionary<EntityId, HashSet<EntityId>> viewsByLocation, ValidationReport report)
        {
            var places = new SortedDictionary<EntityId, SortedSet<EntityId>>();
            void Need(EntityId entity, string place)
            {
                if (!StatePlaceIsLocation(place, out var location))
                {
                    return;
                }

                if (!places.TryGetValue(entity, out var set))
                {
                    places.Add(entity, set = new SortedSet<EntityId>());
                }

                set.Add(location);
            }

            foreach (var definition in content.All)
            {
                if (definition is CharacterDefinition character && character.Id != content.Game.PlayerCharacter)
                {
                    Need(character.Id, character.LocationState.Default.AsEnum);
                }
                else if (definition is ItemDefinition item)
                {
                    Need(item.Id, item.HolderState.Default.AsEnum);
                }
            }

            foreach (var action in AllActions(content))
            {
                switch (action.Action)
                {
                    case InventoryAction inventory:
                        Need(inventory.Item, inventory.To);
                        break;
                    case SetStateAction set when set.Target.Property == BuiltInStates.Location && set.Target.Entity.Kind == EntityKind.Character:
                        Need(set.Target.Entity, set.Value.AsEnum);
                        break;
                }
            }

            foreach (var pair in places)
            {
                foreach (var location in pair.Value)
                {
                    if (viewsByLocation.TryGetValue(location, out var views) && !views.Contains(pair.Key))
                    {
                        var what = pair.Key.Kind == EntityKind.Item ? "lie" : "be";
                        report.Error($"{pair.Key} can {what} in {location}, but its scene has no representation of it.",
                            Asset(content.Get<Definition>(pair.Key).Source));
                    }
                }
            }
        }

        /// <summary>What actions ask of Unity: entry points, animation clips, views to walk or animate.</summary>
        private static void CheckActionNeeds(
            ContentRegistry content,
            Dictionary<EntityId, SceneStructure> structures,
            Dictionary<EntityId, List<EntityView>> viewsByEntity,
            ValidationReport report)
        {
            foreach (var (owner, action) in AllActions(content))
            {
                var context = Asset(action.Source);
                switch (action)
                {
                    case TransitionAction transition:
                        if (structures.TryGetValue(transition.Location, out var structure) && structure.FindEntry(transition.Entry) == null)
                        {
                            report.Error($"{owner}: transition to {transition.Location} uses entry point '{transition.Entry ?? "default"}', which its scene does not have [{action.Source}].", context);
                        }

                        break;

                    case AnimationAction animation:
                        if (!viewsByEntity.TryGetValue(animation.Target, out var animated))
                        {
                            report.Error($"{owner}: animation '{animation.Clip}' targets {animation.Target}, which has no representation in any scene [{action.Source}].", context);
                            break;
                        }

                        foreach (var view in animated)
                        {
                            var clips = view.GetComponent<EntityAnimations>();
                            if (clips == null || clips.Clips.All(c => c.name != animation.Clip))
                            {
                                report.Error($"{owner}: representation '{view.name}' of {animation.Target} has no animation '{animation.Clip}' [{action.Source}].", view);
                            }
                        }

                        break;

                    case MoveAction move:
                        foreach (var entity in new[] { move.Character, move.To })
                        {
                            if (!viewsByEntity.ContainsKey(entity))
                            {
                                report.Error($"{owner}: move needs {entity}, which has no representation in any scene [{action.Source}].", context);
                            }
                        }

                        break;
                }
            }
        }

        /// <summary>Things the player is meant to click must exist somewhere on screen.</summary>
        private static void CheckReachability(ContentRegistry content, Dictionary<EntityId, List<EntityView>> viewsByEntity, ValidationReport report)
        {
            foreach (var definition in content.GetByKind(EntityKind.Object))
            {
                if (!viewsByEntity.ContainsKey(definition.Id))
                {
                    report.Warning($"{definition.Id} has no representation in any scene: the player can never interact with it.", Asset(definition.Source));
                }
            }
        }

        // ---------------- helpers ----------------

        private static IEnumerable<(EntityId Owner, ActionDefinition Action)> AllActions(ContentRegistry content)
        {
            foreach (var definition in content.GetByKind(EntityKind.Event))
            {
                var ev = (EventDefinition)definition;
                foreach (var action in Flatten(ev.Actions))
                {
                    yield return (ev.Id, action);
                }
            }
        }

        private static IEnumerable<ActionDefinition> Flatten(IEnumerable<ActionDefinition> actions)
        {
            foreach (var action in actions)
            {
                yield return action;
                if (action is IfAction branch)
                {
                    foreach (var inner in Flatten(branch.Then).Concat(Flatten(branch.Else)))
                    {
                        yield return inner;
                    }
                }
            }
        }

        private static bool StatePlaceIsLocation(string place, out EntityId location)
        {
            location = default;
            return place != null && EntityId.TryParse(place, out location, out _) && location.Kind == EntityKind.Location;
        }

        private static void Add(Dictionary<EntityId, List<EntityView>> map, EntityId id, EntityView view)
        {
            if (!map.TryGetValue(id, out var list))
            {
                map.Add(id, list = new List<EntityView>());
            }

            list.Add(view);
        }

        private static List<T> FindInScene<T>(Scene scene)
            where T : Component =>
            scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToList();

        private static Object Asset(SourceLocation source) =>
            source.Source == null ? null : AssetDatabase.LoadAssetAtPath<TextAsset>(source.Source);

        public static void Print(ValidationReport report)
        {
            foreach (var problem in report.Problems)
            {
                if (problem.Severity == Severity.Error)
                {
                    Debug.LogError("[Paramatrix] " + problem.Message, problem.Context);
                }
                else
                {
                    Debug.LogWarning("[Paramatrix] " + problem.Message, problem.Context);
                }
            }

            var warnings = report.Problems.Count - report.ErrorCount;
            Debug.Log($"[Paramatrix] Project validation: {report.ErrorCount} error(s), {warnings} warning(s).");
        }
    }
}
