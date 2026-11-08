using System;
using Paramatrix.Content;
using Paramatrix.Gameplay;
using UnityEngine;
using EntityId = Paramatrix.Content.EntityId;

namespace Paramatrix.Unity
{
    /// <summary>
    /// The player character's body in the Bootstrap scene: free walking within the floor of the current location
    /// and the approach walk before interactions (T13, T19). Decides nothing about the story.
    /// </summary>
    [RequireComponent(typeof(CharacterView))]
    public sealed class HeroController : MonoBehaviour, IHeroNavigation
    {
        private RepresentationRegistry _representations;
        private float _minX = -8f;
        private float _maxX = 8f;

        public Walker Walker => GetComponent<Walker>();

        public CharacterView View => GetComponent<CharacterView>();

        public float X => transform.position.x;

        public void Bind(RepresentationRegistry representations) => _representations = representations;

        /// <summary>Called by the scene loader after a location scene is ready.</summary>
        public void EnterScene(LocationRoot root, float entryX)
        {
            _minX = root.FloorMinX;
            _maxX = root.FloorMaxX;
            Walker.PlaceAt(Mathf.Clamp(entryX, _minX, _maxX));
        }

        /// <summary>Player keyboard input for this frame (-1..1). Interrupts any target walk.</summary>
        public void Walk(float direction, float deltaTime)
        {
            if (Mathf.Approximately(direction, 0f))
            {
                return;
            }

            Walker.Stop();
            Walker.Step(direction, _minX, _maxX, deltaTime);
        }

        public void Approach(EntityId target, Action<ApproachOutcome, string> done)
        {
            var view = _representations.Find(target);
            if (view == null || !view.IsShown)
            {
                done(ApproachOutcome.Failed, $"{target} is not shown in the current scene");
                return;
            }

            Walker.WalkTo(Mathf.Clamp(view.ApproachX, _minX, _maxX), () => done(ApproachOutcome.Arrived, null));
        }

        public void Cancel() => Walker.Stop();
    }
}
