using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Paramatrix.Content
{
    /// <summary>Comparison operators (Architecture 0.3.2 §3).</summary>
    public enum ComparisonOperator
    {
        Equals = 1,
        NotEquals,
        GreaterThan,
        GreaterOrEqual,
        LessThan,
        LessOrEqual,
    }

    public static class ComparisonOperators
    {
        private static readonly (string Name, ComparisonOperator Operator)[] Table =
        {
            ("equals", ComparisonOperator.Equals),
            ("not_equals", ComparisonOperator.NotEquals),
            ("greater_than", ComparisonOperator.GreaterThan),
            ("greater_or_equal", ComparisonOperator.GreaterOrEqual),
            ("less_than", ComparisonOperator.LessThan),
            ("less_or_equal", ComparisonOperator.LessOrEqual),
        };

        public static bool TryParse(string name, out ComparisonOperator op)
        {
            foreach (var entry in Table)
            {
                if (string.Equals(entry.Name, name, StringComparison.Ordinal))
                {
                    op = entry.Operator;
                    return true;
                }
            }

            op = default;
            return false;
        }

        public static string Name(ComparisonOperator op)
        {
            foreach (var entry in Table)
            {
                if (entry.Operator == op)
                {
                    return entry.Name;
                }
            }

            return op.ToString();
        }

        public static bool IsNumeric(ComparisonOperator op) => op != ComparisonOperator.Equals && op != ComparisonOperator.NotEquals;

        /// <summary>Numeric operators only for Integer; equals/not_equals for every type (0.3.2 §3, T8).</summary>
        public static bool IsAllowedFor(ComparisonOperator op, StateType type) => !IsNumeric(op) || type == StateType.Integer;
    }

    /// <summary>Immutable condition tree node. No expressions, no code: only these five forms (0.3.2 §2).</summary>
    public abstract class ConditionNode
    {
        protected ConditionNode(SourceLocation source)
        {
            Source = source;
        }

        public SourceLocation Source { get; }
    }

    public sealed class AtomicCondition : ConditionNode
    {
        public AtomicCondition(StateAddress target, ComparisonOperator op, StateValue value, SourceLocation source)
            : base(source)
        {
            if (!target.IsValid || !value.IsValid)
            {
                throw new ArgumentException("Atomic condition needs a target and a value.");
            }

            Target = target;
            Operator = op;
            Value = value;
        }

        public StateAddress Target { get; }

        public ComparisonOperator Operator { get; }

        public StateValue Value { get; }

        public override string ToString() => $"{Target} {ComparisonOperators.Name(Operator)} {Value}";
    }

    public abstract class CompoundCondition : ConditionNode
    {
        protected CompoundCondition(IEnumerable<ConditionNode> children, SourceLocation source)
            : base(source)
        {
            var list = new List<ConditionNode>(children ?? throw new ArgumentNullException(nameof(children)));
            if (list.Count == 0)
            {
                throw new ArgumentException("Compound condition must not be empty.", nameof(children));
            }

            Children = new ReadOnlyCollection<ConditionNode>(list);
        }

        public IReadOnlyList<ConditionNode> Children { get; }
    }

    public sealed class AllCondition : CompoundCondition
    {
        public AllCondition(IEnumerable<ConditionNode> children, SourceLocation source)
            : base(children, source)
        {
        }
    }

    public sealed class AnyCondition : CompoundCondition
    {
        public AnyCondition(IEnumerable<ConditionNode> children, SourceLocation source)
            : base(children, source)
        {
        }
    }

    public sealed class NotCondition : ConditionNode
    {
        public NotCondition(ConditionNode child, SourceLocation source)
            : base(source)
        {
            Child = child ?? throw new ArgumentNullException(nameof(child));
        }

        public ConditionNode Child { get; }
    }

    public sealed class ConditionRef : ConditionNode
    {
        public ConditionRef(EntityId conditionId, SourceLocation source)
            : base(source)
        {
            if (!conditionId.IsValid)
            {
                throw new ArgumentException("Condition ID is not initialized.", nameof(conditionId));
            }

            ConditionId = conditionId;
        }

        public EntityId ConditionId { get; }

        public override string ToString() => "ref " + ConditionId;
    }

    /// <summary>Named, reusable condition <c>condition.*</c>. Stores no result and has no runtime State (0.3.2 §2.3).</summary>
    public sealed class NamedConditionDefinition : Definition
    {
        public NamedConditionDefinition(EntityId id, SourceLocation source, ConditionNode root)
            : base(id, source)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
        }

        public ConditionNode Root { get; }
    }
}
