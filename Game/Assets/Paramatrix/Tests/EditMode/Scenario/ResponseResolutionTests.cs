using NUnit.Framework;
using Paramatrix.Content;
using Paramatrix.Scenario;
using Paramatrix.State;
using static Paramatrix.Tests.Scenario.ScenarioHarness;

namespace Paramatrix.Tests.Scenario
{
    /// <summary>Р6: events first, then responses specific → contextual → generic → fallback.</summary>
    public sealed class ResponseResolutionTests
    {
        private const string Door = "{ 'id': 'object.office_door', 'primary_interaction': 'exit' }";
        private const string Window = "{ 'id': 'object.window', 'primary_interaction': 'primary' }";
        private const string Trinity = "{ 'id': 'character.trinity', 'name': 'Тринити', 'initial_location': 'location.office', 'primary_interaction': 'talk' }";
        private const string Key = "{ 'id': 'item.key', 'name': 'Ключ', 'description': 'Латунный.', 'initial_holder': 'inventory' }";

        private static readonly string[] World = { Door, Window, Trinity, Key, Flags("locked", "night") };

        private static ScenarioHarness Create(params string[] definitions) => ScenarioHarness.Create(With(World, definitions));

        private static string Response(string id, string tier, string interaction, string extra, string text) =>
            "{ 'id': '" + id + "', 'tier': '" + tier + "', 'interaction': '" + interaction + "'" + extra + ", 'text': '" + text + "' }";

        private static string[] Shown(ScenarioHarness h)
        {
            var log = h.TakeLog();
            h.CompleteAll();
            h.TakeLog();
            return log;
        }

        [Test]
        public void SpecificResponseWithCondition()
        {
            var h = Create(Response("response.locked", "specific", "exit", ", 'target': 'object.office_door', 'condition': " + FlagIs("locked", true), "Заперто."));

            h.Click(InteractionType.Exit, "object.office_door");
            Assert.That(Shown(h), Is.EqualTo(new[] { "acquire", "line:character.paranoik:Туда не пройти." }), "condition false → fallback");

            h.SetSystem("story.flags", "locked", StateValue.Boolean(true));
            h.Click(InteractionType.Exit, "object.office_door");
            Assert.That(Shown(h), Is.EqualTo(new[] { "acquire", "line:character.paranoik:Заперто." }));
            h.AssertNoDiagnostics();
        }

        [Test]
        public void SpecificBeatsContextualBeatsGeneric()
        {
            var h = Create(
                Response("response.specific", "specific", "primary", ", 'target': 'object.window'", "Окно."),
                Response("response.night", "contextual", "primary", ", 'condition': " + FlagIs("night", true), "Ночью всё закрыто."),
                Response("response.hm_1", "generic", "primary", "", "Хм."),
                Response("response.hm_2", "generic", "primary", "", "Нет."));

            h.Click(InteractionType.Primary, "object.window");
            Assert.That(Shown(h)[1], Is.EqualTo("line:character.paranoik:Окно."));

            h.Click(InteractionType.Primary, "object.office_door");
            Assert.That(Shown(h)[1], Is.EqualTo("line:character.paranoik:Хм."));

            h.SetSystem("story.flags", "night", StateValue.Boolean(true));
            h.Click(InteractionType.Primary, "object.office_door");
            Assert.That(Shown(h)[1], Is.EqualTo("line:character.paranoik:Ночью всё закрыто."));
        }

        [Test]
        public void EventBeatsResponses()
        {
            var h = Create(
                Response("response.specific", "specific", "primary", ", 'target': 'object.window'", "Окно."),
                Event("event.window", "exclusive", "repeatable", OnClick("primary", "object.window"),
                    "{ 'action': 'set_state', 'target': 'story.flags', 'property': 'night', 'value': true }"));

            h.Click(InteractionType.Primary, "object.window");

            Assert.That(h.TakeLog(), Is.EqualTo(new[] { "acquire", "release:Completed" }));
            Assert.That(h.Get("story.flags", "night").AsBoolean, Is.True);
        }

        [Test]
        public void GenericPoolRotatesWithoutImmediateRepeat()
        {
            var h = Create(
                Response("response.hm_1", "generic", "primary", "", "Раз."),
                Response("response.hm_2", "generic", "primary", "", "Два."),
                Response("response.hm_3", "generic", "primary", "", "Три."));

            var lines = new string[5];
            for (var i = 0; i < lines.Length; i++)
            {
                h.Click(InteractionType.Primary, "object.window");
                lines[i] = Shown(h)[1];
            }

            Assert.That(lines, Is.EqualTo(new[]
            {
                "line:character.paranoik:Раз.",
                "line:character.paranoik:Два.",
                "line:character.paranoik:Три.",
                "line:character.paranoik:Раз.",
                "line:character.paranoik:Два.",
            }));
        }

        [Test]
        public void ConditionalPoolStillAvoidsRepeatWhenPossible()
        {
            var h = Create(
                Response("response.hm_1", "generic", "primary", ", 'condition': " + FlagIs("night", true), "Ночь."),
                Response("response.hm_2", "generic", "primary", "", "Хм."),
                Response("response.hm_3", "generic", "primary", "", "Нет."));

            h.Click(InteractionType.Primary, "object.window");
            Assert.That(Shown(h)[1], Is.EqualTo("line:character.paranoik:Хм."));
            h.Click(InteractionType.Primary, "object.window");
            Assert.That(Shown(h)[1], Is.EqualTo("line:character.paranoik:Нет."));
            h.Click(InteractionType.Primary, "object.window");
            Assert.That(Shown(h)[1], Is.EqualTo("line:character.paranoik:Хм."));
        }

        [Test]
        public void FallbackNoneShowsNothing()
        {
            var h = Create();

            h.Click(InteractionType.Talk, "character.trinity");

            Assert.That(h.TakeLog(), Is.Empty);
            Assert.That(h.HoldsControl, Is.False);
            h.AssertNoDiagnostics();
        }

        [Test]
        public void TalkResponseIsSpokenByTarget()
        {
            var h = Create(Response("response.trinity", "specific", "talk", ", 'target': 'character.trinity'", "Не сейчас."));

            h.Click(InteractionType.Talk, "character.trinity");

            Assert.That(Shown(h)[1], Is.EqualTo("line:character.trinity:Не сейчас."));
        }

        [Test]
        public void ExplicitSpeakerWins()
        {
            var h = Create(Response("response.window", "specific", "inspect", ", 'target': 'object.window', 'speaker': 'character.trinity'", "Там ничего."));

            h.Click(InteractionType.Inspect, "object.window");

            Assert.That(Shown(h)[1], Is.EqualTo("line:character.trinity:Там ничего."));
        }

        [Test]
        public void ItemFilteredResponse()
        {
            var h = Create(Response("response.key_door", "specific", "apply_item", ", 'target': 'object.office_door', 'item': 'item.key'", "Не тот ключ."));

            h.Click(InteractionType.ApplyItem, "object.office_door", "item.key");

            Assert.That(Shown(h)[1], Is.EqualTo("line:character.paranoik:Не тот ключ."));
        }

        [Test]
        public void TwoTrueSpecificResponsesAreAmbiguous()
        {
            var h = Create(
                Response("response.a", "specific", "primary", ", 'target': 'object.window', 'condition': " + FlagIs("locked", false), "А."),
                Response("response.b", "specific", "primary", ", 'target': 'object.window', 'condition': " + FlagIs("night", false), "Б."));

            h.Click(InteractionType.Primary, "object.window");

            Assert.That(h.TakeLog(), Is.Empty);
            Assert.That(h.Errors, Is.EqualTo(new[] { DiagnosticCode.AmbiguousResponse }));
        }

        [Test]
        public void ResponseHoldsControlUntilShown()
        {
            var h = Create(Event("event.other", "exclusive", "repeatable", OnClick("exit", "object.office_door"),
                "{ 'action': 'set_state', 'target': 'story.flags', 'property': 'night', 'value': true }"));

            h.Click(InteractionType.Primary, "object.window");
            Assert.That(h.HoldsControl, Is.True);

            h.Click(InteractionType.Exit, "object.office_door");
            Assert.That(h.Get("story.flags", "night").AsBoolean, Is.False, "player actions are rejected while a response is shown");
            Assert.That(h.Warnings, Is.EqualTo(new[] { DiagnosticCode.PlayerActionRejected }));

            h.Complete();
            Assert.That(h.HoldsControl, Is.False);
        }

        [Test]
        public void ExternalStateChangeIsProcessedOnNextPump()
        {
            var h = Create(Event("event.react", "background", "repeatable",
                "{ 'type': 'state_changed', 'target': 'story.flags', 'property': 'locked' }",
                "{ 'action': 'set_state', 'target': 'story.flags', 'property': 'night', 'value': true }"));

            h.State.Set(StateAddress.Parse("story.flags", "locked"), StateValue.Boolean(true), MutationSource.System);
            Assert.That(h.Get("story.flags", "night").AsBoolean, Is.False, "nothing runs inside SetState");
            Assert.That(h.Runtime.HasPendingWork, Is.True);

            h.Runtime.Pump();

            Assert.That(h.Get("story.flags", "night").AsBoolean, Is.True);
            Assert.That(h.Runtime.HasPendingWork, Is.False);
        }
    }
}
