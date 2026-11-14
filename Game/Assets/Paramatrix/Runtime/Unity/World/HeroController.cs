using System;
using Paramatrix.Content;
using Paramatrix.Gameplay;
using UnityEngine;
using EntityId = Paramatrix.Content.EntityId;

namespace Paramatrix.Unity
{
    /// <summary>
    /// The player character's body in the Bootstrap scene: free walking within the floor of the current location (T19)
    /// and his reach (Р11): the player walks him; a left click counts only on what is within
    /// <see cref="InteractionRadius"/>. Decides nothing about the story.
    /// </summary>
    [RequireComponent(typeof(CharacterView))]
    public sealed class HeroController : MonoBehaviour, IHeroReach
    {
        /// <summary>
        /// How far the hero reaches, grid pixels, from his middle to the nearer edge of what is drawn of the target
        /// (Р11; a character of usual width is reached up to about 130 pixels from its middle).
        /// </summary>
        public const int InteractionRadius = 108;

        private RepresentationRegistry _representations;
        private float _minX = -8f;
        private float _maxX = 8f;

        public Walker Walker => GetComponent<Walker>();

        public CharacterView View => GetComponent<CharacterView>();

        public float X => transform.position.x;

        public void Bind(RepresentationRegistry representations) => _representations = representations;

        /// <summary>
        /// Called by the scene loader after a location scene is ready: the hero stands at the entry point and looks
        /// the way it says (into the room, back to the door he came through).
        /// </summary>
        public void EnterScene(LocationRoot root, EntryPoint entry)
        {
            _minX = root.FloorMinX;
            _maxX = root.FloorMaxX;
            Walker.PlaceAt(entry.transform.position.x); // on the floor: SceneStructure checks it
            Walker.Face(entry.Facing);
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

        public bool CanReach(EntityId target)
        {
            var view = _representations.Find(target);
            return view != null && view.IsShown && view.HorizontalDistanceTo(X) <= PixelGrid.Units(InteractionRadius);
        }

        public void FaceForInteraction(EntityId target)
        {
            var view = _representations.Find(target);
            if (view == null || !view.IsShown)
            {
                throw new InvalidOperationException($"{target} is not shown in the current scene: nothing to face.");
            }

            Walker.Halt();
            FaceEachOther(view);
        }

        /// <summary>
        /// The hero looks at what he interacts with; a character looks back at him, unless it is walking somewhere itself.
        /// </summary>
        private void FaceEachOther(EntityView target)
        {
            Walker.FaceTowards(target.CenterX);
            if (target is CharacterView character && !character.Walker.IsMoving)
            {
                character.Walker.FaceTowards(X);
            }
        }
    }
}
