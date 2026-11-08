using System.Linq;
using Paramatrix.Content;
using UnityEditor;
using UnityEngine;

namespace Paramatrix.Unity.Editor
{
    /// <summary>Inspector of every representation: says at once when its entity ID is invalid, unknown or of the wrong kind.</summary>
    [CustomEditor(typeof(EntityView), true)]
    [CanEditMultipleObjects]
    public sealed class EntityViewEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var content = EditorContentCache.Registry;
            if (content == null)
            {
                EditorGUILayout.HelpBox(EditorContentCache.Problem ?? "Content is not loaded.", MessageType.Warning);
                return;
            }

            foreach (var view in targets.OfType<EntityView>())
            {
                var problem = SceneStructure.ViewProblem(view, content, out _);
                if (problem != null)
                {
                    EditorGUILayout.HelpBox($"'{view.name}' {problem}.", MessageType.Error);
                }
            }
        }
    }

    /// <summary>
    /// Content registry for editor tooling, loaded from the manifest on demand and dropped whenever a content file
    /// or the manifest changes. Editor-only cache; game logic never uses it.
    /// </summary>
    public static class EditorContentCache
    {
        private const string ManifestPath = "Assets/Paramatrix/ContentManifest.asset";
        private static bool _loaded;
        private static ContentRegistry _registry;

        public static string Problem { get; private set; }

        public static ContentRegistry Registry
        {
            get
            {
                if (!_loaded)
                {
                    Load();
                }

                return _registry;
            }
        }

        public static void Invalidate()
        {
            _loaded = false;
            _registry = null;
            Problem = null;
        }

        private static void Load()
        {
            _loaded = true;
            var manifest = AssetDatabase.LoadAssetAtPath<ContentManifest>(ManifestPath);
            if (manifest == null)
            {
                Problem = "No content manifest: run Paramatrix > Collect Content.";
                return;
            }

            try
            {
                var result = ContentLoader.Load(manifest.ToSources());
                _registry = result.Registry;
                Problem = result.Succeeded ? null : "Content has errors: run Paramatrix > Validate Project.";
            }
            catch (System.InvalidOperationException exception)
            {
                Problem = exception.Message;
            }
        }

        private sealed class Watcher : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if (imported.Concat(deleted).Concat(moved).Any(p => p.EndsWith(".json") || p == ManifestPath))
                {
                    Invalidate();
                }
            }
        }
    }
}
