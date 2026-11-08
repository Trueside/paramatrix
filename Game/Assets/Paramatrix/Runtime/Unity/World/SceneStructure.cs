using System.Collections.Generic;
using Paramatrix.Content;
using UnityEngine.SceneManagement;
using EntityId = Paramatrix.Content.EntityId;

namespace Paramatrix.Unity
{
    /// <summary>
    /// What a location scene contains and what is wrong with its structure. The single place of these rules:
    /// used by <see cref="SceneLoader"/> at runtime and by the editor project validator (stage 12).
    /// </summary>
    public sealed class SceneStructure
    {
        private readonly List<EntryPoint> _entries = new List<EntryPoint>();
        private readonly List<EntityView> _views = new List<EntityView>();
        private readonly List<string> _problems = new List<string>();

        private SceneStructure()
        {
        }

        public LocationRoot Root { get; private set; }

        public EntryPoint DefaultEntry { get; private set; }

        public IReadOnlyList<EntryPoint> Entries => _entries;

        public IReadOnlyList<EntityView> Views => _views;

        /// <summary>Structural problems: missing or duplicated LocationRoot, default entry point, entry names.</summary>
        public IReadOnlyList<string> Problems => _problems;

        public static SceneStructure Inspect(Scene scene)
        {
            var structure = new SceneStructure();
            var roots = new List<LocationRoot>();
            var names = new HashSet<string>();
            var defaults = 0;

            foreach (var gameObject in scene.GetRootGameObjects())
            {
                roots.AddRange(gameObject.GetComponentsInChildren<LocationRoot>(true));
                structure._views.AddRange(gameObject.GetComponentsInChildren<EntityView>(true));
                foreach (var point in gameObject.GetComponentsInChildren<EntryPoint>(true))
                {
                    structure._entries.Add(point);
                    if (!names.Add(point.EntryName))
                    {
                        structure._problems.Add($"entry point name '{point.EntryName}' is used twice");
                    }

                    if (point.IsDefault)
                    {
                        defaults++;
                        structure.DefaultEntry = point;
                    }
                }
            }

            if (roots.Count == 0)
            {
                structure._problems.Add("has no LocationRoot");
            }
            else if (roots.Count > 1)
            {
                structure._problems.Add("has more than one LocationRoot");
            }
            else
            {
                structure.Root = roots[0];
                if (structure.Root.FloorMinX >= structure.Root.FloorMaxX)
                {
                    structure._problems.Add("LocationRoot floor min X is not less than max X");
                }
            }

            if (defaults != 1)
            {
                structure._problems.Add($"must have exactly one default entry point, found {defaults}");
                structure.DefaultEntry = null;
            }

            return structure;
        }

        /// <summary>The entry point by name; null name = the default one. Null when there is no such point.</summary>
        public EntryPoint FindEntry(string name)
        {
            if (name == null)
            {
                return DefaultEntry;
            }

            foreach (var entry in _entries)
            {
                if (entry.EntryName == name)
                {
                    return entry;
                }
            }

            return null;
        }

        /// <summary>
        /// Why a view cannot represent anything (invalid or unknown ID, wrong view type), or null when it is valid.
        /// <paramref name="id"/> is set when the ID parses.
        /// </summary>
        public static string ViewProblem(EntityView view, ContentRegistry content, out EntityId id)
        {
            if (!EntityId.TryParse(view.RawEntityId, out id, out var error))
            {
                return "has an invalid entity ID: " + error;
            }

            if (!content.Exists(id))
            {
                return $"represents '{id}', which is not defined in content";
            }

            if (id.Kind != view.Kind)
            {
                return $"is a {view.GetType().Name} but '{id}' is a {EntityKinds.Prefix(id.Kind)}";
            }

            return null;
        }
    }
}
