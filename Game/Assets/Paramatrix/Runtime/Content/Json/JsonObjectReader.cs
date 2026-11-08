using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Paramatrix.Content
{
    /// <summary>
    /// Reads fields of one JSON object and reports problems as diagnostics.
    /// Every field read is remembered; <see cref="ReportUnknownFields"/> then reports all fields nobody read,
    /// so a misspelled field name is an error instead of being silently ignored.
    /// All getters return false/null on failure after reporting; callers just stop building that definition.
    /// </summary>
    internal sealed class JsonObjectReader
    {
        private readonly HashSet<string> _consumed = new HashSet<string>(StringComparer.Ordinal);

        public JsonObjectReader(JObject obj, string source, DiagnosticBag diagnostics)
        {
            Object = obj ?? throw new ArgumentNullException(nameof(obj));
            Source = source;
            Diagnostics = diagnostics;
        }

        public JObject Object { get; }

        public string Source { get; }

        public DiagnosticBag Diagnostics { get; }

        public SourceLocation Location => JsonDocument.Locate(Source, Object);

        /// <summary>Creates a reader for a token that must be an object; reports and returns null otherwise.</summary>
        public static JsonObjectReader ForObject(JToken token, string source, DiagnosticBag diagnostics, string what)
        {
            if (token is JObject obj)
            {
                return new JsonObjectReader(obj, source, diagnostics);
            }

            diagnostics.Error(
                DiagnosticCode.UnexpectedJsonType,
                JsonDocument.Locate(source, token),
                $"{what} must be an object, found {JsonDocument.KindName(token.Type)}.");
            return null;
        }

        public bool Has(string name) => Object.ContainsKey(name);

        public SourceLocation LocationOf(JToken token) => JsonDocument.Locate(Source, token);

        /// <summary>Marks the field as known. Returns the token or null when absent (no diagnostic).</summary>
        public JToken Optional(string name)
        {
            _consumed.Add(name);
            return Object.TryGetValue(name, StringComparison.Ordinal, out var token) ? token : null;
        }

        /// <summary>Marks the field as known. Reports MissingRequiredField and returns null when absent.</summary>
        public JToken Required(string name)
        {
            var token = Optional(name);
            if (token == null)
            {
                Diagnostics.Error(DiagnosticCode.MissingRequiredField, Location, $"Required field '{name}' is missing.");
            }

            return token;
        }

        public bool RequiredString(string name, out string value) => ReadString(Required(name), name, out value);

        public bool OptionalString(string name, out string value, out bool present)
        {
            var token = Optional(name);
            present = token != null;
            if (!present)
            {
                value = null;
                return true;
            }

            return ReadString(token, name, out value);
        }

        public bool RequiredBoolean(string name, out bool value)
        {
            value = false;
            var token = Required(name);
            if (token == null)
            {
                return false;
            }

            if (token.Type != JTokenType.Boolean)
            {
                ReportType(token, name, "boolean");
                return false;
            }

            value = token.Value<bool>();
            return true;
        }

        public bool RequiredInteger(string name, out int value) => ReadInteger(Required(name), name, out value);

        public bool OptionalInteger(string name, out int value, out bool present)
        {
            var token = Optional(name);
            present = token != null;
            value = 0;
            return !present || ReadInteger(token, name, out value);
        }

        public JObject RequiredObject(string name)
        {
            var token = Required(name);
            if (token == null)
            {
                return null;
            }

            if (token is JObject obj)
            {
                return obj;
            }

            ReportType(token, name, "object");
            return null;
        }

        public JArray RequiredArray(string name)
        {
            var token = Required(name);
            return token == null ? null : AsArray(token, name);
        }

        public JArray OptionalArray(string name, out bool present)
        {
            var token = Optional(name);
            present = token != null;
            return token == null ? null : AsArray(token, name);
        }

        /// <summary>Reads a string field holding an Entity ID; optionally requires a specific kind.</summary>
        public bool RequiredEntityId(string name, EntityKind? expectedKind, out EntityId id)
        {
            id = default;
            var token = Required(name);
            return token != null && ReadEntityId(token, name, expectedKind, out id);
        }

        public bool ReadEntityId(JToken token, string name, EntityKind? expectedKind, out EntityId id)
        {
            id = default;
            if (!ReadString(token, name, out var text))
            {
                return false;
            }

            if (!EntityId.TryParse(text, out id, out var error))
            {
                Diagnostics.Error(DiagnosticCode.InvalidEntityId, LocationOf(token), error);
                return false;
            }

            if (expectedKind.HasValue && id.Kind != expectedKind.Value)
            {
                Diagnostics.Error(
                    DiagnosticCode.InvalidReferenceKind,
                    LocationOf(token),
                    $"Field '{name}' must reference a '{EntityKinds.Prefix(expectedKind.Value)}' entity, found '{text}'.");
                id = default;
                return false;
            }

            return true;
        }

        public bool ReadString(JToken token, string name, out string value)
        {
            value = null;
            if (token == null)
            {
                return false;
            }

            if (token.Type != JTokenType.String)
            {
                ReportType(token, name, "string");
                return false;
            }

            value = token.Value<string>();
            return true;
        }

        public bool ReadInteger(JToken token, string name, out int value)
        {
            value = 0;
            if (token == null)
            {
                return false;
            }

            if (token.Type != JTokenType.Integer)
            {
                ReportType(token, name, "integer");
                return false;
            }

            // Newtonsoft stores integers as long, or as BigInteger when they do not fit long.
            if (!(((JValue)token).Value is long wide) || wide < int.MinValue || wide > int.MaxValue)
            {
                ReportIntegerRange(token, name);
                return false;
            }

            value = (int)wide;
            return true;
        }

        /// <summary>Reports every field that was never read through this reader.</summary>
        public void ReportUnknownFields()
        {
            foreach (var property in Object.Properties())
            {
                if (!_consumed.Contains(property.Name))
                {
                    Diagnostics.Error(DiagnosticCode.UnknownField, LocationOf(property), $"Unknown field '{property.Name}'.");
                }
            }
        }

        private JArray AsArray(JToken token, string name)
        {
            if (token is JArray array)
            {
                return array;
            }

            ReportType(token, name, "array");
            return null;
        }

        private void ReportType(JToken token, string name, string expected) =>
            Diagnostics.Error(
                DiagnosticCode.UnexpectedJsonType,
                LocationOf(token),
                $"Field '{name}' must be {expected}, found {JsonDocument.KindName(token.Type)}.");

        private void ReportIntegerRange(JToken token, string name) =>
            Diagnostics.Error(
                DiagnosticCode.UnexpectedJsonType,
                LocationOf(token),
                $"Field '{name}' does not fit a 32-bit integer.");
    }
}
