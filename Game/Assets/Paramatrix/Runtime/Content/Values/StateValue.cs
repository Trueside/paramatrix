using System;
using System.Globalization;

namespace Paramatrix.Content
{
    /// <summary>State property types (Architecture 0.3.1 §4.2). Zero is deliberately not a valid type.</summary>
    public enum StateType
    {
        Boolean = 1,
        Integer,
        String,
        Enum,
    }

    /// <summary>
    /// Typed State value. No implicit conversions: reading a value as a different type throws (Architecture 0.3.2 §3).
    /// default(StateValue) has no type and is invalid.
    /// </summary>
    public readonly struct StateValue : IEquatable<StateValue>
    {
        private readonly bool _boolean;
        private readonly int _integer;
        private readonly string _text;

        private StateValue(StateType type, bool boolean, int integer, string text)
        {
            Type = type;
            _boolean = boolean;
            _integer = integer;
            _text = text;
        }

        public StateType Type { get; }

        public bool IsValid => Type != 0;

        public static StateValue Boolean(bool value) => new StateValue(StateType.Boolean, value, 0, null);

        public static StateValue Integer(int value) => new StateValue(StateType.Integer, false, value, null);

        public static StateValue String(string value) =>
            new StateValue(StateType.String, false, 0, value ?? throw new ArgumentNullException(nameof(value)));

        public static StateValue Enum(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException("Enum value must not be empty.", nameof(value));
            }

            return new StateValue(StateType.Enum, false, 0, value);
        }

        public bool AsBoolean => Type == StateType.Boolean ? _boolean : throw WrongType(StateType.Boolean);

        public int AsInteger => Type == StateType.Integer ? _integer : throw WrongType(StateType.Integer);

        public string AsString => Type == StateType.String ? _text : throw WrongType(StateType.String);

        public string AsEnum => Type == StateType.Enum ? _text : throw WrongType(StateType.Enum);

        private InvalidOperationException WrongType(StateType requested) =>
            new InvalidOperationException($"State value of type {TypeName(Type)} read as {TypeName(requested)}.");

        public static string TypeName(StateType type)
        {
            switch (type)
            {
                case StateType.Boolean: return "boolean";
                case StateType.Integer: return "integer";
                case StateType.String: return "string";
                case StateType.Enum: return "enum";
                default: return "<no type>";
            }
        }

        public bool Equals(StateValue other)
        {
            if (Type != other.Type)
            {
                return false;
            }

            switch (Type)
            {
                case StateType.Boolean: return _boolean == other._boolean;
                case StateType.Integer: return _integer == other._integer;
                case StateType.String:
                case StateType.Enum: return string.Equals(_text, other._text, StringComparison.Ordinal);
                default: return true;
            }
        }

        public override bool Equals(object obj) => obj is StateValue other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Type * 397;
                switch (Type)
                {
                    case StateType.Boolean: return hash ^ (_boolean ? 1 : 0);
                    case StateType.Integer: return hash ^ _integer;
                    case StateType.String:
                    case StateType.Enum: return hash ^ StringComparer.Ordinal.GetHashCode(_text);
                    default: return hash;
                }
            }
        }

        public static bool operator ==(StateValue left, StateValue right) => left.Equals(right);

        public static bool operator !=(StateValue left, StateValue right) => !left.Equals(right);

        /// <summary>JSON-like rendering for diagnostics: true, 42, "text", enum values bare.</summary>
        public override string ToString()
        {
            switch (Type)
            {
                case StateType.Boolean: return _boolean ? "true" : "false";
                case StateType.Integer: return _integer.ToString(CultureInfo.InvariantCulture);
                case StateType.String: return "\"" + _text + "\"";
                case StateType.Enum: return _text;
                default: return "<no value>";
            }
        }
    }
}
