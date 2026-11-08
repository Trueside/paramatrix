using System;

namespace Paramatrix.Content
{
    /// <summary>
    /// Address of one State property: Entity ID + property name (Architecture 0.3.1 §4.1).
    /// The dotted form "object.office_door.open" is for diagnostics only; content always stores entity and property separately.
    /// </summary>
    public readonly struct StateAddress : IEquatable<StateAddress>, IComparable<StateAddress>
    {
        private readonly string _property;

        public StateAddress(EntityId entity, string property)
        {
            if (!entity.IsValid)
            {
                throw new ArgumentException("Entity ID is not initialized.", nameof(entity));
            }

            if (!IsValidPropertyName(property))
            {
                throw new ArgumentException($"Invalid property name '{property}'.", nameof(property));
            }

            Entity = entity;
            _property = property;
        }

        public EntityId Entity { get; }

        public string Property => _property ?? throw new InvalidOperationException("StateAddress is not initialized.");

        public bool IsValid => _property != null;

        /// <summary>Property name: starts with a lowercase Latin letter, then lowercase letters, digits, '_'.</summary>
        public static bool IsValidPropertyName(string name)
        {
            if (string.IsNullOrEmpty(name) || name[0] < 'a' || name[0] > 'z')
            {
                return false;
            }

            foreach (var c in name)
            {
                if (!EntityId.IsNameChar(c))
                {
                    return false;
                }
            }

            return true;
        }

        public static StateAddress Parse(string entity, string property) => new StateAddress(EntityId.Parse(entity), property);

        public bool Equals(StateAddress other) =>
            Entity.Equals(other.Entity) && string.Equals(_property, other._property, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is StateAddress other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (Entity.GetHashCode() * 397) ^ (_property == null ? 0 : StringComparer.Ordinal.GetHashCode(_property));
            }
        }

        public int CompareTo(StateAddress other)
        {
            var byEntity = Entity.CompareTo(other.Entity);
            return byEntity != 0 ? byEntity : string.CompareOrdinal(_property, other._property);
        }

        public static bool operator ==(StateAddress left, StateAddress right) => left.Equals(right);

        public static bool operator !=(StateAddress left, StateAddress right) => !left.Equals(right);

        public override string ToString() => _property == null ? "<invalid address>" : Entity + "." + _property;
    }
}
