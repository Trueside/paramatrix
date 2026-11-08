using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Paramatrix.Content;

namespace Paramatrix.Tests.Content
{
    /// <summary>
    /// Builds content for tests. JSON uses single quotes for readability (Newtonsoft accepts them).
    /// <see cref="Load"/> adds a minimal valid base (game settings, hero, office) in a separate file.
    /// </summary>
    internal static class TestContent
    {
        public const string Settings = "{ 'id': 'game.settings', 'player_character': 'character.paranoik' }";

        public const string Hero =
            "{ 'id': 'character.paranoik', 'name': 'Параноик', 'initial_location': 'location.office', 'primary_interaction': 'talk' }";

        public const string Office = "{ 'id': 'location.office', 'scene': 'Location_Office' }";

        public static string File(params string[] definitions) =>
            "{ 'format_version': 1, 'definitions': [ " + string.Join(", ", definitions) + " ] }";

        public static ContentSource Base() => new ContentSource("base.json", File(Settings, Hero, Office));

        public static ContentLoadResult Load(params string[] definitions) =>
            ContentLoader.Load(new[] { Base(), new ContentSource("test.json", File(definitions)) });

        public static ContentLoadResult LoadFiles(params ContentSource[] sources) => ContentLoader.Load(sources);

        /// <summary>A state declaration on a story entity, for state-rule tests.</summary>
        public static string Story(string statesJson) => "{ 'id': 'story.test', 'states': " + statesJson + " }";

        public static ContentRegistry LoadValid(params string[] definitions)
        {
            var result = Load(definitions);
            Assert.That(result.Succeeded, Is.True, Describe(result.Diagnostics));
            return result.Registry;
        }

        public static DiagnosticCode[] ErrorCodes(ContentLoadResult result) =>
            result.Diagnostics.Where(d => d.Severity == Severity.Error).Select(d => d.Code).ToArray();

        /// <summary>Asserts loading failed with exactly the given error codes (order-insensitive).</summary>
        public static void AssertErrors(ContentLoadResult result, params DiagnosticCode[] expected)
        {
            Assert.That(result.Succeeded, Is.False, "Content was expected to be rejected.");
            Assert.That(ErrorCodes(result), Is.EquivalentTo(expected), Describe(result.Diagnostics));
        }

        public static string Describe(IEnumerable<Diagnostic> diagnostics) =>
            string.Join("\n", diagnostics.Select(d => d.ToString()));
    }
}
