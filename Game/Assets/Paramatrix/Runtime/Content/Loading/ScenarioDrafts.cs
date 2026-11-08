using Newtonsoft.Json.Linq;

namespace Paramatrix.Content
{
    /// <summary>
    /// Event header parsed up front (execution type and repeat policy are needed to check CALL/START between events);
    /// triggers, launch condition and actions stay JSON until the linker resolves them against the whole content.
    /// </summary>
    internal sealed class EventDraft : Draft
    {
        public EventDraft(EntityId id, SourceLocation source, string sourceName)
            : base(id, source)
        {
            SourceName = sourceName;
        }

        public string SourceName { get; }

        /// <summary>Zero when invalid (already reported).</summary>
        public ExecutionType ExecutionType { get; set; }

        public RepeatPolicy RepeatPolicy { get; set; }

        public JArray Triggers { get; set; }

        public JToken LaunchCondition { get; set; }

        public JArray Actions { get; set; }
    }

    /// <summary>Dialogues and responses are small; they are parsed from JSON during linking.</summary>
    internal sealed class RawDraft : Draft
    {
        public RawDraft(EntityId id, SourceLocation source, string sourceName, JObject json)
            : base(id, source)
        {
            SourceName = sourceName;
            Json = json;
        }

        public string SourceName { get; }

        public JObject Json { get; }
    }
}
