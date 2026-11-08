using Paramatrix.Content;
using Paramatrix.State;
using UnityEngine;
using EntityId = Paramatrix.Content.EntityId;

namespace Paramatrix.Unity
{
    /// <summary>
    /// Unity representation of one entity (Architecture 0.3 §3.3, §5.7). Identified only by its stable Entity ID,
    /// never by GameObject name. Shows Logical State; owns none of it. Provides the click area and the approach point.
    /// The click area is the bounds of the view's sprites: no physics is involved, so it is always where the sprite is.
    /// </summary>
    public abstract class EntityView : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Stable Entity ID, e.g. object.office_door.")]
        private string entityId;

        [SerializeField]
        [Tooltip("Where a character stands to interact. Empty: the view's own position.")]
        private Transform approachPoint;

        private SpriteRenderer[] _sprites;

        public string RawEntityId => entityId;

        public EntityId Id { get; private set; }

        public bool IsShown { get; private set; } = true;

        /// <summary>X of the approach point on the floor line.</summary>
        public float ApproachX => approachPoint != null ? approachPoint.position.x : transform.position.x;

        /// <summary>The kind of entity this view type represents.</summary>
        public abstract EntityKind Kind { get; }

        /// <summary>Drawing order of the topmost sprite; a click goes to the view drawn on top.</summary>
        public int SortingOrder
        {
            get
            {
                var order = int.MinValue;
                foreach (var sprite in Sprites)
                {
                    order = Mathf.Max(order, sprite.sortingOrder);
                }

                return order;
            }
        }

        private SpriteRenderer[] Sprites => _sprites ?? (_sprites = GetComponentsInChildren<SpriteRenderer>(true));

        /// <summary>Whether a world point lies on one of the view's sprites (their 2D bounds).</summary>
        public bool Contains(Vector2 world)
        {
            foreach (var sprite in Sprites)
            {
                var bounds = sprite.bounds;
                if (world.x >= bounds.min.x && world.x <= bounds.max.x && world.y >= bounds.min.y && world.y <= bounds.max.y)
                {
                    return true;
                }
            }

            return false;
        }

        internal void Bind(EntityId id)
        {
            Id = id;
            _sprites = GetComponentsInChildren<SpriteRenderer>(true);
        }

        /// <summary>Re-reads the entity's State. <paramref name="sceneLocation"/> is the location of the view's scene.</summary>
        internal abstract void Apply(IStateReader state, EntityId sceneLocation);

        protected void SetShown(bool shown)
        {
            IsShown = shown;
            foreach (var sprite in Sprites)
            {
                sprite.enabled = shown;
            }
        }

        protected static string ReadEnum(IStateReader state, EntityId entity, string property) =>
            state.TryGet(new StateAddress(entity, property), out var value) == StateReadStatus.Ok ? value.AsEnum : null;
    }
}
