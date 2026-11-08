using System.Collections.Generic;

namespace Paramatrix.Content
{
    /// <summary>
    /// Turns condition drafts into immutable nodes, checking everything that needs the whole content:
    /// target and property exist, operator fits the type, comparison value has the property's type and is admissible.
    /// </summary>
    internal sealed class ConditionResolver
    {
        private readonly IReadOnlyDictionary<EntityId, Draft> _drafts;
        private readonly IReadOnlyDictionary<StateAddress, StateDefinition> _states;
        private readonly DiagnosticBag _diagnostics;

        public ConditionResolver(
            IReadOnlyDictionary<EntityId, Draft> drafts,
            IReadOnlyDictionary<StateAddress, StateDefinition> states,
            DiagnosticBag diagnostics)
        {
            _drafts = drafts;
            _states = states;
            _diagnostics = diagnostics;
        }

        /// <summary>Returns null when anything in the subtree is invalid; every problem is reported.</summary>
        public ConditionNode Resolve(ConditionDraftNode draft)
        {
            switch (draft)
            {
                case AtomicConditionDraft atomic:
                    return ResolveAtomic(atomic);

                case CompoundConditionDraft compound:
                    var children = new List<ConditionNode>();
                    var valid = true;
                    foreach (var child in compound.Children)
                    {
                        var node = Resolve(child);
                        valid &= node != null;
                        children.Add(node);
                    }

                    if (!valid)
                    {
                        return null;
                    }

                    return compound.IsAll
                        ? (ConditionNode)new AllCondition(children, compound.Source)
                        : new AnyCondition(children, compound.Source);

                case NotConditionDraft not:
                    var inner = Resolve(not.Child);
                    return inner == null ? null : new NotCondition(inner, not.Source);

                case RefConditionDraft reference:
                    if (!_drafts.TryGetValue(reference.Id, out var target) || !(target is ConditionDefinitionDraft))
                    {
                        _diagnostics.Error(DiagnosticCode.UnknownReference, reference.Source, $"Condition '{reference.Id}' is not defined.");
                        return null;
                    }

                    return new ConditionRef(reference.Id, reference.Source);

                default:
                    return null;
            }
        }

        /// <summary>Addresses of named conditions referenced directly by this subtree.</summary>
        public static void CollectReferences(ConditionDraftNode draft, List<RefConditionDraft> into)
        {
            switch (draft)
            {
                case CompoundConditionDraft compound:
                    foreach (var child in compound.Children)
                    {
                        CollectReferences(child, into);
                    }

                    break;
                case NotConditionDraft not:
                    CollectReferences(not.Child, into);
                    break;
                case RefConditionDraft reference:
                    into.Add(reference);
                    break;
            }
        }

        private ConditionNode ResolveAtomic(AtomicConditionDraft draft)
        {
            if (!_drafts.TryGetValue(draft.Target, out var target))
            {
                _diagnostics.Error(DiagnosticCode.UnknownReference, draft.TargetSource, $"Condition target '{draft.Target}' is not defined.");
                return null;
            }

            if (!EntityKinds.CanOwnState(target.Id.Kind))
            {
                _diagnostics.Error(
                    DiagnosticCode.InvalidReferenceKind,
                    draft.TargetSource,
                    $"Condition target '{draft.Target}' has no State: only character, item, object, location and story entities do.");
                return null;
            }

            var address = new StateAddress(draft.Target, draft.Property);
            if (!_states.TryGetValue(address, out var state))
            {
                // A declared or built-in property missing from the table failed its own validation: already reported.
                if (target is WorldDraft world && !world.DeclaredProperties.Contains(draft.Property) && !IsBuiltIn(world, draft.Property))
                {
                    _diagnostics.Error(DiagnosticCode.UnknownStateProperty, draft.PropertySource, $"{draft.Target} has no State property '{draft.Property}'.");
                }

                return null;
            }

            if (!ComparisonOperators.IsAllowedFor(draft.Operator, state.Type))
            {
                _diagnostics.Error(
                    DiagnosticCode.InvalidOperator,
                    draft.OperatorSource,
                    $"Operator '{ComparisonOperators.Name(draft.Operator)}' needs an integer property; {address} is {StateValue.TypeName(state.Type)}.");
                return null;
            }

            if (!StateValueReader.TryRead(draft.Value, state.Type, out var value, out var error))
            {
                _diagnostics.Error(DiagnosticCode.ConditionValueTypeMismatch, draft.ValueSource, $"Value for {address}: {error}");
                return null;
            }

            var check = state.Check(value);
            var admissible = check == ValueCheck.Ok
                || (check == ValueCheck.OutOfRange && ComparisonOperators.IsNumeric(draft.Operator));
            if (!admissible)
            {
                _diagnostics.Error(
                    DiagnosticCode.InvalidComparisonValue,
                    draft.ValueSource,
                    check == ValueCheck.InvalidEnumValue
                        ? $"'{value.AsEnum}' is not a value of {address}."
                        : $"{value} is outside the range of {address}: the comparison could never change its result.");
                return null;
            }

            return new AtomicCondition(address, draft.Operator, value, draft.Source);
        }

        private static bool IsBuiltIn(WorldDraft world, string property) =>
            (world.Id.Kind == EntityKind.Character && property == BuiltInStates.Location)
            || (world.Id.Kind == EntityKind.Item && property == BuiltInStates.Holder);
    }
}
