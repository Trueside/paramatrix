using System;

namespace Paramatrix.Content
{
    /// <summary>
    /// Stable logical identifier "&lt;kind&gt;.&lt;name&gt;" (Architecture 0.3.1 §2.3).
    /// Kind prefix comes from the closed <see cref="EntityKind"/> list; name is lowercase Latin, digits, underscore.
    /// default(EntityId) is invalid: every accessor except <see cref="IsValid"/> throws on it.
    /// </summary>
    public readonly struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
    {
        private readonly string _value;
        private readonly EntityKind _kind;

        private EntityId(string value, EntityKind kind)
        {
            _value = value;
            _kind = kind;
        }

        public bool IsValid => _value != null;

        public string Value => _value ?? throw new InvalidOperationException("EntityId is not initialized.");

        public EntityKind Kind => _value != null ? _kind : throw new InvalidOperationException("EntityId is not initialized.");

        public string Name => Value.Substring(Value.IndexOf('.') + 1);

        public static bool TryParse(string text, out EntityId id, out string error)
        {
            id = default;

            if (string.IsNullOrEmpty(text))
            {
                error = "ID is empty.";
                return false;
            }

            var dot = text.IndexOf('.');
            if (dot <= 0 || dot == text.Length - 1)
            {
                error = $"ID '{text}' must have the form '<kind>.<name>'.";
                return false;
            }

            if (text.IndexOf('.', dot + 1) >= 0)
            {
                error = $"ID '{text}' must contain exactly one dot.";
                return false;
            }

            var prefix = text.Substring(0, dot);
            if (!EntityKinds.TryFromPrefix(prefix, out var kind))
            {
                error = $"ID '{text}' has unknown kind prefix '{prefix}'.";
                return false;
            }

            for (var i = dot + 1; i < text.Length; i++)
            {
                if (!IsNameChar(text[i]))
                {
                    error = $"ID '{text}' contains invalid character '{text[i]}': only lowercase Latin letters, digits and '_' are allowed.";
                    return false;
                }
            }

            id = new EntityId(text, kind);
            error = null;
            return true;
        }

        /// <summary>Parses an ID that is known to be valid (code, tests). Throws on invalid input.</summary>
        public static EntityId Parse(string text)
        {
            if (!TryParse(text, out var id, out var error))
            {
                throw new ArgumentException(error, nameof(text));
            }

            return id;
        }

        internal static bool IsNameChar(char c) => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';

        public bool Equals(EntityId other) => string.Equals(_value, other._value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is EntityId other && Equals(other);

        public override int GetHashCode() => _value == null ? 0 : StringComparer.Ordinal.GetHashCode(_value);

        public int CompareTo(EntityId other) => string.CompareOrdinal(_value, other._value);

        public static bool operator ==(EntityId left, EntityId right) => left.Equals(right);

        public static bool operator !=(EntityId left, EntityId right) => !left.Equals(right);

        public override string ToString() => _value ?? "<invalid id>";
    }
}
