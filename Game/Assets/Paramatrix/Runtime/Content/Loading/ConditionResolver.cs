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
            if (!TryResolveState(draft.Target, draft.TargetSource, draft.Property, draft.PropertySource, out var state))
            {
                return null;
            }

            var address = state.Address;
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

        /// <summary>
        /// Resolves "entity + property" to its State definition, reporting a missing or stateless entity
        /// and a missing property. A declared or built-in property that failed its own validation is not
        /// reported again. Shared by conditions, triggers and actions.
        /// </summary>
        public bool TryResolveState(EntityId target, SourceLocation targetSource, string property, SourceLocation propertySource, out StateDefinition state)
        {
            state = null;
            if (!_drafts.TryGetValue(target, out var draft))
            {
                _diagnostics.Error(DiagnosticCode.UnknownReference, targetSource, $"'{target}' is not defined.");
                return false;
            }

            if (!EntityKinds.CanOwnState(target.Kind))
            {
                _diagnostics.Error(
                    DiagnosticCode.InvalidReferenceKind,
                    targetSource,
                    $"'{target}' has no State: only character, item, object, location and story entities do.");
                return false;
            }

            if (_states.TryGetValue(new StateAddress(target, property), out state))
            {
                return true;
            }

            if (draft is WorldDraft world && !world.DeclaredProperties.Contains(property) && !IsBuiltIn(world, property))
            {
                _diagnostics.Error(DiagnosticCode.UnknownStateProperty, propertySource, $"{target} has no State property '{property}'.");
            }

            return false;
        }

        private static bool IsBuiltIn(WorldDraft world, string property) =>
            (world.Id.Kind == EntityKind.Character && property == BuiltInStates.Location)
            || (world.Id.Kind == EntityKind.Item && property == BuiltInStates.Holder);
    }
}
