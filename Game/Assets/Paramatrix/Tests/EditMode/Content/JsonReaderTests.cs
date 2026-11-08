using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Paramatrix.Content;

namespace Paramatrix.Tests.Content
{
    public sealed class JsonReaderTests
    {
        private static JObject ParseObject(string json, DiagnosticBag diagnostics)
        {
            var token = JsonDocument.Parse(new ContentSource("test.json", json), diagnostics);
            Assert.That(token, Is.InstanceOf<JObject>());
            return (JObject)token;
        }

        private static DiagnosticCode[] Codes(DiagnosticBag bag) => bag.Items.Select(d => d.Code).ToArray();

        [Test]
        public void MalformedJsonIsReportedWithPosition()
        {
            var bag = new DiagnosticBag();
            var token = JsonDocument.Parse(new ContentSource("bad.json", "{ 'id': 'item.key', }\n{"), bag);

            Assert.That(token, Is.Null);
            Assert.That(Codes(bag), Is.EqualTo(new[] { DiagnosticCode.InvalidJson }));
            Assert.That(bag.Items[0].Location.Source, Is.EqualTo("bad.json"));
            Assert.That(bag.Items[0].Location.Line, Is.GreaterThan(0));
        }

        [Test]
        public void DuplicatePropertyIsError()
        {
            var bag = new DiagnosticBag();
            var token = JsonDocument.Parse(new ContentSource("dup.json", "{ 'id': 'item.a', 'id': 'item.b' }"), bag);

            Assert.That(token, Is.Null);
            Assert.That(Codes(bag), Is.EqualTo(new[] { DiagnosticCode.InvalidJson }));
        }

        [Test]
        public void CommentsAreAllowed()
        {
            var bag = new DiagnosticBag();
            var token = JsonDocument.Parse(new ContentSource("c.json", "// note\n{ 'a': 1 /* inline */ }"), bag);

            Assert.That(token, Is.Not.Null);
            Assert.That(bag.Items, Is.Empty);
        }

        [Test]
        public void UnreadFieldsAreUnknown()
        {
            var bag = new DiagnosticBag();
            var reader = new JsonObjectReader(ParseObject("{ 'id': 'item.key', 'nmae': 'Ключ' }", bag), "test.json", bag);

            Assert.That(reader.RequiredString("id", out var id), Is.True);
            Assert.That(id, Is.EqualTo("item.key"));
            reader.ReportUnknownFields();

            Assert.That(Codes(bag), Is.EqualTo(new[] { DiagnosticCode.UnknownField }));
            Assert.That(bag.Items[0].Message, Does.Contain("nmae"));
            Assert.That(bag.Items[0].Location.Path, Is.EqualTo("nmae"));
        }

        [Test]
        public void MissingRequiredField()
        {
            var bag = new DiagnosticBag();
            var reader = new JsonObjectReader(ParseObject("{ }", bag), "test.json", bag);

            Assert.That(reader.RequiredString("id", out _), Is.False);
            Assert.That(Codes(bag), Is.EqualTo(new[] { DiagnosticCode.MissingRequiredField }));
        }

        [TestCase("{ 'v': 'true' }")]
        [TestCase("{ 'v': 1 }")]
        [TestCase("{ 'v': null }")]
        public void BooleanTypeIsStrict(string json)
        {
            var bag = new DiagnosticBag();
            var reader = new JsonObjectReader(ParseObject(json, bag), "test.json", bag);

            Assert.That(reader.RequiredBoolean("v", out _), Is.False);
            Assert.That(Codes(bag), Is.EqualTo(new[] { DiagnosticCode.UnexpectedJsonType }));
        }

        [TestCase("{ 'v': 1.5 }")]
        [TestCase("{ 'v': '3' }")]
        [TestCase("{ 'v': 3000000000 }")]
        [TestCase("{ 'v': 99999999999999999999999 }")]
        public void IntegerTypeAndRangeAreStrict(string json)
        {
            var bag = new DiagnosticBag();
            var reader = new JsonObjectReader(ParseObject(json, bag), "test.json", bag);

            Assert.That(reader.RequiredInteger("v", out _), Is.False);
            Assert.That(Codes(bag), Is.EqualTo(new[] { DiagnosticCode.UnexpectedJsonType }));
        }

        [Test]
        public void IntegerBoundsAreAccepted()
        {
            var bag = new DiagnosticBag();
            var reader = new JsonObjectReader(ParseObject("{ 'min': -2147483648, 'max': 2147483647 }", bag), "test.json", bag);

            Assert.That(reader.RequiredInteger("min", out var min), Is.True);
            Assert.That(reader.RequiredInteger("max", out var max), Is.True);
            Assert.That(min, Is.EqualTo(int.MinValue));
            Assert.That(max, Is.EqualTo(int.MaxValue));
            Assert.That(bag.Items, Is.Empty);
        }

        [Test]
        public void EntityIdFieldChecksFormatAndKind()
        {
            var bag = new DiagnosticBag();
            var reader = new JsonObjectReader(
                ParseObject("{ 'ok': 'location.office', 'bad': 'Office', 'wrong': 'item.key' }", bag), "test.json", bag);

            Assert.That(reader.RequiredEntityId("ok", EntityKind.Location, out var ok), Is.True);
            Assert.That(ok.Kind, Is.EqualTo(EntityKind.Location));
            Assert.That(reader.RequiredEntityId("bad", null, out _), Is.False);
            Assert.That(reader.RequiredEntityId("wrong", EntityKind.Location, out _), Is.False);
            Assert.That(Codes(bag), Is.EqualTo(new[] { DiagnosticCode.InvalidEntityId, DiagnosticCode.InvalidReferenceKind }));
        }

        [Test]
        public void LocationPointsToNestedPath()
        {
            var bag = new DiagnosticBag();
            var root = ParseObject("{ 'definitions': [ { 'id': 'item.key', 'extra': 1 } ] }", bag);
            var reader = new JsonObjectReader((JObject)root["definitions"][0], "test.json", bag);

            reader.Optional("id");
            reader.ReportUnknownFields();

            Assert.That(bag.Items.Single().Location.Path, Is.EqualTo("definitions[0].extra"));
            Assert.That(bag.Items.Single().Location.Line, Is.EqualTo(1));
        }
    }
}
