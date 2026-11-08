using System.Collections.Generic;
using System.IO;
using System.Linq;
using Paramatrix.Content;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
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

        // ---------------- checks ----------------

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

        private static void Print(ValidationReport report)
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
