using NUnit.Framework;
using Paramatrix.Content;
using static Paramatrix.Tests.Content.TestContent;

namespace Paramatrix.Tests.Conditions
{
    /// <summary>Validator rules for conditions (Architecture 0.3.2 §10).</summary>
    public sealed class ConditionContentTests
    {
        internal const string Door =
            "{ 'id': 'object.office_door', 'primary_interaction': 'exit', 'states': {" +
            "  'open':   { 'type': 'boolean', 'default': false, 'persistence': 'persistent', 'mutation_policy': 'scenario_or_system' }," +
            "  'charge': { 'type': 'integer', 'default': 0, 'min': 0, 'max': 3, 'persistence': 'persistent', 'mutation_policy': 'scenario_or_system' }," +
            "  'mood':   { 'type': 'enum', 'values': ['calm', 'alarmed'], 'default': 'calm', 'persistence': 'persistent', 'mutation_policy': 'scenario_only' }," +
            "  'label':  { 'type': 'string', 'default': 'Офис', 'persistence': 'persistent', 'mutation_policy': 'scenario_only' } } }";

        private static string Named(string id, string condition) => "{ 'id': '" + id + "', 'condition': " + condition + " }";

        private static string Atomic(string target, string property, string op, string value) =>
            "{ 'target': '" + target + "', 'property': '" + property + "', 'operator': '" + op + "', 'value': " + value + " }";

        private static ContentLoadResult LoadCondition(string condition) => Load(Door, Named("condition.test", condition));

        [Test]
        public void AllFormsLoad()
        {
            var registry = LoadValid(
                Door,
                Named("condition.open", Atomic("object.office_door", "open", "equals", "true")),
                Named("condition.combo",
                    "{ 'all': [ { 'ref': 'condition.open' }," +
                    "  { 'any': [ " + Atomic("object.office_door", "charge", "greater_or_equal", "2") + ", " +
                    "             { 'not': " + Atomic("object.office_door", "mood", "equals", "'calm'") + " } ] }," +
                    "  " + Atomic("character.paranoik", "location", "equals", "'location.office'") + " ] }"));

            var combo = registry.Get<NamedConditionDefinition>(EntityId.Parse("condition.combo"));
            var all = (AllCondition)combo.Root;
            Assert.That(all.Children.Count, Is.EqualTo(3));
            Assert.That(((ConditionRef)all.Children[0]).ConditionId, Is.EqualTo(EntityId.Parse("condition.open")));
            var any = (AnyCondition)all.Children[1];
            Assert.That(((AtomicCondition)any.Children[0]).Value, Is.EqualTo(StateValue.Integer(2)));
            var not = (NotCondition)any.Children[1];
            Assert.That(((AtomicCondition)not.Child).Value, Is.EqualTo(StateValue.Enum("calm")));
            Assert.That(((AtomicCondition)all.Children[2]).Target, Is.EqualTo(StateAddress.Parse("character.paranoik", "location")));
        }

        [TestCase("not_equals", "'Офис'")]
        [TestCase("equals", "''")]
        public void StringComparisons(string op, string value) =>
            Assert.That(LoadCondition(Atomic("object.office_door", "label", op, value)).Succeeded, Is.True);

        [TestCase("greater_than", "5")]
        [TestCase("less_than", "-1")]
        public void OrderingBeyondRangeIsAllowed(string op, string value) =>
            Assert.That(LoadCondition(Atomic("object.office_door", "charge", op, value)).Succeeded, Is.True);

        [Test]
        public void ConditionFieldRequired() => AssertErrors(Load("{ 'id': 'condition.x' }"), DiagnosticCode.MissingRequiredField);

        [Test]
        public void NodeMustBeObject() => AssertErrors(LoadCondition("true"), DiagnosticCode.UnexpectedJsonType);

        [Test]
        public void NodeNeedsAForm() => AssertErrors(LoadCondition("{ 'value': true, 'target': 'object.office_door' , 'x': 1 }"), DiagnosticCode.MissingRequiredField, DiagnosticCode.MissingRequiredField, DiagnosticCode.UnknownField);

        [Test]
        public void EmptyNodeIsInvalidForm() => AssertErrors(LoadCondition("{ }"), DiagnosticCode.InvalidConditionForm);

        [Test]
        public void MixedFormsAreInvalid() =>
            AssertErrors(LoadCondition("{ 'all': [], 'ref': 'condition.a' }"), DiagnosticCode.InvalidConditionForm);

        [Test]
        public void UnknownFieldInAtomic() =>
            AssertErrors(LoadCondition("{ 'target': 'object.office_door', 'property': 'open', 'operator': 'equals', 'value': true, 'negate': true }"), DiagnosticCode.UnknownField);

        [TestCase("all")]
        [TestCase("any")]
        public void EmptyCompoundIsError(string form) =>
            AssertErrors(LoadCondition("{ '" + form + "': [] }"), DiagnosticCode.EmptyCompoundCondition);

        [Test]
        public void CompoundMustBeArray() => AssertErrors(LoadCondition("{ 'all': {} }"), DiagnosticCode.UnexpectedJsonType);

        [Test]
        public void NotNeedsNode() => AssertErrors(LoadCondition("{ 'not': [] }"), DiagnosticCode.UnexpectedJsonType);

        [Test]
        public void ErrorsInsideNestedNodesAreFound() =>
            AssertErrors(
                LoadCondition("{ 'all': [ " + Atomic("object.office_door", "open", "equals", "1") + ", { 'any': [ " + Atomic("object.window", "open", "equals", "true") + " ] } ] }"),
                DiagnosticCode.ConditionValueTypeMismatch,
                DiagnosticCode.UnknownReference);

        [Test]
        public void TargetMustExist() =>
            AssertErrors(LoadCondition(Atomic("object.window", "open", "equals", "true")), DiagnosticCode.UnknownReference);

        [Test]
        public void TargetMustOwnState() =>
            AssertErrors(Load(Door, Named("condition.a", Atomic("object.office_door", "open", "equals", "true")),
                Named("condition.test", Atomic("condition.a", "open", "equals", "true"))), DiagnosticCode.InvalidReferenceKind);

        [Test]
        public void PropertyMustExist() =>
            AssertErrors(LoadCondition(Atomic("object.office_door", "broken", "equals", "true")), DiagnosticCode.UnknownStateProperty);

        [Test]
        public void PropertyNameMustBeValid() =>
            AssertErrors(LoadCondition(Atomic("object.office_door", "Open", "equals", "true")), DiagnosticCode.InvalidPropertyName);

        [Test]
        public void InvalidStatePropertyIsNotReportedTwice() =>
            AssertErrors(
                Load("{ 'id': 'story.x', 'states': { 'flag': { 'type': 'bool', 'default': true, 'persistence': 'persistent', 'mutation_policy': 'scenario_only' } } }",
                    Named("condition.test", Atomic("story.x", "flag", "equals", "true"))),
                DiagnosticCode.InvalidStateType);

        [Test]
        public void UnknownOperator() =>
            AssertErrors(LoadCondition(Atomic("object.office_door", "open", "is", "true")), DiagnosticCode.InvalidOperator);

        [TestCase("open", "true")]
        [TestCase("mood", "'calm'")]
        [TestCase("label", "'a'")]
        public void NumericOperatorOnlyForInteger(string property, string value) =>
            AssertErrors(LoadCondition(Atomic("object.office_door", property, "greater_than", value)), DiagnosticCode.InvalidOperator);

        [TestCase("open", "'true'")]
        [TestCase("open", "1")]
        [TestCase("charge", "'1'")]
        [TestCase("charge", "true")]
        [TestCase("mood", "1")]
        [TestCase("label", "5")]
        public void ValueMustHavePropertyType(string property, string value) =>
            AssertErrors(LoadCondition(Atomic("object.office_door", property, "equals", value)), DiagnosticCode.ConditionValueTypeMismatch);

        [Test]
        public void EnumValueMustBeDeclared() =>
            AssertErrors(LoadCondition(Atomic("object.office_door", "mood", "equals", "'angry'")), DiagnosticCode.InvalidComparisonValue);

        [Test]
        public void BuiltInEnumValueMustBeDeclared() =>
            AssertErrors(LoadCondition(Atomic("character.paranoik", "location", "equals", "'location.roof'")), DiagnosticCode.InvalidComparisonValue);

        [TestCase("equals")]
        [TestCase("not_equals")]
        public void EqualityOutsideRangeIsError(string op) =>
            AssertErrors(LoadCondition(Atomic("object.office_door", "charge", op, "7")), DiagnosticCode.InvalidComparisonValue);

        [Test]
        public void RefMustExist() => AssertErrors(LoadCondition("{ 'ref': 'condition.missing' }"), DiagnosticCode.UnknownReference);

        [Test]
        public void RefMustBeCondition() => AssertErrors(LoadCondition("{ 'ref': 'object.office_door' }"), DiagnosticCode.InvalidReferenceKind);

        [Test]
        public void RefToBrokenConditionIsNotReportedTwice() =>
            AssertErrors(
                Load(Door, Named("condition.broken", "{ }"), Named("condition.test", "{ 'ref': 'condition.broken' }")),
                DiagnosticCode.InvalidConditionForm);

        [Test]
        public void SelfReferenceIsCycle() =>
            AssertErrors(Load(Named("condition.a", "{ 'ref': 'condition.a' }")), DiagnosticCode.ConditionCycle);

        [Test]
        public void IndirectCycleIsReportedOnce()
        {
            var result = Load(
                Named("condition.a", "{ 'not': { 'ref': 'condition.b' } }"),
                Named("condition.b", "{ 'all': [ { 'ref': 'condition.c' } ] }"),
                Named("condition.c", "{ 'ref': 'condition.a' }"));

            AssertErrors(result, DiagnosticCode.ConditionCycle);
            Assert.That(result.Diagnostics[0].Message, Does.Contain("condition.a → condition.b → condition.c → condition.a"));
        }

        [Test]
        public void SharedReferenceIsNotCycle() =>
            Assert.That(Load(
                Door,
                Named("condition.base", Atomic("object.office_door", "open", "equals", "true")),
                Named("condition.x", "{ 'all': [ { 'ref': 'condition.base' }, { 'ref': 'condition.base' } ] }"),
                Named("condition.y", "{ 'any': [ { 'ref': 'condition.x' }, { 'ref': 'condition.base' } ] }")).Succeeded, Is.True);
    }
}
