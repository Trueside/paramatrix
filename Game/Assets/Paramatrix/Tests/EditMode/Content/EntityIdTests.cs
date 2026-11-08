using System;
using NUnit.Framework;
using Paramatrix.Content;

namespace Paramatrix.Tests.Content
{
    public sealed class EntityIdTests
    {
        [TestCase("character.paranoik", EntityKind.Character, "paranoik")]
        [TestCase("item.red_pill", EntityKind.Item, "red_pill")]
        [TestCase("object.office_door", EntityKind.Object, "office_door")]
        [TestCase("location.office", EntityKind.Location, "office")]
        [TestCase("story.morpheus_meeting", EntityKind.Story, "morpheus_meeting")]
        [TestCase("event.morpheus_intro", EntityKind.Event, "morpheus_intro")]
        [TestCase("condition.door_is_locked", EntityKind.Condition, "door_is_locked")]
        [TestCase("dialogue.door_locked", EntityKind.Dialogue, "door_locked")]
        [TestCase("response.fallback_take", EntityKind.Response, "fallback_take")]
        [TestCase("game.settings", EntityKind.Game, "settings")]
        [TestCase("item.key2", EntityKind.Item, "key2")]
        [TestCase("item.2nd_key", EntityKind.Item, "2nd_key")]
        public void ValidIdIsParsed(string text, EntityKind kind, string name)
        {
            Assert.That(EntityId.TryParse(text, out var id, out var error), Is.True, error);
            Assert.That(id.IsValid, Is.True);
            Assert.That(id.Value, Is.EqualTo(text));
            Assert.That(id.Kind, Is.EqualTo(kind));
            Assert.That(id.Name, Is.EqualTo(name));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("office")]
        [TestCase(".office")]
        [TestCase("location.")]
        [TestCase("location.office.door")]
        [TestCase("place.office")]
        [TestCase("Location.office")]
        [TestCase("location.Office")]
        [TestCase("location.office-door")]
        [TestCase("location.офис")]
        [TestCase("location.office door")]
        public void InvalidIdIsRejected(string text)
        {
            Assert.That(EntityId.TryParse(text, out var id, out var error), Is.False);
            Assert.That(id.IsValid, Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void DefaultIdThrowsOnAccess()
        {
            var id = default(EntityId);
            Assert.That(id.IsValid, Is.False);
            Assert.Throws<InvalidOperationException>(() => { var _ = id.Value; });
            Assert.Throws<InvalidOperationException>(() => { var _ = id.Kind; });
        }

        [Test]
        public void ParseThrowsOnInvalid()
        {
            Assert.Throws<ArgumentException>(() => EntityId.Parse("nonsense"));
        }

        [Test]
        public void EqualityAndOrderingAreOrdinal()
        {
            var a = EntityId.Parse("item.a");
            var b = EntityId.Parse("item.b");
            Assert.That(a == EntityId.Parse("item.a"), Is.True);
            Assert.That(a != b, Is.True);
            Assert.That(a.CompareTo(b), Is.LessThan(0));
            Assert.That(a.GetHashCode(), Is.EqualTo(EntityId.Parse("item.a").GetHashCode()));
        }

        [TestCase(EntityKind.Character, true)]
        [TestCase(EntityKind.Item, true)]
        [TestCase(EntityKind.Object, true)]
        [TestCase(EntityKind.Location, true)]
        [TestCase(EntityKind.Story, true)]
        [TestCase(EntityKind.Game, false)]
        [TestCase(EntityKind.Condition, false)]
        [TestCase(EntityKind.Event, false)]
        [TestCase(EntityKind.Dialogue, false)]
        [TestCase(EntityKind.Response, false)]
        public void OnlyWorldKindsOwnState(EntityKind kind, bool canOwnState)
        {
            Assert.That(EntityKinds.CanOwnState(kind), Is.EqualTo(canOwnState));
        }
    }
}
