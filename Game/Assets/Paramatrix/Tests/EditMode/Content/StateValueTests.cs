using System;
using NUnit.Framework;
using Paramatrix.Content;

namespace Paramatrix.Tests.Content
{
    public sealed class StateValueTests
    {
        [Test]
        public void TypedAccessorsReturnPayload()
        {
            Assert.That(StateValue.Boolean(true).AsBoolean, Is.True);
            Assert.That(StateValue.Integer(42).AsInteger, Is.EqualTo(42));
            Assert.That(StateValue.String("текст").AsString, Is.EqualTo("текст"));
            Assert.That(StateValue.Enum("calm").AsEnum, Is.EqualTo("calm"));
        }

        [Test]
        public void ReadingAsAnotherTypeThrows()
        {
            Assert.Throws<InvalidOperationException>(() => { var _ = StateValue.Boolean(true).AsInteger; });
            Assert.Throws<InvalidOperationException>(() => { var _ = StateValue.Integer(1).AsBoolean; });
            Assert.Throws<InvalidOperationException>(() => { var _ = StateValue.Enum("a").AsString; });
            Assert.Throws<InvalidOperationException>(() => { var _ = StateValue.String("a").AsEnum; });
        }

        [Test]
        public void NoImplicitConversionInEquality()
        {
            // Architecture 0.3.2 §3: "1" is not 1, true is not 1, enum "a" is not string "a".
            Assert.That(StateValue.String("1"), Is.Not.EqualTo(StateValue.Integer(1)));
            Assert.That(StateValue.Boolean(true), Is.Not.EqualTo(StateValue.Integer(1)));
            Assert.That(StateValue.Enum("a"), Is.Not.EqualTo(StateValue.String("a")));
        }

        [Test]
        public void EqualValuesAreEqual()
        {
            Assert.That(StateValue.Integer(5), Is.EqualTo(StateValue.Integer(5)));
            Assert.That(StateValue.Enum("calm") == StateValue.Enum("calm"), Is.True);
            Assert.That(StateValue.String("a").GetHashCode(), Is.EqualTo(StateValue.String("a").GetHashCode()));
        }

        [Test]
        public void DefaultValueIsInvalid()
        {
            var value = default(StateValue);
            Assert.That(value.IsValid, Is.False);
            Assert.Throws<InvalidOperationException>(() => { var _ = value.AsBoolean; });
        }

        [Test]
        public void FactoriesRejectMissingText()
        {
            Assert.Throws<ArgumentNullException>(() => StateValue.String(null));
            Assert.Throws<ArgumentException>(() => StateValue.Enum(""));
        }

        [Test]
        public void DiagnosticRendering()
        {
            Assert.That(StateValue.Boolean(false).ToString(), Is.EqualTo("false"));
            Assert.That(StateValue.Integer(-3).ToString(), Is.EqualTo("-3"));
            Assert.That(StateValue.String("x").ToString(), Is.EqualTo("\"x\""));
            Assert.That(StateValue.Enum("calm").ToString(), Is.EqualTo("calm"));
        }
    }
}
