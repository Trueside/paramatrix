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
        private readonly List<ParallaxLayer> _layers = new List<ParallaxLayer>();
        private readonly List<string> _problems = new List<string>();

        private SceneStructure()
        {
        }

        public LocationRoot Root { get; private set; }

        public EntryPoint DefaultEntry { get; private set; }

        public IReadOnlyList<EntryPoint> Entries => _entries;

        public IReadOnlyList<EntityView> Views => _views;

        public IReadOnlyList<ParallaxLayer> Layers => _layers;

        /// <summary>
        /// Structural problems: missing or duplicated LocationRoot, floor or view extents, default entry point, entry
        /// names and facings, parallax layers and entities placed on them.
        /// </summary>
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
                foreach (var layer in gameObject.GetComponentsInChildren<ParallaxLayer>(true))
                {
                    structure._layers.Add(layer);
                    if (layer.Problem != null)
                    {
                        structure._problems.Add(layer.Problem);
                    }

                    if (layer.GetComponentsInChildren<EntityView>(true).Length > 0)
                    {
                        structure._problems.Add($"parallax layer '{layer.name}' carries an entity: clickable things stay on the floor plane");
                    }
                }

                foreach (var point in gameObject.GetComponentsInChildren<EntryPoint>(true))
                {
                    structure._entries.Add(point);
                    if (!names.Add(point.EntryName))
                    {
                        structure._problems.Add($"entry point name '{point.EntryName}' is used twice");
                    }

                    if (point.Facing != Facing.Left && point.Facing != Facing.Right)
                    {
                        structure._problems.Add($"entry point '{point.EntryName}' does not say where the hero looks");
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

                var root = structure.Root;
                if (root.ViewMaxX - root.ViewMinX < (2f * WorldCamera.HalfWidth) - 0.001f)
                {
                    structure._problems.Add($"LocationRoot view {root.ViewMinX}..{root.ViewMaxX} is narrower than one screen ({2f * WorldCamera.HalfWidth} units)");
                }

                if (root.FloorDepth < 0 || root.FloorDepth >= PixelGrid.Height)
                {
                    structure._problems.Add($"LocationRoot floor depth {root.FloorDepth} is not within the screen (0..{PixelGrid.Height - 1})");
                }

                // Р10: avatars and conversation bubbles stand on this line, above the floor, and fit under the top.
                if (root.DialogueLine <= root.FloorDepth || root.DialogueLine + DialogueView.AvatarHeightGridPixels > PixelGrid.Height)
                {
                    structure._problems.Add($"LocationRoot conversation line {root.DialogueLine} is not between the floor line ({root.FloorDepth}) " +
                                            $"and {PixelGrid.Height - DialogueView.AvatarHeightGridPixels} (a {DialogueView.AvatarHeightGridPixels}-pixel avatar must fit above it)");
                }

                if (root.FloorMinX < root.ViewMinX || root.FloorMaxX > root.ViewMaxX)
                {
                    structure._problems.Add("LocationRoot floor reaches past its view: the hero could walk off screen");
                }

                foreach (var entry in structure._entries)
                {
                    var x = entry.transform.position.x;
                    if (x < root.FloorMinX || x > root.FloorMaxX)
                    {
                        structure._problems.Add($"entry point '{entry.EntryName}' at X {x} is off the floor");
                    }
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
