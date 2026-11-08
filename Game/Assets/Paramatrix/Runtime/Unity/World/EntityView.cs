using Paramatrix.Content;
using Paramatrix.State;
using UnityEngine;
using EntityId = Paramatrix.Content.EntityId;

namespace Paramatrix.Unity
{
    /// <summary>
    /// Unity representation of one entity (Architecture 0.3 §3.3, §5.7). Identified only by its stable Entity ID,
    /// never by GameObject name. Shows Logical State; owns none of it. Provides the hitbox and the approach point.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public abstract class EntityView : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Stable Entity ID, e.g. object.office_door.")]
        private string entityId;

        [SerializeField]
        [Tooltip("Where a character stands to interact. Empty: the view's own position.")]
        private Transform approachPoint;

        private Renderer[] _renderers;
        private Collider2D[] _colliders;

        public string RawEntityId => entityId;

        public EntityId Id { get; private set; }

        public bool IsShown { get; private set; } = true;

        /// <summary>X of the approach point on the floor line.</summary>
        public float ApproachX => approachPoint != null ? approachPoint.position.x : transform.position.x;

        /// <summary>The kind of entity this view type represents.</summary>
        public abstract EntityKind Kind { get; }

        internal void Bind(EntityId id)
        {
            Id = id;
            _renderers = GetComponentsInChildren<Renderer>(true);
            _colliders = GetComponentsInChildren<Collider2D>(true);
        }

        /// <summary>Re-reads the entity's State. <paramref name="sceneLocation"/> is the location of the view's scene.</summary>
        internal abstract void Apply(IStateReader state, EntityId sceneLocation);

        protected void SetShown(bool shown)
        {
            IsShown = shown;
            foreach (var item in _renderers)
            {
                item.enabled = shown;
            }

            foreach (var item in _colliders)
            {
                item.enabled = shown;
            }
        }

        protected static string ReadEnum(IStateReader state, EntityId entity, string property) =>
            state.TryGet(new StateAddress(entity, property), out var value) == StateReadStatus.Ok ? value.AsEnum : null;
    }
}
