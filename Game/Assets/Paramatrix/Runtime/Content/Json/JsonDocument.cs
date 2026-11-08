using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Paramatrix.Content
{
    /// <summary>
    /// Parses content JSON into a token tree with line information.
    /// Duplicate property names are an error: silently keeping one of them would hide an authoring mistake.
    /// </summary>
    internal static class JsonDocument
    {
        private static readonly JsonLoadSettings LoadSettings = new JsonLoadSettings
        {
            LineInfoHandling = LineInfoHandling.Load,
            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
            CommentHandling = CommentHandling.Ignore,
        };

        public static JToken Parse(ContentSource source, DiagnosticBag diagnostics)
        {
            try
            {
                using (var reader = new JsonTextReader(new StringReader(source.Text)))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    reader.FloatParseHandling = FloatParseHandling.Decimal;
                    var token = JToken.ReadFrom(reader, LoadSettings);

                    // Reject trailing content after the root value.
                    while (reader.Read())
                    {
                        if (reader.TokenType != JsonToken.Comment)
                        {
                            diagnostics.Error(
                                DiagnosticCode.InvalidJson,
                                new SourceLocation(source.Name, null, reader.LineNumber, reader.LinePosition),
                                "Unexpected content after the end of the JSON document.");
                            return null;
                        }
                    }

                    return token;
                }
            }
            catch (JsonReaderException exception)
            {
                diagnostics.Error(
                    DiagnosticCode.InvalidJson,
                    new SourceLocation(source.Name, exception.Path, exception.LineNumber, exception.LinePosition),
                    exception.Message);
                return null;
            }
        }

        public static SourceLocation Locate(string source, JToken token)
        {
            var lineInfo = (IJsonLineInfo)token;
            return lineInfo.HasLineInfo()
                ? new SourceLocation(source, token.Path, lineInfo.LineNumber, lineInfo.LinePosition)
                : new SourceLocation(source, token.Path, 0, 0);
        }

        public static string KindName(JTokenType type)
        {
            switch (type)
            {
                case JTokenType.Object: return "object";
                case JTokenType.Array: return "array";
                case JTokenType.String: return "string";
                case JTokenType.Integer: return "integer";
                case JTokenType.Float: return "number";
                case JTokenType.Boolean: return "boolean";
                case JTokenType.Null: return "null";
                default: return type.ToString().ToLowerInvariant();
            }
        }
    }
}
