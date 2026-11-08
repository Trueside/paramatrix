using Newtonsoft.Json.Linq;

namespace Paramatrix.Content
{
    /// <summary>
    /// Converts a JSON token into a <see cref="StateValue"/> of an expected type. No conversions:
    /// "1" is not an integer, 1 is not a boolean (Architecture 0.3.2 §3).
    /// </summary>
    internal static class StateValueReader
    {
        public static bool TryRead(JToken token, StateType type, out StateValue value, out string error)
        {
            value = default;
            error = null;

            switch (type)
            {
                case StateType.Boolean:
                    if (token.Type == JTokenType.Boolean)
                    {
                        value = StateValue.Boolean(token.Value<bool>());
                        return true;
                    }

                    break;

                case StateType.Integer:
                    if (token.Type == JTokenType.Integer)
                    {
                        if (((JValue)token).Value is long wide && wide >= int.MinValue && wide <= int.MaxValue)
                        {
                            value = StateValue.Integer((int)wide);
                            return true;
                        }

                        error = "Value does not fit a 32-bit integer.";
                        return false;
                    }

                    break;

                case StateType.String:
                    if (token.Type == JTokenType.String)
                    {
                        value = StateValue.String(token.Value<string>());
                        return true;
                    }

                    break;

                case StateType.Enum:
                    if (token.Type == JTokenType.String)
                    {
                        var text = token.Value<string>();
                        if (text.Length == 0)
                        {
                            error = "Enum value must not be empty.";
                            return false;
                        }

                        value = StateValue.Enum(text);
                        return true;
                    }

                    break;
            }

            error = $"Expected a {StateValue.TypeName(type)} value, found JSON {JsonDocument.KindName(token.Type)}.";
            return false;
        }

        /// <summary>
        /// Enum value format: a plain name (lowercase Latin, digits, '_', starting with a letter)
        /// or an Entity ID (decision A16 — such values must reference existing entities).
        /// </summary>
        public static bool IsValidEnumValueFormat(string text) =>
            StateAddress.IsValidPropertyName(text) || EntityId.TryParse(text, out _, out _);

        public static bool IsEntityReference(string text) => text.IndexOf('.') >= 0;
    }
}
