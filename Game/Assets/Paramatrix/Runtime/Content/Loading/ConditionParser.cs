using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Paramatrix.Content
{
    internal abstract class ConditionDraftNode
    {
        protected ConditionDraftNode(SourceLocation source)
        {
            Source = source;
        }

        public SourceLocation Source { get; }
    }

    internal sealed class AtomicConditionDraft : ConditionDraftNode
    {
        public AtomicConditionDraft(SourceLocation source)
            : base(source)
        {
        }

        public EntityId Target { get; set; }

        public SourceLocation TargetSource { get; set; }

        public string Property { get; set; }

        public SourceLocation PropertySource { get; set; }

        public ComparisonOperator Operator { get; set; }

        public SourceLocation OperatorSource { get; set; }

        public JToken Value { get; set; }

        public SourceLocation ValueSource { get; set; }
    }

    internal sealed class CompoundConditionDraft : ConditionDraftNode
    {
        public CompoundConditionDraft(bool isAll, SourceLocation source)
            : base(source)
        {
            IsAll = isAll;
        }

        public bool IsAll { get; }

        public List<ConditionDraftNode> Children { get; } = new List<ConditionDraftNode>();
    }

    internal sealed class NotConditionDraft : ConditionDraftNode
    {
        public NotConditionDraft(ConditionDraftNode child, SourceLocation source)
            : base(source)
        {
            Child = child;
        }

        public ConditionDraftNode Child { get; }
    }

    internal sealed class RefConditionDraft : ConditionDraftNode
    {
        public RefConditionDraft(EntityId id, SourceLocation source)
            : base(source)
        {
            Id = id;
        }

        public EntityId Id { get; }
    }

    internal sealed class ConditionDefinitionDraft : Draft
    {
        public ConditionDefinitionDraft(EntityId id, SourceLocation source, ConditionDraftNode root)
            : base(id, source)
        {
            Root = root;
        }

        public ConditionDraftNode Root { get; }
    }

    /// <summary>
    /// Structural parsing of a condition node. A node is exactly one of:
    /// atomic {target, property, operator, value}, {all: [...]}, {any: [...]}, {not: node}, {ref: id}.
    /// The comparison value stays a JSON token until the target property's type is known (cross-file).
    /// </summary>
    internal static class ConditionParser
    {
        private static readonly string[] AtomicFields = { "target", "property", "operator", "value" };
        private static readonly string[] OtherForms = { "all", "any", "not", "ref" };

        /// <summary>Returns null when the node is invalid (already reported).</summary>
        public static ConditionDraftNode Parse(JToken token, string source, DiagnosticBag diagnostics)
        {
            var reader = JsonObjectReader.ForObject(token, source, diagnostics, "Condition");
            if (reader == null)
            {
                return null;
            }

            var forms = new List<string>();
            foreach (var field in AtomicFields)
            {
                if (reader.Has(field))
                {
                    forms.Add("atomic");
                    break;
                }
            }

            foreach (var form in OtherForms)
            {
                if (reader.Has(form))
                {
                    forms.Add(form);
                }
            }

            if (forms.Count != 1)
            {
                diagnostics.Error(
                    DiagnosticCode.InvalidConditionForm,
                    reader.Location,
                    forms.Count == 0
                        ? "Condition must be one of: {target, property, operator, value}, {all}, {any}, {not}, {ref}."
                        : $"Condition mixes several forms ({string.Join(", ", forms)}); use exactly one.");
                return null;
            }

            ConditionDraftNode node;
            switch (forms[0])
            {
                case "atomic": node = ParseAtomic(reader); break;
                case "all": node = ParseCompound(reader, "all", true); break;
                case "any": node = ParseCompound(reader, "any", false); break;
                case "not": node = ParseNot(reader); break;
                default: node = ParseRef(reader); break;
            }

            reader.ReportUnknownFields();
            return node;
        }

        private static ConditionDraftNode ParseAtomic(JsonObjectReader reader)
        {
            var draft = new AtomicConditionDraft(reader.Location);
            var valid = true;

            var target = reader.Required("target");
            if (target != null && reader.ReadEntityId(target, "target", null, out var targetId))
            {
                draft.Target = targetId;
                draft.TargetSource = reader.LocationOf(target);
            }
            else
            {
                valid = false;
            }

            var property = reader.Required("property");
            if (property != null && reader.ReadString(property, "property", out var name))
            {
                if (StateAddress.IsValidPropertyName(name))
                {
                    draft.Property = name;
                    draft.PropertySource = reader.LocationOf(property);
                }
                else
                {
                    reader.Diagnostics.Error(DiagnosticCode.InvalidPropertyName, reader.LocationOf(property), $"Property name '{name}' is invalid.");
                    valid = false;
                }
            }
            else
            {
                valid = false;
            }

            var op = reader.Required("operator");
            if (op != null && reader.ReadString(op, "operator", out var opName))
            {
                if (ComparisonOperators.TryParse(opName, out var parsed))
                {
                    draft.Operator = parsed;
                    draft.OperatorSource = reader.LocationOf(op);
                }
                else
                {
                    reader.Diagnostics.Error(
                        DiagnosticCode.InvalidOperator,
                        reader.LocationOf(op),
                        $"Operator '{opName}' is not supported; use equals, not_equals, greater_than, greater_or_equal, less_than or less_or_equal.");
                    valid = false;
                }
            }
            else
            {
                valid = false;
            }

            var value = reader.Required("value");
            if (value != null)
            {
                draft.Value = value;
                draft.ValueSource = reader.LocationOf(value);
            }
            else
            {
                valid = false;
            }

            return valid ? draft : null;
        }

        private static ConditionDraftNode ParseCompound(JsonObjectReader reader, string field, bool isAll)
        {
            var array = reader.RequiredArray(field);
            if (array == null)
            {
                return null;
            }

            if (array.Count == 0)
            {
                reader.Diagnostics.Error(DiagnosticCode.EmptyCompoundCondition, reader.LocationOf(array), $"'{field}' must contain at least one condition.");
                return null;
            }

            var draft = new CompoundConditionDraft(isAll, reader.Location);
            var valid = true;
            foreach (var child in array)
            {
                var node = Parse(child, reader.Source, reader.Diagnostics);
                if (node == null)
                {
                    valid = false;
                }
                else
                {
                    draft.Children.Add(node);
                }
            }

            return valid ? draft : null;
        }

        private static ConditionDraftNode ParseNot(JsonObjectReader reader)
        {
            var token = reader.Required("not");
            var child = token == null ? null : Parse(token, reader.Source, reader.Diagnostics);
            return child == null ? null : new NotConditionDraft(child, reader.Location);
        }

        private static ConditionDraftNode ParseRef(JsonObjectReader reader)
        {
            return reader.RequiredEntityId("ref", EntityKind.Condition, out var id)
                ? new RefConditionDraft(id, reader.LocationOf(reader.Object["ref"]))
                : null;
        }
    }
}
