using System.Linq;
using NUnit.Framework;
using Paramatrix.Conditions;
using Paramatrix.Content;
using Paramatrix.State;
using Paramatrix.Tests.Content;

namespace Paramatrix.Tests.Conditions
{
    public sealed class ConditionEvaluatorTests
    {
        private static readonly StateAddress Open = StateAddress.Parse("object.office_door", "open");
        private static readonly StateAddress Charge = StateAddress.Parse("object.office_door", "charge");
        private static readonly StateAddress Mood = StateAddress.Parse("object.office_door", "mood");
        private static readonly StateAddress Label = StateAddress.Parse("object.office_door", "label");

        private ContentRegistry _registry;
        private StateSystem _state;
        private ConditionEvaluator _conditions;

        [SetUp]
        public void SetUp()
        {
            _registry = TestContent.LoadValid(
                ConditionContentTests.Door,
                "{ 'id': 'condition.door_open', 'condition': { 'target': 'object.office_door', 'property': 'open', 'operator': 'equals', 'value': true } }",
                "{ 'id': 'condition.can_enter', 'condition': { 'all': [ { 'ref': 'condition.door_open' }," +
                "   { 'target': 'object.office_door', 'property': 'charge', 'operator': 'greater_or_equal', 'value': 2 } ] } }");
            _state = new StateSystem(_registry, new DiagnosticBag());
            _conditions = new ConditionEvaluator(_registry, _state);
        }

        private static AtomicCondition Atomic(StateAddress target, ComparisonOperator op, StateValue value) =>
            new AtomicCondition(target, op, value, SourceLocation.None);

        private ConditionResult Eval(ConditionNode node) => _conditions.Evaluate(node);

        private void Set(StateAddress address, StateValue value) =>
            Assert.That(_state.Set(address, value, MutationSource.Scenario).Status, Is.Not.EqualTo(MutationStatus.Rejected));

        private static readonly ConditionNode TrueNode = new AtomicCondition(Open, ComparisonOperator.Equals, StateValue.Boolean(false), SourceLocation.None);
        private static readonly ConditionNode FalseNode = new AtomicCondition(Open, ComparisonOperator.Equals, StateValue.Boolean(true), SourceLocation.None);
        private static readonly ConditionNode ErrorNode = new AtomicCondition(Open, ComparisonOperator.Equals, StateValue.Integer(1), SourceLocation.None);

        // ---------- atomic ----------

        [Test]
        public void EqualsAndNotEquals()
        {
            Assert.That(Eval(Atomic(Open, ComparisonOperator.Equals, StateValue.Boolean(false))).Outcome, Is.EqualTo(ConditionOutcome.True));
            Assert.That(Eval(Atomic(Open, ComparisonOperator.NotEquals, StateValue.Boolean(false))).Outcome, Is.EqualTo(ConditionOutcome.False));
            Set(Open, StateValue.Boolean(true));
            Assert.That(Eval(Atomic(Open, ComparisonOperator.Equals, StateValue.Boolean(true))).Outcome, Is.EqualTo(ConditionOutcome.True));
        }

        [TestCase(ComparisonOperator.GreaterThan, 1, true)]
        [TestCase(ComparisonOperator.GreaterThan, 2, false)]
        [TestCase(ComparisonOperator.GreaterOrEqual, 2, true)]
        [TestCase(ComparisonOperator.GreaterOrEqual, 3, false)]
        [TestCase(ComparisonOperator.LessThan, 3, true)]
        [TestCase(ComparisonOperator.LessThan, 2, false)]
        [TestCase(ComparisonOperator.LessOrEqual, 2, true)]
        [TestCase(ComparisonOperator.LessOrEqual, 1, false)]
        public void IntegerComparisons(ComparisonOperator op, int value, bool expected)
        {
            _state.Set(Charge, StateValue.Integer(2), MutationSource.System);
            Assert.That(Eval(Atomic(Charge, op, StateValue.Integer(value))).IsTrue, Is.EqualTo(expected));
        }

        [Test]
        public void EnumAndStringComparison()
        {
            Assert.That(Eval(Atomic(Mood, ComparisonOperator.Equals, StateValue.Enum("calm"))).IsTrue, Is.True);
            Assert.That(Eval(Atomic(Mood, ComparisonOperator.NotEquals, StateValue.Enum("alarmed"))).IsTrue, Is.True);
            Assert.That(Eval(Atomic(Label, ComparisonOperator.Equals, StateValue.String("Офис"))).IsTrue, Is.True);
        }

        [Test]
        public void TypeMismatchIsErrorNotFalse()
        {
            var result = Eval(Atomic(Open, ComparisonOperator.Equals, StateValue.Integer(1)));
            Assert.That(result.Outcome, Is.EqualTo(ConditionOutcome.Error));
            Assert.That(result.Error, Is.EqualTo(ConditionError.TypeMismatch));
            Assert.That(result.Detail, Is.Not.Null);

            Assert.That(Eval(Atomic(Mood, ComparisonOperator.Equals, StateValue.String("calm"))).Error, Is.EqualTo(ConditionError.TypeMismatch));
        }

        [Test]
        public void NumericOperatorOnNonIntegerIsError()
        {
            Assert.That(Eval(Atomic(Mood, ComparisonOperator.GreaterThan, StateValue.Enum("calm"))).Error, Is.EqualTo(ConditionError.InvalidOperator));
            Assert.That(Eval(Atomic(Open, ComparisonOperator.LessThan, StateValue.Boolean(true))).Error, Is.EqualTo(ConditionError.InvalidOperator));
        }

        [Test]
        public void UnknownEntityAndProperty()
        {
            Assert.That(Eval(Atomic(StateAddress.Parse("object.window", "open"), ComparisonOperator.Equals, StateValue.Boolean(true))).Error,
                Is.EqualTo(ConditionError.UnknownEntity));
            Assert.That(Eval(Atomic(StateAddress.Parse("object.office_door", "broken"), ComparisonOperator.Equals, StateValue.Boolean(true))).Error,
                Is.EqualTo(ConditionError.UnknownProperty));
        }

        // ---------- compound ----------

        [Test]
        public void All()
        {
            Assert.That(Eval(new AllCondition(new[] { TrueNode, TrueNode }, SourceLocation.None)).Outcome, Is.EqualTo(ConditionOutcome.True));
            Assert.That(Eval(new AllCondition(new[] { TrueNode, FalseNode }, SourceLocation.None)).Outcome, Is.EqualTo(ConditionOutcome.False));
        }

        [Test]
        public void Any()
        {
            Assert.That(Eval(new AnyCondition(new[] { FalseNode, TrueNode }, SourceLocation.None)).Outcome, Is.EqualTo(ConditionOutcome.True));
            Assert.That(Eval(new AnyCondition(new[] { FalseNode, FalseNode }, SourceLocation.None)).Outcome, Is.EqualTo(ConditionOutcome.False));
        }

        [Test]
        public void Not()
        {
            Assert.That(Eval(new NotCondition(TrueNode, SourceLocation.None)).Outcome, Is.EqualTo(ConditionOutcome.False));
            Assert.That(Eval(new NotCondition(FalseNode, SourceLocation.None)).Outcome, Is.EqualTo(ConditionOutcome.True));
        }

        [Test]
        public void ErrorPropagatesThroughEveryCompound()
        {
            // An Error is never masked by a True in 'any' or a False in 'all' (0.3.2 §4.4).
            Assert.That(Eval(new AnyCondition(new[] { TrueNode, ErrorNode }, SourceLocation.None)).Outcome, Is.EqualTo(ConditionOutcome.Error));
            Assert.That(Eval(new AllCondition(new[] { FalseNode, ErrorNode }, SourceLocation.None)).Outcome, Is.EqualTo(ConditionOutcome.Error));
            Assert.That(Eval(new NotCondition(ErrorNode, SourceLocation.None)).Outcome, Is.EqualTo(ConditionOutcome.Error));
        }

        [Test]
        public void NestedCombination()
        {
            var node = new AllCondition(new ConditionNode[]
            {
                new AnyCondition(new[] { FalseNode, TrueNode }, SourceLocation.None),
                new NotCondition(FalseNode, SourceLocation.None),
            }, SourceLocation.None);

            Assert.That(Eval(node).IsTrue, Is.True);
        }

        // ---------- named ----------

        [Test]
        public void NamedConditionsFollowState()
        {
            var canEnter = EntityId.Parse("condition.can_enter");
            Assert.That(_conditions.Evaluate(canEnter).Outcome, Is.EqualTo(ConditionOutcome.False));

            Set(Open, StateValue.Boolean(true));
            Assert.That(_conditions.Evaluate(canEnter).Outcome, Is.EqualTo(ConditionOutcome.False));

            _state.Set(Charge, StateValue.Integer(2), MutationSource.System);
            Assert.That(_conditions.Evaluate(canEnter).Outcome, Is.EqualTo(ConditionOutcome.True));
            Assert.That(_conditions.Evaluate(EntityId.Parse("condition.door_open")).Outcome, Is.EqualTo(ConditionOutcome.True), "reuse");
        }

        [Test]
        public void UnknownConditionIsError()
        {
            Assert.That(_conditions.Evaluate(EntityId.Parse("condition.missing")).Error, Is.EqualTo(ConditionError.UnknownCondition));
            Assert.That(Eval(new ConditionRef(EntityId.Parse("condition.missing"), SourceLocation.None)).Error, Is.EqualTo(ConditionError.UnknownCondition));
        }

        [Test]
        public void RuntimeCycleGuard()
        {
            // Validator rejects cycles in content; the evaluator still guards against trees built in code.
            var a = EntityId.Parse("condition.a");
            var b = EntityId.Parse("condition.b");
            var registry = new ContentRegistry(new Definition[]
            {
                new GameSettingsDefinition(EntityId.Parse("game.settings"), SourceLocation.None, EntityId.Parse("character.paranoik")),
                new NamedConditionDefinition(a, SourceLocation.None, new ConditionRef(b, SourceLocation.None)),
                new NamedConditionDefinition(b, SourceLocation.None, new NotCondition(new ConditionRef(a, SourceLocation.None), SourceLocation.None)),
            });
            var evaluator = new ConditionEvaluator(registry, _state);

            Assert.That(evaluator.Evaluate(a).Error, Is.EqualTo(ConditionError.ConditionCycle));
        }

        [Test]
        public void EvaluationDoesNotChangeState()
        {
            var changes = 0;
            _state.Changed += _ => changes++;
            var before = _state.ExportPersistent().Entries.Select(e => e.Value).ToArray();

            _conditions.Evaluate(EntityId.Parse("condition.can_enter"));
            Eval(new AnyCondition(new[] { TrueNode, ErrorNode }, SourceLocation.None));

            Assert.That(changes, Is.Zero);
            Assert.That(_state.ExportPersistent().Entries.Select(e => e.Value).ToArray(), Is.EqualTo(before));
        }

        // ---------- dependencies ----------

        [Test]
        public void DependenciesExpandReferencesSortedAndDistinct()
        {
            var node = new AllCondition(new ConditionNode[]
            {
                new ConditionRef(EntityId.Parse("condition.can_enter"), SourceLocation.None),
                Atomic(Mood, ComparisonOperator.Equals, StateValue.Enum("calm")),
                new NotCondition(Atomic(Open, ComparisonOperator.Equals, StateValue.Boolean(true)), SourceLocation.None),
            }, SourceLocation.None);

            var deps = _conditions.GetDependencies(node).Select(a => a.ToString()).ToArray();
            Assert.That(deps, Is.EqualTo(new[]
            {
                "object.office_door.charge",
                "object.office_door.mood",
                "object.office_door.open",
            }));
        }
    }
}
