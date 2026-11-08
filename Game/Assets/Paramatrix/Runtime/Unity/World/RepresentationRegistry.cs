using System.Collections.Generic;
using Paramatrix.Content;
using Paramatrix.State;
using UnityEngine;
using UnityEngine.SceneManagement;
using EntityId = Paramatrix.Content.EntityId;

namespace Paramatrix.Unity
{
    /// <summary>
    /// Entity ID → Unity representations (Architecture 0.3 §3). Views are registered by the scene loader,
    /// never found by name. Representations follow Logical State: updated on every committed change,
    /// rebuilt after Restore and after a scene load (0.3.1 §8).
    /// </summary>
    public sealed class RepresentationRegistry
    {
        private readonly ContentRegistry _content;
        private readonly StateSystem _state;
        private readonly IDiagnosticsSink _diagnostics;
        private readonly Dictionary<EntityId, EntityView> _persistent = new Dictionary<EntityId, EntityView>();
        private readonly Dictionary<EntityId, EntityView> _scene = new Dictionary<EntityId, EntityView>();

        public RepresentationRegistry(ContentRegistry content, StateSystem state, IDiagnosticsSink diagnostics)
        {
            _content = content;
            _state = state;
            _diagnostics = diagnostics;
            _state.Changed += change => ApplyEntity(change.Address.Entity);
            _state.Restored += ApplyAll;
        }

        /// <summary>The location of the loaded scene; default when no location scene is loaded.</summary>
        public EntityId SceneLocation { get; private set; }

        public LocationRoot SceneRoot { get; private set; }

        /// <summary>Views that live outside location scenes (the hero in the Bootstrap scene).</summary>
        public bool RegisterPersistent(EntityView view) => Register(view, _persistent);

        /// <summary>Registers every view of a freshly loaded location scene. False when any view is invalid (reported).</summary>
        public bool RegisterScene(Scene scene, LocationRoot root, EntityId location)
        {
            _scene.Clear();
            SceneRoot = root;
            SceneLocation = location;

            var ok = true;
            foreach (var gameObject in scene.GetRootGameObjects())
            {
                foreach (var view in gameObject.GetComponentsInChildren<EntityView>(true))
                {
                    ok &= Register(view, _scene);
                }
            }

            ApplyAll();
            return ok;
        }

        public void UnregisterScene()
        {
            _scene.Clear();
            SceneRoot = null;
            SceneLocation = default;
        }

        /// <summary>The view of an entity in the current scene or the persistent set; null when there is none.</summary>
        public EntityView Find(EntityId id)
        {
            if (_scene.TryGetValue(id, out var view) || _persistent.TryGetValue(id, out view))
            {
                return view;
            }

            return null;
        }

        public IEnumerable<EntityView> SceneViews => _scene.Values;

        /// <summary>Every registered view: persistent ones (the hero) and those of the current scene.</summary>
        public IEnumerable<EntityView> AllViews
        {
            get
            {
                foreach (var view in _persistent.Values)
                {
                    yield return view;
                }

                foreach (var view in _scene.Values)
                {
                    yield return view;
                }
            }
        }

        private bool Register(EntityView view, Dictionary<EntityId, EntityView> into)
        {
            var problem = SceneStructure.ViewProblem(view, _content, out var id);
            if (problem != null)
            {
                Report(view, problem);
                return false;
            }

            if (into.ContainsKey(id) || (into != _persistent && _persistent.ContainsKey(id)))
            {
                Report(view, $"duplicates the representation of '{id}'");
                return false;
            }

            view.Bind(id);
            into.Add(id, view);
            return true;
        }

        private void ApplyEntity(EntityId id)
        {
            var view = Find(id);
            if (view != null)
            {
                view.Apply(_state, SceneLocation);
            }
        }

        private void ApplyAll()
        {
            foreach (var view in _persistent.Values)
            {
                view.Apply(_state, SceneLocation);
            }

            foreach (var view in _scene.Values)
            {
                view.Apply(_state, SceneLocation);
            }
        }

        private void Report(EntityView view, string problem) =>
            _diagnostics.Report(new Diagnostic(
                DiagnosticCode.UnknownReference,
                Severity.Error,
                $"Representation '{PathOf(view.transform)}' in scene '{view.gameObject.scene.name}' {problem}.",
                SourceLocation.None));

        private static string PathOf(Transform transform) =>
            transform.parent == null ? transform.name : PathOf(transform.parent) + "/" + transform.name;
    }
}
