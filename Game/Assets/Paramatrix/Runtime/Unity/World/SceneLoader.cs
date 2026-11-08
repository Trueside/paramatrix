using System;
using Paramatrix.Content;
using Paramatrix.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;
using EntityId = Paramatrix.Content.EntityId;

namespace Paramatrix.Unity
{
    /// <summary>
    /// Persistent Bootstrap scene + one additive scene per location (Architecture 0.3 §3.2): unloads the current
    /// location scene, loads the next, checks its structure, registers its representations and places the hero.
    /// Any structural problem fails the load with an explicit error (Р0).
    /// </summary>
    public sealed class SceneLoader : ISceneLoader
    {
        private readonly HeroController _hero;
        private ContentRegistry _content;
        private RepresentationRegistry _representations;
        private string _currentScene;
        private bool _busy;

        public SceneLoader(HeroController hero)
        {
            _hero = hero;
        }

        public string CurrentScene => _currentScene;

        public void Bind(ContentRegistry content, RepresentationRegistry representations)
        {
            _content = content;
            _representations = representations;
        }

        public void LoadLocationScene(string scene, string entry, Action<bool, string> done)
        {
            if (_busy)
            {
                done(false, "another scene load is in progress");
                return;
            }

            if (!Application.CanStreamedLevelBeLoaded(scene))
            {
                done(false, $"scene '{scene}' is not in Build Settings");
                return;
            }

            _busy = true;
            if (_currentScene == null)
            {
                LoadNew(scene, entry, done);
                return;
            }

            _representations.UnregisterScene();
            var unloading = SceneManager.UnloadSceneAsync(_currentScene);
            _currentScene = null;
            if (unloading == null)
            {
                _busy = false;
                done(false, "the previous location scene could not be unloaded");
                return;
            }

            unloading.completed += _ => LoadNew(scene, entry, done);
        }

        private void LoadNew(string sceneName, string entry, Action<bool, string> done)
        {
            var loading = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (loading == null)
            {
                _busy = false;
                done(false, $"scene '{sceneName}' could not be loaded");
                return;
            }

            loading.completed += _ =>
            {
                _busy = false;
                var scene = SceneManager.GetSceneByName(sceneName);
                _currentScene = sceneName;
                SceneManager.SetActiveScene(scene);

                if (!Prepare(scene, sceneName, entry, out var error))
                {
                    done(false, error);
                    return;
                }

                done(true, null);
            };
        }

        private bool Prepare(Scene scene, string sceneName, string entry, out string error)
        {
            var structure = SceneStructure.Inspect(scene);
            if (structure.Problems.Count > 0)
            {
                error = $"scene '{sceneName}' " + string.Join("; ", structure.Problems);
                return false;
            }

            var expected = FindLocationOf(sceneName);
            if (!expected.IsValid || structure.Root.LocationId != expected.Value)
            {
                error = $"scene '{sceneName}' says it is '{structure.Root.LocationId}', content maps it to '{expected}'";
                return false;
            }

            var chosen = structure.FindEntry(entry);
            if (chosen == null)
            {
                error = $"scene '{sceneName}' has no entry point '{entry}'";
                return false;
            }

            if (!_representations.RegisterScene(scene, structure.Root, expected))
            {
                error = $"scene '{sceneName}' has invalid representations (see the errors above)";
                return false;
            }

            _hero.EnterScene(structure.Root, chosen.transform.position.x);
            error = null;
            return true;
        }

        private EntityId FindLocationOf(string sceneName)
        {
            foreach (var definition in _content.GetByKind(EntityKind.Location))
            {
                if (((LocationDefinition)definition).Scene == sceneName)
                {
                    return definition.Id;
                }
            }

            return default;
        }
    }
}
