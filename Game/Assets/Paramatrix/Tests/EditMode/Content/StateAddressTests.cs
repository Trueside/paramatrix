using System;
using NUnit.Framework;
using Paramatrix.Content;

namespace Paramatrix.Tests.Content
{
    public sealed class StateAddressTests
    {
        [TestCase("open")]
        [TestCase("met_morpheus")]
        [TestCase("charge2")]
        public void ValidPropertyName(string name)
        {
            Assert.That(StateAddress.IsValidPropertyName(name), Is.True);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("Open")]
        [TestCase("2open")]
        [TestCase("_open")]
        [TestCase("is.open")]
        [TestCase("is-open")]
        public void InvalidPropertyName(string name)
        {
            Assert.That(StateAddress.IsValidPropertyName(name), Is.False);
        }

        [Test]
        public void DottedFormIsForDiagnostics()
        {
            var address = StateAddress.Parse("object.office_door", "open");
            Assert.That(address.ToString(), Is.EqualTo("object.office_door.open"));
            Assert.That(address.Entity, Is.EqualTo(EntityId.Parse("object.office_door")));
            Assert.That(address.Property, Is.EqualTo("open"));
        }

        [Test]
        public void ConstructorRejectsInvalidParts()
        {
            Assert.Throws<ArgumentException>(() => new StateAddress(default, "open"));
            Assert.Throws<ArgumentException>(() => new StateAddress(EntityId.Parse("item.key"), "Bad"));
        }

        [Test]
        public void EqualityByEntityAndProperty()
        {
            Assert.That(StateAddress.Parse("item.key", "holder"), Is.EqualTo(StateAddress.Parse("item.key", "holder")));
            Assert.That(StateAddress.Parse("item.key", "holder"), Is.Not.EqualTo(StateAddress.Parse("item.key", "used")));
            Assert.That(StateAddress.Parse("item.a", "x").CompareTo(StateAddress.Parse("item.b", "a")), Is.LessThan(0));
        }
    }
}
