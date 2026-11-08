using System;
using System.Collections.Generic;
using Paramatrix.Content;
using Paramatrix.State;

namespace Paramatrix.Conditions
{
    public enum ConditionOutcome
    {
        True = 1,
        False,
        Error,
    }

    /// <summary>Architecture 0.3.2 §4.3.</summary>
    public enum ConditionError
    {
        None = 0,
        UnknownCondition,
        UnknownEntity,
        UnknownProperty,
        TypeMismatch,
        InvalidOperator,
        InvalidDefinition,
        ConditionCycle,
    }

    /// <summary>True, False or Error. Error is never False (0.3.2 §4.2).</summary>
    public readonly struct ConditionResult
    {
        private ConditionResult(ConditionOutcome outcome, ConditionError error, string detail)
        {
            Outcome = outcome;
            Error = error;
            Detail = detail;
        }

        public static ConditionResult True => new ConditionResult(ConditionOutcome.True, ConditionError.None, null);

        public static ConditionResult False => new ConditionResult(ConditionOutcome.False, ConditionError.None, null);

        public static ConditionResult Fail(ConditionError error, string detail) => new ConditionResult(ConditionOutcome.Error, error, detail);

        public static ConditionResult FromBool(bool value) => value ? True : False;

        public ConditionOutcome Outcome { get; }

        public ConditionError Error { get; }

        /// <summary>Human-readable cause of an Error; null otherwise.</summary>
        public string Detail { get; }

        public bool IsTrue => Outcome == ConditionOutcome.True;

        public bool IsError => Outcome == ConditionOutcome.Error;

        public override string ToString() => IsError ? $"Error ({Error}: {Detail})" : Outcome.ToString();
    }

    /// <summary>
    /// The only evaluator of game conditions (Architecture 0.3.2). Reads State through <see cref="IStateReader"/> only,
    /// keeps no copies, results or subscriptions, and decides nothing about when it is called.
    /// Compound conditions evaluate every child (no short-circuit) so that an Error anywhere is never hidden.
    /// </summary>
    public sealed class ConditionEvaluator
    {
        private readonly ContentRegistry _registry;
        private readonly IStateReader _state;

        public ConditionEvaluator(ContentRegistry registry, IStateReader state)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public ConditionResult Evaluate(EntityId conditionId) =>
            EvaluateReference(conditionId, new List<EntityId>());

        public ConditionResult Evaluate(ConditionNode node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            return Evaluate(node, new List<EntityId>());
        }

        /// <summary>
        /// Every State address the condition reads, references expanded, sorted. Used by Scenario to decide
        /// which monitored conditions to re-evaluate after a State change (T2).
        /// </summary>
        public IReadOnlyList<StateAddress> GetDependencies(ConditionNode node)
        {
            var set = new SortedSet<StateAddress>();
            Collect(node, set, new HashSet<EntityId>());
            return new List<StateAddress>(set);
        }

        private ConditionResult Evaluate(ConditionNode node, List<EntityId> stack)
        {
            switch (node)
            {
                case AtomicCondition atomic:
                    return EvaluateAtomic(atomic);

                case AllCondition all:
                    return Combine(all.Children, stack, requireAll: true);

                case AnyCondition any:
                    return Combine(any.Children, stack, requireAll: false);

                case NotCondition not:
                    var inner = Evaluate(not.Child, stack);
                    return inner.IsError ? inner : ConditionResult.FromBool(!inner.IsTrue);

                case ConditionRef reference:
                    return EvaluateReference(reference.ConditionId, stack);

                default:
                    return ConditionResult.Fail(ConditionError.InvalidDefinition, $"Unsupported condition node {node.GetType().Name}.");
            }
        }

        private ConditionResult Combine(IReadOnlyList<ConditionNode> children, List<EntityId> stack, bool requireAll)
        {
            if (children.Count == 0)
            {
                return ConditionResult.Fail(ConditionError.InvalidDefinition, "Empty compound condition.");
            }

            ConditionResult? firstError = null;
            var trueCount = 0;
            foreach (var child in children)
            {
                var result = Evaluate(child, stack);
                if (result.IsError)
                {
                    firstError = firstError ?? result;
                }
                else if (result.IsTrue)
                {
                    trueCount++;
                }
            }

            if (firstError.HasValue)
            {
                return firstError.Value;
            }

            return ConditionResult.FromBool(requireAll ? trueCount == children.Count : trueCount > 0);
        }

        private ConditionResult EvaluateReference(EntityId id, List<EntityId> stack)
        {
            if (!_registry.TryGet<NamedConditionDefinition>(id, out var definition))
            {
                return ConditionResult.Fail(ConditionError.UnknownCondition, $"Condition '{id}' is not defined.");
            }

            if (stack.Contains(id))
            {
                return ConditionResult.Fail(ConditionError.ConditionCycle, "Condition reference cycle through " + id + ".");
            }

            stack.Add(id);
            var result = Evaluate(definition.Root, stack);
            stack.RemoveAt(stack.Count - 1);
            return result;
        }

        private ConditionResult EvaluateAtomic(AtomicCondition atomic)
        {
            switch (_state.TryGet(atomic.Target, out var current))
            {
                case StateReadStatus.UnknownEntity:
                    return ConditionResult.Fail(ConditionError.UnknownEntity, $"Entity '{atomic.Target.Entity}' has no State.");
                case StateReadStatus.UnknownProperty:
                    return ConditionResult.Fail(ConditionError.UnknownProperty, $"State property {atomic.Target} does not exist.");
            }

            if (current.Type != atomic.Value.Type)
            {
                return ConditionResult.Fail(
                    ConditionError.TypeMismatch,
                    $"{atomic.Target} is {StateValue.TypeName(current.Type)}, compared with {StateValue.TypeName(atomic.Value.Type)} {atomic.Value}.");
            }

            switch (atomic.Operator)
            {
                case ComparisonOperator.Equals:
                    return ConditionResult.FromBool(current == atomic.Value);
                case ComparisonOperator.NotEquals:
                    return ConditionResult.FromBool(current != atomic.Value);
            }

            if (current.Type != StateType.Integer)
            {
                return ConditionResult.Fail(
                    ConditionError.InvalidOperator,
                    $"Operator {ComparisonOperators.Name(atomic.Operator)} is not defined for {StateValue.TypeName(current.Type)} {atomic.Target}.");
            }

            var left = current.AsInteger;
            var right = atomic.Value.AsInteger;
            switch (atomic.Operator)
            {
                case ComparisonOperator.GreaterThan: return ConditionResult.FromBool(left > right);
                case ComparisonOperator.GreaterOrEqual: return ConditionResult.FromBool(left >= right);
                case ComparisonOperator.LessThan: return ConditionResult.FromBool(left < right);
                case ComparisonOperator.LessOrEqual: return ConditionResult.FromBool(left <= right);
                default:
                    return ConditionResult.Fail(ConditionError.InvalidOperator, $"Unsupported operator {atomic.Operator}.");
            }
        }

        private void Collect(ConditionNode node, SortedSet<StateAddress> into, HashSet<EntityId> visited)
        {
            switch (node)
            {
                case AtomicCondition atomic:
                    into.Add(atomic.Target);
                    break;
                case CompoundCondition compound:
                    foreach (var child in compound.Children)
                    {
                        Collect(child, into, visited);
                    }

                    break;
                case NotCondition not:
                    Collect(not.Child, into, visited);
                    break;
                case ConditionRef reference:
                    if (visited.Add(reference.ConditionId) && _registry.TryGet<NamedConditionDefinition>(reference.ConditionId, out var definition))
                    {
                        Collect(definition.Root, into, visited);
                    }

                    break;
            }
        }
    }
}
