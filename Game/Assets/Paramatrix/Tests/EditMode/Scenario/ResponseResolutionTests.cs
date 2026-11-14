using System.Linq;
using NUnit.Framework;
using Paramatrix.Content;
using Paramatrix.Scenario;
using Paramatrix.State;
using static Paramatrix.Tests.Scenario.ScenarioHarness;

namespace Paramatrix.Tests.Scenario
{
    /// <summary>
    /// Р6, Р10: events first, then responses specific → contextual (prepared monologues are said once through) →
    /// stubs (own pool, else common; each round closed by "…") → fallback. Every response is a monologue:
    /// shown at once, no control taken, nothing waited for.
    /// </summary>
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

        private static string Prepared(string id, string interaction, string target, params string[] lines) =>
            "{ 'id': '" + id + "', 'tier': 'specific', 'interaction': '" + interaction + "', 'target': '" + target + "', 'lines': [ " +
            string.Join(", ", lines.Select(l => "'" + l + "'")) + " ] }";

        /// <summary>Clicks and returns what was said; asserts that saying took no control.</summary>
        private static string Said(ScenarioHarness h, InteractionType interaction, string target, string item = null)
        {
            h.Click(interaction, target, item);
            var log = h.TakeLog();
            Assert.That(log.Length, Is.EqualTo(1), string.Join(" | ", log));
            Assert.That(h.HoldsControl, Is.False, "a monologue takes no control");
            return log[0];
        }

        [Test]
        public void SpecificResponseWithCondition()
        {
            var h = Create(Response("response.locked", "specific", "exit", ", 'target': 'object.office_door', 'condition': " + FlagIs("locked", true), "Заперто."));

            Assert.That(Said(h, InteractionType.Exit, "object.office_door"), Is.EqualTo("say:character.paranoik:Туда не пройти."), "condition false → fallback");

            h.SetSystem("story.flags", "locked", StateValue.Boolean(true));
            Assert.That(Said(h, InteractionType.Exit, "object.office_door"), Is.EqualTo("say:character.paranoik:Заперто."));
            Assert.That(Said(h, InteractionType.Exit, "object.office_door"), Is.EqualTo("say:character.paranoik:Заперто."), "text is said every time");
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

            Assert.That(Said(h, InteractionType.Primary, "object.window"), Is.EqualTo("say:character.paranoik:Окно."));
            Assert.That(Said(h, InteractionType.Primary, "object.office_door"), Is.EqualTo("say:character.paranoik:Хм."));

            h.SetSystem("story.flags", "night", StateValue.Boolean(true));
            Assert.That(Said(h, InteractionType.Primary, "object.office_door"), Is.EqualTo("say:character.paranoik:Ночью всё закрыто."));
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
        public void StubRoundEndsWithEllipsis()
        {
            var h = Create(
                Response("response.hm_1", "generic", "primary", "", "Раз."),
                Response("response.hm_2", "generic", "primary", "", "Два."),
                Response("response.hm_3", "generic", "primary", "", "Три."));

            var lines = Enumerable.Range(0, 6).Select(_ => Said(h, InteractionType.Primary, "object.window")).ToArray();

            Assert.That(lines, Is.EqualTo(new[]
            {
                "say:character.paranoik:Раз.",
                "say:character.paranoik:Два.",
                "say:character.paranoik:Три.",
                "say:character.paranoik:" + ScenarioRuntime.EndOfStubs,
                "say:character.paranoik:Раз.",
                "say:character.paranoik:Два.",
            }));
        }

        [Test]
        public void SingleStubAlternatesWithEllipsis()
        {
            var h = Create(Response("response.stub_talk", "generic", "talk", "", "Мне нечего тебе сказать."));

            var lines = Enumerable.Range(0, 3).Select(_ => Said(h, InteractionType.Talk, "character.trinity")).ToArray();

            Assert.That(lines, Is.EqualTo(new[]
            {
                "say:character.trinity:Мне нечего тебе сказать.",
                "say:character.trinity:…",
                "say:character.trinity:Мне нечего тебе сказать.",
            }));
        }

        [Test]
        public void OwnStubsBeatTheCommonPool()
        {
            var h = Create(
                Response("response.common", "generic", "inspect", "", "Ничего."),
                Response("response.window_1", "generic", "inspect", ", 'target': 'object.window'", "Стекло."),
                Response("response.window_2", "generic", "inspect", ", 'target': 'object.window'", "Пыль."));

            Assert.That(Said(h, InteractionType.Inspect, "object.window"), Is.EqualTo("say:character.paranoik:Стекло."));
            Assert.That(Said(h, InteractionType.Inspect, "object.office_door"), Is.EqualTo("say:character.paranoik:Ничего."), "no own pool → common pool");
            Assert.That(Said(h, InteractionType.Inspect, "object.window"), Is.EqualTo("say:character.paranoik:Пыль."), "pools keep separate rounds");
            Assert.That(Said(h, InteractionType.Inspect, "object.window"), Is.EqualTo("say:character.paranoik:…"));
        }

        [Test]
        public void ConditionalPoolKeepsItsPlace()
        {
            var h = Create(
                Response("response.hm_1", "generic", "primary", ", 'condition': " + FlagIs("night", true), "Ночь."),
                Response("response.hm_2", "generic", "primary", "", "Хм."),
                Response("response.hm_3", "generic", "primary", "", "Нет."));

            Assert.That(Said(h, InteractionType.Primary, "object.window"), Is.EqualTo("say:character.paranoik:Хм."));
            h.SetSystem("story.flags", "night", StateValue.Boolean(true));
            Assert.That(Said(h, InteractionType.Primary, "object.window"), Is.EqualTo("say:character.paranoik:Нет."), "the round goes on after the last said stub");
            Assert.That(Said(h, InteractionType.Primary, "object.window"), Is.EqualTo("say:character.paranoik:…"));
            Assert.That(Said(h, InteractionType.Primary, "object.window"), Is.EqualTo("say:character.paranoik:Ночь."));
        }

        [Test]
        public void PreparedMonologueIsSaidOnceThenStubs()
        {
            var h = Create(
                Prepared("response.bar", "talk", "character.trinity", "Опять ты.", "О тебе спрашивали."),
                Response("response.trinity_stub", "generic", "talk", ", 'target': 'character.trinity'", "Наливать?"));

            var lines = Enumerable.Range(0, 5).Select(_ => Said(h, InteractionType.Talk, "character.trinity")).ToArray();

            Assert.That(lines, Is.EqualTo(new[]
            {
                "say:character.trinity:Опять ты.",
                "say:character.trinity:О тебе спрашивали.",
                "say:character.trinity:Наливать?",
                "say:character.trinity:…",
                "say:character.trinity:Наливать?",
            }));
            Assert.That(h.Runtime.SpokenLines(EntityId.Parse("response.bar")), Is.EqualTo(2));
            h.AssertNoDiagnostics();
        }

        [Test]
        public void SaidOutMonologueNoLongerMatches()
        {
            // While the monologue has lines left, a second true specific response would be ambiguous (Р0);
            // once it is said out, it steps aside.
            var h = Create(
                Prepared("response.first", "inspect", "object.window", "Раз."),
                "{ 'id': 'response.later', 'tier': 'specific', 'interaction': 'inspect', 'target': 'object.window', 'condition': " + FlagIs("night", true) + ", 'text': 'Потом.' }");

            Assert.That(Said(h, InteractionType.Inspect, "object.window"), Is.EqualTo("say:character.paranoik:Раз."));
            h.SetSystem("story.flags", "night", StateValue.Boolean(true));
            Assert.That(Said(h, InteractionType.Inspect, "object.window"), Is.EqualTo("say:character.paranoik:Потом."));
            Assert.That(Said(h, InteractionType.Inspect, "object.window"), Is.EqualTo("say:character.paranoik:Потом."));
            h.AssertNoDiagnostics();
        }

        [Test]
        public void RefusedLineIsAnErrorAndNotCounted()
        {
            var h = Create(Prepared("response.bar", "talk", "character.trinity", "Раз.", "Два."));
            h.RefuseSay = "not here";

            h.Click(InteractionType.Talk, "character.trinity");
            Assert.That(h.Errors, Is.EqualTo(new[] { DiagnosticCode.MonologueFailed }));
            Assert.That(h.Runtime.SpokenLines(EntityId.Parse("response.bar")), Is.EqualTo(0), "a line counts only when shown");

            h.RefuseSay = null;
            h.TakeLog();
            Assert.That(Said(h, InteractionType.Talk, "character.trinity"), Is.EqualTo("say:character.trinity:Раз."));
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
            Assert.That(Said(h, InteractionType.Talk, "character.trinity"), Is.EqualTo("say:character.trinity:Не сейчас."));
        }

        [Test]
        public void ExplicitSpeakerWins()
        {
            var h = Create(Response("response.window", "specific", "inspect", ", 'target': 'object.window', 'speaker': 'character.trinity'", "Там ничего."));
            Assert.That(Said(h, InteractionType.Inspect, "object.window"), Is.EqualTo("say:character.trinity:Там ничего."));
        }

        [Test]
        public void ItemFilteredResponse()
        {
            var h = Create(Response("response.key_door", "specific", "apply_item", ", 'target': 'object.office_door', 'item': 'item.key'", "Не тот ключ."));
            Assert.That(Said(h, InteractionType.ApplyItem, "object.office_door", "item.key"), Is.EqualTo("say:character.paranoik:Не тот ключ."));
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
        public void ResponseDoesNotBlockTheNextAction()
        {
            var h = Create(Event("event.other", "exclusive", "repeatable", OnClick("exit", "object.office_door"),
                "{ 'action': 'set_state', 'target': 'story.flags', 'property': 'night', 'value': true }"));

            h.Click(InteractionType.Primary, "object.window");
            Assert.That(h.HoldsControl, Is.False);

            h.Click(InteractionType.Exit, "object.office_door");
            Assert.That(h.Get("story.flags", "night").AsBoolean, Is.True, "the next action runs while the bubble is still shown");
            h.AssertNoDiagnostics();
        }

        [Test]
        public void WouldStartEventDecidesWhetherTheHeroStops()
        {
            var h = Create(
                Event("event.exit", "exclusive", "repeatable", OnClick("exit", "object.office_door"),
                    "{ 'action': 'set_state', 'target': 'story.flags', 'property': 'night', 'value': true }", ", 'launch_condition': " + FlagIs("locked", false)));

            Assert.That(h.Runtime.WouldStartEvent(Trigger.PlayerInteraction(InteractionType.Exit, EntityId.Parse("object.office_door"))), Is.True);
            Assert.That(h.Runtime.WouldStartEvent(Trigger.PlayerInteraction(InteractionType.Primary, EntityId.Parse("object.window"))), Is.False);

            h.SetSystem("story.flags", "locked", StateValue.Boolean(true));
            Assert.That(h.Runtime.WouldStartEvent(Trigger.PlayerInteraction(InteractionType.Exit, EntityId.Parse("object.office_door"))), Is.False,
                "a scene whose condition is false does not stop the hero: its response answers as a monologue");
        }

        [Test]
        public void SceneSaysWithoutWaiting()
        {
            var h = Create(Event("event.think", "exclusive", "repeatable", OnClick("primary", "object.window"),
                Say("Окно.") + ", { 'action': 'set_state', 'target': 'story.flags', 'property': 'night', 'value': true }"));

            h.Click(InteractionType.Primary, "object.window");

            Assert.That(h.TakeLog(), Is.EqualTo(new[] { "acquire", "say:character.paranoik:Окно.", "release:Completed" }));
            Assert.That(h.Get("story.flags", "night").AsBoolean, Is.True);
            Assert.That(h.PendingOperations, Is.Zero, "nothing waits for the line");
        }

        [Test]
        public void RefusedSceneLineFailsTheScene()
        {
            var h = Create(Event("event.think", "exclusive", "repeatable", OnClick("primary", "object.window"), Say("Окно.")));
            h.RefuseSay = "not here";

            h.Click(InteractionType.Primary, "object.window");

            Assert.That(h.Status("event.think"), Is.EqualTo(EventStatus.Failed));
            Assert.That(h.Errors, Is.EqualTo(new[] { DiagnosticCode.MonologueFailed }));
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
