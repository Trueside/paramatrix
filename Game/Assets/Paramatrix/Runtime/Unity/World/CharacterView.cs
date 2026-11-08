using Paramatrix.Content;
using Paramatrix.State;
using UnityEngine;
using EntityId = Paramatrix.Content.EntityId;

namespace Paramatrix.Unity
{
    /// <summary>A character: shown only while its <c>location</c> State is this scene's location (T14).</summary>
    [RequireComponent(typeof(Walker))]
    public sealed class CharacterView : EntityView
    {
        [SerializeField]
        [Tooltip("The player character lives in the Bootstrap scene and is always shown.")]
        private bool isPlayer;

        public bool IsPlayer => isPlayer;

        public Walker Walker => GetComponent<Walker>();

        public override EntityKind Kind => EntityKind.Character;

        internal override void Apply(IStateReader state, EntityId sceneLocation)
        {
            SetShown(isPlayer || (sceneLocation.IsValid && ReadEnum(state, Id, BuiltInStates.Location) == sceneLocation.Value));
        }
    }
}
