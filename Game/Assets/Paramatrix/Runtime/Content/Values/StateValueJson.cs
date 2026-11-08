using Newtonsoft.Json.Linq;

namespace Paramatrix.Content
{
    /// <summary>
    /// JSON form of State values for files other than content (saves). Same strict rules as content:
    /// no conversions between types.
    /// </summary>
    public static class StateValueJson
    {
        public static bool TryRead(JToken token, StateType type, out StateValue value, out string error) =>
            StateValueReader.TryRead(token, type, out value, out error);

        public static JToken ToToken(StateValue value)
        {
            switch (value.Type)
            {
                case StateType.Boolean: return new JValue(value.AsBoolean);
                case StateType.Integer: return new JValue(value.AsInteger);
                case StateType.String: return new JValue(value.AsString);
                case StateType.Enum: return new JValue(value.AsEnum);
                default: throw new System.ArgumentException("State value is not initialized.", nameof(value));
            }
        }
    }
}
