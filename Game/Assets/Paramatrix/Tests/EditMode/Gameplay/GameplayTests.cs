using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Paramatrix.Content;
using Paramatrix.Gameplay;
using Paramatrix.Scenario;
using Paramatrix.State;
using Paramatrix.Tests.Content;

namespace Paramatrix.Tests.Gameplay
{
    /// <summary>
    /// End-to-end scenarios without Unity (plan 0.2, II.9 p.4): a small office → corridor game run through
    /// GameSession with a fake presentation layer.
    /// </summary>
    public sealed class GameplayTests
    {
        private static string Flag(string name, bool value = false) =>
            "'" + name + "': { 'type': 'boolean', 'default': " + (value ? "true" : "false") + ", 'persistence': 'persistent', 'mutation_policy': 'scenario_or_system' }";

        private static string FlagIs(string name, bool value) =>
            "{ 'target': 'story.flags', 'property': '" + name + "', 'operator': 'equals', 'value': " + (value ? "true" : "false") + " }";

        private static string Set(string name, bool value) =>
            "{ 'action': 'set_state', 'target': 'story.flags', 'property': '" + name + "', 'value': " + (value ? "true" : "false") + " }";

        private static string Locked(bool value) =>
            "{ 'target': 'object.office_door', 'property': 'locked', 'operator': 'equals', 'value': " + (value ? "true" : "false") + " }";

        private static readonly string[] Game =
        {
            "{ 'id': 'location.corridor', 'scene': 'Location_Corridor' }",
            "{ 'id': 'object.office_door', 'primary_interaction': 'exit', 'states': { 'locked': { 'type': 'boolean', 'default': true, 'persistence': 'persistent', 'mutation_policy': 'scenario_or_system' } } }",
            "{ 'id': 'object.corridor_door', 'primary_interaction': 'exit' }",
            "{ 'id': 'object.window', 'primary_interaction': 'primary' }",
            "{ 'id': 'item.key', 'name': 'Ключ', 'description': 'Латунный.', 'initial_holder': 'location.office' }",
            "{ 'id': 'item.note', 'name': 'Записка', 'description': 'Мятая.', 'initial_holder': 'inventory' }",
            "{ 'id': 'character.trinity', 'name': 'Тринити', 'initial_location': 'location.office', 'primary_interaction': 'talk', 'emotions': ['default'] }",
            "{ 'id': 'story.flags', 'states': { " + string.Join(", ", Flag("intro_done"), Flag("radio"), Flag("met_trinity"), Flag("ready"), Flag("combined"), Flag("broken_road")) + " } }",

            "{ 'id': 'dialogue.intro', 'mode': 'normal', 'lines': [ { 'speaker': 'character.paranoik', 'text': 'Где я?' }, { 'speaker': 'character.paranoik', 'text': 'Офис.' } ] }",
            "{ 'id': 'dialogue.trinity', 'mode': 'normal', 'lines': [ { 'speaker': 'character.trinity', 'text': 'Он ждёт.' }, { 'speaker': 'character.paranoik', 'text': 'Кто?' } ] }",

            "{ 'id': 'event.intro', 'execution': 'exclusive', 'repeat': 'once', 'triggers': [ { 'type': 'location_entered', 'location': 'location.office' } ]," +
            "  'launch_condition': " + FlagIs("intro_done", false) + ", 'actions': [ { 'action': 'dialogue', 'dialogue': 'dialogue.intro' }, " + Set("intro_done", true) + " ] }",

            "{ 'id': 'event.radio', 'execution': 'background', 'repeat': 'once_per_location_visit', 'triggers': [ { 'type': 'location_entered', 'location': 'location.office' } ]," +
            "  'actions': [ " + Set("radio", true) + " ] }",

            "{ 'id': 'event.take_key', 'execution': 'exclusive', 'repeat': 'once', 'triggers': [ { 'type': 'interaction', 'interaction': 'take', 'target': 'item.key' } ]," +
            "  'actions': [ { 'action': 'animation', 'target': 'character.paranoik', 'clip': 'bend_down' }, { 'action': 'inventory', 'item': 'item.key', 'to': 'inventory' } ] }",

            "{ 'id': 'event.unlock', 'execution': 'exclusive', 'repeat': 'repeatable'," +
            "  'triggers': [ { 'type': 'interaction', 'interaction': 'apply_item', 'target': 'object.office_door', 'item': 'item.key' } ]," +
            "  'launch_condition': " + Locked(true) + "," +
            "  'actions': [ { 'action': 'set_state', 'target': 'object.office_door', 'property': 'locked', 'value': false }, { 'action': 'clear_selection' } ] }",

            "{ 'id': 'event.exit', 'execution': 'exclusive', 'repeat': 'repeatable', 'triggers': [ { 'type': 'interaction', 'interaction': 'exit', 'target': 'object.office_door' } ]," +
            "  'launch_condition': " + Locked(false) + "," +
            "  'actions': [ { 'action': 'animation', 'target': 'character.paranoik', 'clip': 'open_door' }, { 'action': 'transition', 'location': 'location.corridor', 'entry': 'from_office' } ] }",

            "{ 'id': 'event.back', 'execution': 'exclusive', 'repeat': 'repeatable', 'triggers': [ { 'type': 'interaction', 'interaction': 'exit', 'target': 'object.corridor_door' } ]," +
            "  'actions': [ { 'action': 'transition', 'location': 'location.office', 'entry': 'from_corridor' } ] }",

            "{ 'id': 'event.talk_trinity', 'execution': 'exclusive', 'repeat': 'once', 'triggers': [ { 'type': 'interaction', 'interaction': 'talk', 'target': 'character.trinity' } ]," +
            "  'actions': [ { 'action': 'dialogue', 'dialogue': 'dialogue.trinity' }, " + Set("met_trinity", true) + " ] }",

            "{ 'id': 'event.combine', 'execution': 'background', 'repeat': 'repeatable'," +
            "  'triggers': [ { 'type': 'interaction', 'interaction': 'combine_items', 'target': 'item.key', 'item': 'item.note' } ], 'actions': [ " + Set("combined", true) + " ] }",

            "{ 'id': 'event.ready', 'execution': 'background', 'repeat': 'repeatable'," +
            "  'triggers': [ { 'type': 'conditions_met', 'condition': { 'all': [ " + FlagIs("met_trinity", true) + ", " + Locked(false) + " ] } } ]," +
            "  'actions': [ " + Set("ready", true) + " ] }",

            "{ 'id': 'event.ask', 'execution': 'exclusive', 'repeat': 'repeatable', 'triggers': [ { 'type': 'interaction', 'interaction': 'inspect', 'target': 'object.window' } ]," +
            "  'launch_condition': { 'target': 'character.trinity', 'property': 'location', 'operator': 'equals', 'value': 'location.corridor' }," +
            "  'actions': [ { 'action': 'say', 'speaker': 'character.trinity', 'text': 'Смотри.' } ] }",

            // Monologues (Р10): Trinity's prepared lines on inspect, her own stubs; the window has none and uses the common pool.
            "{ 'id': 'response.trinity_look', 'tier': 'specific', 'interaction': 'inspect', 'target': 'character.trinity', 'lines': [ 'Чёрная кожа.', 'Тёмные очки.' ] }",
            "{ 'id': 'response.trinity_stub_1', 'tier': 'generic', 'interaction': 'inspect', 'target': 'character.trinity', 'text': 'Она всё ещё здесь.' }",
            "{ 'id': 'response.window_think', 'tier': 'generic', 'interaction': 'primary', 'text': 'Хм.' }",

            "{ 'id': 'response.door_locked', 'tier': 'specific', 'interaction': 'exit', 'target': 'object.office_door', 'condition': " + Locked(true) + ", 'text': 'Заперто.' }",
        };

        private static GameTestBed Start()
        {
            var bed = GameTestBed.Create(Game);
            bed.NewGame();
            return bed;
        }

        /// <summary>New game, intro finished, control with the player.</summary>
        private static GameTestBed AfterIntro()
        {
            var bed = Start();
            bed.FinishDialogues();
            bed.TakeLog();
            return bed;
        }

        private static void TakeKeyAndUnlock(GameTestBed bed)
        {
            bed.Click("item.key");
            bed.ClickItem("item.key");
            bed.Click("object.office_door");
            bed.TakeLog();
        }

        // ================= start =================

        [Test]
        public void ContentErrorsBlockTheSession()
        {
            var session = GameSession.Create(
                new[] { TestContent.Base(), new ContentSource("bad.json", TestContent.File("{ 'id': 'story.x', 'oops': 1 }")) },
                new PresentationPorts(new NullPorts(), new NullPorts(), new NullPorts(), new NullPorts(), new NullPorts(), new NullPorts()),
                new DiagnosticBag(),
                out var diagnostics);

            Assert.That(session, Is.Null);
            Assert.That(diagnostics.Select(d => d.Code), Does.Contain(DiagnosticCode.UnknownField));
        }

        [Test]
        public void BootHasNoPlayerControl()
        {
            var bed = GameTestBed.Create(Game);
            Assert.That(bed.Session.Flow.Owner, Is.EqualTo(ControlOwner.System));
            Assert.That(bed.Session.Flow.Permissions, Is.EqualTo(PlayerPermissions.None));
            Assert.That(bed.Session.IsBooting, Is.True);
        }

        [Test]
        public void NewGameEntersStartLocationAndPlaysIntro()
        {
            var bed = Start();

            Assert.That(bed.TakeLog(), Is.EqualTo(new[] { "scene:Location_Office:default", "say:paranoik:Где я?" }));
            Assert.That(bed.Session.Scenario.CurrentVisit, Is.EqualTo(1));
            Assert.That(bed.Flag("radio"), Is.True, "background scene on the first entry");
            Assert.That(bed.Session.Flow.Owner, Is.EqualTo(ControlOwner.Scenario));
            Assert.That(bed.Session.Flow.Permissions, Is.EqualTo(PlayerPermissions.AdvanceDialogue));

            bed.Advance();
            Assert.That(bed.TakeLog(), Is.EqualTo(new[] { "say:paranoik:Офис." }));
            bed.Advance();

            Assert.That(bed.TakeLog(), Is.EqualTo(new[] { "hide" }));
            Assert.That(bed.Flag("intro_done"), Is.True);
            Assert.That(bed.Session.Flow.Owner, Is.EqualTo(ControlOwner.Player));
            Assert.That(bed.Session.Flow.Permissions & PlayerPermissions.Interact, Is.EqualTo(PlayerPermissions.Interact));
            bed.AssertNoErrors();
        }

        [Test]
        public void ClicksDuringIntroAreIgnored()
        {
            var bed = Start();
            bed.TakeLog();

            bed.Click("object.window");

            Assert.That(bed.TakeLog(), Is.Empty);
            bed.AssertNoErrors();
        }

        // ================= interactions =================

        [Test]
        public void LockedDoorAnswersWithSpecificResponse()
        {
            var bed = AfterIntro();

            bed.Click("object.office_door");

            // The exit scene's condition is false: the hero is not stopped, the specific response answers as a monologue (Р10).
            Assert.That(bed.TakeLog(), Is.EqualTo(new[] { "bubble:paranoik:Заперто." }));
            Assert.That(bed.Session.Flow.Owner, Is.EqualTo(ControlOwner.Player));
        }

        [Test]
        public void InspectWorksFromAnyDistance()
        {
            var bed = AfterIntro();
            bed.OutOfReach.Add("object.window");

            bed.Click("object.window", PointerButton.Secondary);

            Assert.That(bed.TakeLog(), Is.EqualTo(new[] { "bubble:paranoik:Ничего особенного." }));
        }

        [Test]
        public void TakeKeyMovesItIntoInventory()
        {
            var bed = AfterIntro();

            bed.Click("item.key");

            Assert.That(bed.TakeLog(), Is.EqualTo(new[] { "face:item.key", "anim:paranoik:bend_down" }));
            Assert.That(bed.Get("item.key", "holder").AsEnum, Is.EqualTo("inventory"));
            Assert.That(bed.Session.Inventory.Items.Select(i => i.Id.Value), Is.EqualTo(new[] { "item.key", "item.note" }), "declaration order (T18)");
            Assert.That(bed.Session.Flow.Owner, Is.EqualTo(ControlOwner.Player));
        }

        [Test]
        public void TakingAgainFallsBack()
        {
            var bed = AfterIntro();
            bed.Click("item.key");
            bed.TakeLog();

            bed.Click("item.key");

            Assert.That(bed.TakeLog(), Is.EqualTo(new[] { "bubble:paranoik:Не могу это взять." }), "the take scene is used up: a monologue, the hero is not stopped");
        }

        [Test]
        public void ApplyingKeyUnlocksDoorAndClearsSelection()
        {
            var bed = AfterIntro();
            bed.Click("item.key");
            bed.ClickItem("item.key");
            Assert.That(bed.Session.Inventory.Selected, Is.EqualTo(EntityId.Parse("item.key")));

            bed.Click("object.office_door");

            Assert.That(bed.Get("object.office_door", "locked").AsBoolean, Is.False);
            Assert.That(bed.Session.Inventory.Selected, Is.Null);
        }

        [Test]
        public void FailedApplicationKeepsSelection()
        {
            var bed = AfterIntro();
            bed.Click("item.key");
            bed.ClickItem("item.key");
            bed.TakeLog();

            bed.Click("object.window");

            Assert.That(bed.TakeLog(), Is.EqualTo(new[] { "bubble:paranoik:Не подходит." }));
            Assert.That(bed.Session.Inventory.Selected, Is.EqualTo(EntityId.Parse("item.key")), "0.3 §4.5");
        }

        [Test]
        public void SelectionToggleAndRightClickCancel()
        {
            var bed = AfterIntro();

            bed.ClickItem("item.note");
            Assert.That(bed.Session.Inventory.Selected, Is.EqualTo(EntityId.Parse("item.note")));
            bed.ClickItem("item.note");
            Assert.That(bed.Session.Inventory.Selected, Is.Null);

            bed.ClickItem("item.note");
            bed.Session.Interaction.ClickEmpty(PointerButton.Secondary);
            Assert.That(bed.Session.Inventory.Selected, Is.Null);
        }

        [Test]
        public void CombineItemsInInventory()
        {
            var bed = AfterIntro();
            bed.Click("item.key");
            bed.ClickItem("item.key");

            bed.ClickItem("item.note");

            Assert.That(bed.Flag("combined"), Is.True);
        }

        [Test]
        public void OutOfReachALeftClickDoesNothing()
        {
            var bed = AfterIntro();
            bed.OutOfReach.Add("item.key");
            bed.OutOfReach.Add("object.office_door");
            bed.OutOfReach.Add("character.trinity");

            bed.Click("item.key");
            bed.Click("object.office_door");
            bed.Click("character.trinity");

            Assert.That(bed.TakeLog(), Is.Empty, "no scene, no monologue, no walk: the player walks closer himself (Р11)");
            Assert.That(bed.Get("item.key", "holder").AsEnum, Is.Not.EqualTo("inventory"));
            Assert.That(bed.Session.Flow.Owner, Is.EqualTo(ControlOwner.Player));
            bed.AssertNoErrors();
        }

        [Test]
        public void WithinReachTheSameClickWorks()
        {
            var bed = AfterIntro();
            bed.OutOfReach.Add("item.key");
            bed.Click("item.key");

            bed.OutOfReach.Remove("item.key"); // the player walked up to it
            bed.Click("item.key");

            Assert.That(bed.TakeLog(), Is.EqualTo(new[] { "face:item.key", "anim:paranoik:bend_down" }));
            Assert.That(bed.Session.Inventory.IsCarried(EntityId.Parse("item.key")), Is.True);
        }

        // ================= locations =================

        [Test]
        public void ExitToCorridorStartsNewVisit()
        {
            var bed = AfterIntro();
            TakeKeyAndUnlock(bed);

            bed.Click("object.office_door");

            Assert.That(bed.TakeLog(), Is.EqualTo(new[] { "face:object.office_door", "anim:paranoik:open_door", "scene:Location_Corridor:from_office" }));
            Assert.That(bed.Location, Is.EqualTo("location.corridor"));
            Assert.That(bed.Session.Scenario.CurrentVisit, Is.EqualTo(2));
            Assert.That(bed.Session.Flow.Owner, Is.EqualTo(ControlOwner.Player));
            Assert.That(bed.Session.Flow.InTransition, Is.False);
        }

        [Test]
        public void ReturningToOfficeReplaysPerVisitSceneOnly()
        {
            var bed = AfterIntro();
            TakeKeyAndUnlock(bed);
            bed.Click("object.office_door");
            bed.Session.State.Set(StateAddress.Parse("story.flags", "radio"), StateValue.Boolean(false), MutationSource.System);
            bed.Session.Tick();

            bed.Click("object.corridor_door");

            Assert.That(bed.Location, Is.EqualTo("location.office"));
            Assert.That(bed.Session.Scenario.CurrentVisit, Is.EqualTo(3));
            Assert.That(bed.Flag("radio"), Is.True, "once_per_location_visit runs again on a new Visit");
            Assert.That(bed.Session.Dialogue.IsOpen, Is.False, "the once-intro does not replay");
        }

        [Test]
        public void SceneLoadFailureFailsTheEventAndBlocksIt()
        {
            var bed = AfterIntro();
            TakeKeyAndUnlock(bed);
            bed.FailSceneLoad = true;

            bed.Click("object.office_door");

            Assert.That(bed.Session.Scenario.GetStatus(EntityId.Parse("event.exit")), Is.EqualTo(EventStatus.Failed));
            Assert.That(bed.Location, Is.EqualTo("location.office"), "State changes only after a successful load");
            Assert.That(bed.Session.Flow.Owner, Is.EqualTo(ControlOwner.Player));
            Assert.That(bed.Errors, Is.EqualTo(new[] { DiagnosticCode.SceneLoadFailed, DiagnosticCode.OperationFailed }));

            bed.FailSceneLoad = false;
            bed.TakeLog();
            bed.Click("object.office_door");
            Assert.That(bed.TakeLog(), Is.EqualTo(new[] { "face:object.office_door" }), "blocked until a load (Р3), no fallback");
            Assert.That(bed.Errors.Last(), Is.EqualTo(DiagnosticCode.EventBlockedByFailure));
        }

        // ================= dialogue =================

        [Test]
        public void ConversationPausesAndShowsEmotions()
        {
            var bed = AfterIntro();

            bed.Click("character.trinity");

            Assert.That(bed.Session.Flow.Presentation, Is.EqualTo(DialoguePresentation.Normal));
            Assert.That(bed.CurrentLine.Value.Speaker, Is.EqualTo(EntityId.Parse("character.trinity")));
            Assert.That(bed.CurrentLine.Value.Emotion, Is.EqualTo("default"));
            bed.FinishDialogues();
            Assert.That(bed.Flag("met_trinity"), Is.True);
            Assert.That(bed.Session.Flow.Presentation, Is.EqualTo(DialoguePresentation.None));
        }

        [Test]
        public void SceneMonologueSpeakerMustBePresent()
        {
            var bed = AfterIntro();
            bed.Session.State.Set(StateAddress.Parse("character.trinity", "location"), StateValue.Enum("location.corridor"), MutationSource.System);
            bed.Session.Tick();

            bed.TakeLog();

            bed.Click("object.window", PointerButton.Secondary); // event.ask: Trinity says a line, but she is not here

            Assert.That(bed.Session.Scenario.GetStatus(EntityId.Parse("event.ask")), Is.EqualTo(EventStatus.Failed));
            Assert.That(bed.Errors, Is.EqualTo(new[] { DiagnosticCode.MonologueFailed }));
            Assert.That(bed.Bubbles, Is.Empty, "nothing was shown");
            Assert.That(bed.Session.Flow.Owner, Is.EqualTo(ControlOwner.Player));
        }

        [Test]
        public void ConversationSpeakerMustBePresent()
        {
            var bed = AfterIntro();
            bed.Session.State.Set(StateAddress.Parse("character.trinity", "location"), StateValue.Enum("location.corridor"), MutationSource.System);
            bed.Session.Tick();

            bed.Click("character.trinity");

            Assert.That(bed.Errors, Is.EqualTo(new[] { DiagnosticCode.OperationFailed }));
            Assert.That(bed.Session.Flow.Presentation, Is.EqualTo(DialoguePresentation.None));
            Assert.That(bed.CurrentLine, Is.Null);
        }

        // ================= monologues (Р10) =================

        [Test]
        public void MonologueTakesNoControlAndDoesNotStopTheHero()
        {
            var bed = AfterIntro();

            bed.Click("object.window"); // primary: the common stub pool

            Assert.That(bed.TakeLog(), Is.EqualTo(new[] { "bubble:paranoik:Хм." }), "no facing: the hero keeps walking");
            Assert.That(bed.Session.Flow.Owner, Is.EqualTo(ControlOwner.Player));
            Assert.That(bed.Session.Flow.Permissions & PlayerPermissions.Move, Is.EqualTo(PlayerPermissions.Move));
            Assert.That(bed.Session.Save.CanSave, Is.True, "a monologue does not block saving");
            bed.AssertNoErrors();
        }

        [Test]
        public void SceneStopsTheHeroAndTurnsHimToTheTarget()
        {
            var bed = AfterIntro();
            bed.Click("character.trinity");
            Assert.That(bed.TakeLog().First(), Is.EqualTo("face:character.trinity"));
        }

        [Test]
        public void RightClickOnEmptySpaceMovesTheThoughtOnThenClosesIt()
        {
            var bed = AfterIntro();
            bed.Click("character.trinity", PointerButton.Secondary);
            Assert.That(bed.Bubbles["paranoik"], Is.EqualTo("Чёрная кожа."));

            bed.ClickEmpty();
            Assert.That(bed.Bubbles["paranoik"], Is.EqualTo("Тёмные очки."), "the next prepared line");

            bed.ClickEmpty();
            Assert.That(bed.Bubbles.ContainsKey("paranoik"), Is.False, "nothing prepared is left: the thought closes, no stub");

            bed.ClickEmpty();
            Assert.That(bed.Bubbles, Is.Empty, "no thought shown: nothing happens");
            bed.Click("character.trinity", PointerButton.Secondary);
            Assert.That(bed.Bubbles["paranoik"], Is.EqualTo("Она всё ещё здесь."), "said lines count as said");
            bed.AssertNoErrors();
        }

        [Test]
        public void RightClickOnEmptySpaceClosesAThoughtThatIsNotPrepared()
        {
            var bed = AfterIntro();
            bed.Click("object.window"); // a stub
            bed.TakeLog();

            bed.ClickEmpty();

            Assert.That(bed.TakeLog(), Is.EqualTo(new[] { "bubble:hide:paranoik" }));
        }

        [Test]
        public void AStubAfterAPreparedLineIsNotMovedOn()
        {
            var bed = AfterIntro();
            bed.Click("character.trinity", PointerButton.Secondary);
            bed.Click("object.window"); // the hero says a stub: the bubble no longer shows the prepared thought
            bed.TakeLog();

            bed.ClickEmpty();

            Assert.That(bed.TakeLog(), Is.EqualTo(new[] { "bubble:hide:paranoik" }), "a stub is not moved on");
        }

        [Test]
        public void PreparedMonologueThenOwnStubsWithEndMark()
        {
            var bed = AfterIntro();
            var said = new List<string>();
            for (var i = 0; i < 6; i++)
            {
                bed.Click("character.trinity", PointerButton.Secondary);
                said.Add(bed.Bubbles["paranoik"]);
            }

            Assert.That(said, Is.EqualTo(new[] { "Чёрная кожа.", "Тёмные очки.", "Она всё ещё здесь.", "…", "Она всё ещё здесь.", "…" }));
            bed.AssertNoErrors();
        }

        [Test]
        public void PreparedMonologueProgressIsSaved()
        {
            var bed = AfterIntro();
            bed.Click("character.trinity", PointerButton.Secondary);
            Assert.That(bed.SaveGame().Succeeded, Is.True);

            var loaded = GameTestBed.LoadIntoNewSession(Game, bed.Saves, "slot1", out var result);
            Assert.That(result.Succeeded, Is.True, TestContent.Describe(result.Diagnostics));
            loaded.Click("character.trinity", PointerButton.Secondary);
            Assert.That(loaded.Bubbles["paranoik"], Is.EqualTo("Тёмные очки."), "the monologue goes on, it does not start over");
        }

        [Test]
        public void ConversationAndTransitionClearMonologues()
        {
            var bed = AfterIntro();
            bed.Click("object.window");
            Assert.That(bed.Bubbles, Is.Not.Empty);

            bed.Click("character.trinity");
            Assert.That(bed.Bubbles, Is.Empty, "a conversation takes the screen");
            bed.FinishDialogues();

            bed.Click("object.window");
            TakeKeyAndUnlock(bed);
            bed.Click("object.window");
            Assert.That(bed.Bubbles, Is.Not.Empty);
            bed.Click("object.office_door");
            Assert.That(bed.Location, Is.EqualTo("location.corridor"));
            Assert.That(bed.Bubbles, Is.Empty, "leaving the location takes its monologues");
        }

        [Test]
        public void ConditionMonitorAcrossSystems()
        {
            var bed = AfterIntro();
            bed.Click("character.trinity");
            bed.FinishDialogues();
            Assert.That(bed.Flag("ready"), Is.False);

            TakeKeyAndUnlock(bed);

            Assert.That(bed.Flag("ready"), Is.True);
        }

        // ================= save / load =================

        [Test]
        public void SaveIsRefusedDuringDialogue()
        {
            var bed = Start();

            var result = bed.SaveGame();

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Diagnostics.Single().Code, Is.EqualTo(DiagnosticCode.SaveFailed));
            Assert.That(bed.Saves, Is.Empty);
        }

        [Test]
        public void SaveAndLoadRestoreTheWorldWithoutReplay()
        {
            var bed = AfterIntro();
            bed.Click("item.key");
            bed.Click("character.trinity");
            bed.FinishDialogues();
            Assert.That(bed.SaveGame().Succeeded, Is.True);

            var loaded = GameTestBed.LoadIntoNewSession(Game, bed.Saves, "slot1", out var result);

            Assert.That(result.Succeeded, Is.True, TestContent.Describe(result.Diagnostics));
            Assert.That(loaded.TakeLog(), Is.EqualTo(new[] { "scene:Location_Office:default" }), "no intro, no radio, no location_entered (Р2, Р9)");
            Assert.That(loaded.Get("item.key", "holder").AsEnum, Is.EqualTo("inventory"));
            Assert.That(loaded.Flag("met_trinity"), Is.True);
            Assert.That(loaded.Session.Scenario.CurrentVisit, Is.EqualTo(1));
            Assert.That(loaded.Session.Flow.Owner, Is.EqualTo(ControlOwner.Player));
            Assert.That(loaded.Session.IsBooting, Is.False);
            loaded.AssertNoErrors();

            // History survived: talking again falls back to "no reaction" (talk_trinity is once).
            loaded.Click("character.trinity");
            Assert.That(loaded.Session.Dialogue.IsOpen, Is.False);
        }

        [Test]
        public void LoadDoesNotFireMonitorFalsely()
        {
            var bed = AfterIntro();
            bed.Click("character.trinity");
            bed.FinishDialogues();
            TakeKeyAndUnlock(bed);
            Assert.That(bed.Flag("ready"), Is.True);
            bed.Session.State.Set(StateAddress.Parse("story.flags", "ready"), StateValue.Boolean(false), MutationSource.System);
            bed.Session.Tick();
            Assert.That(bed.SaveGame().Succeeded, Is.True);

            var loaded = GameTestBed.LoadIntoNewSession(Game, bed.Saves, "slot1", out var result);
            loaded.Session.Tick();

            Assert.That(result.Succeeded, Is.True);
            Assert.That(loaded.Flag("ready"), Is.False, "condition already true after load: no edge");
        }

        [Test]
        public void LoadMidGameReplacesWorld()
        {
            var bed = AfterIntro();
            Assert.That(bed.SaveGame().Succeeded, Is.True);
            bed.Click("item.key");
            Assert.That(bed.Get("item.key", "holder").AsEnum, Is.EqualTo("inventory"));
            bed.TakeLog();

            SaveLoadResult result = null;
            bed.Session.LoadGame("slot1", r => result = r);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(bed.Get("item.key", "holder").AsEnum, Is.EqualTo("location.office"));
            Assert.That(bed.TakeLog(), Is.EqualTo(new[] { "scene:Location_Office:default" }));
            Assert.That(bed.Session.Flow.Owner, Is.EqualTo(ControlOwner.Player));
        }

        [Test]
        public void LoadAfterFailureUnblocksTheScene()
        {
            var bed = AfterIntro();
            TakeKeyAndUnlock(bed);
            bed.FailSceneLoad = true;
            bed.Click("object.office_door");
            bed.FailSceneLoad = false;
            Assert.That(bed.SaveGame().Succeeded, Is.True, "saving is allowed after a failure");

            var loaded = GameTestBed.LoadIntoNewSession(Game, bed.Saves, "slot1", out _);
            loaded.TakeLog();
            loaded.Click("object.office_door");

            Assert.That(loaded.Location, Is.EqualTo("location.corridor"));
        }

        [Test]
        public void LoadIsRejectedWhenContentChanged()
        {
            var bed = AfterIntro();
            Assert.That(bed.SaveGame().Succeeded, Is.True);

            var changed = Game.Select(d => d.Replace(", " + Flag("broken_road"), "")).ToArray();
            var loaded = GameTestBed.LoadIntoNewSession(changed, bed.Saves, "slot1", out var result);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain(DiagnosticCode.SnapshotUnknownProperty));
            Assert.That(loaded.Session.IsBooting, Is.True, "nothing was applied");
            Assert.That(loaded.Get("story.flags", "intro_done").AsBoolean, Is.False);
            Assert.That(loaded.TakeLog(), Is.Empty, "no scene was loaded");
        }

        [Test]
        public void LoadRejectsMissingSlotAndBrokenFile()
        {
            var bed = GameTestBed.Create(Game, new Dictionary<string, string> { ["broken"] = "{ not json" });

            SaveLoadResult missing = null, broken = null;
            bed.Session.LoadGame("absent", r => missing = r);
            bed.Session.LoadGame("broken", r => broken = r);

            Assert.That(missing.Diagnostics.Single().Code, Is.EqualTo(DiagnosticCode.SaveFailed));
            Assert.That(broken.Diagnostics.Single().Code, Is.EqualTo(DiagnosticCode.SaveFileInvalid));
            Assert.That(bed.Session.IsBooting, Is.True);
        }

        // ================= recovery =================

        [Test]
        public void RecoveryDuringDialogueReturnsControl()
        {
            var bed = AfterIntro();
            bed.Click("character.trinity");
            Assert.That(bed.Session.Dialogue.IsOpen, Is.True);

            bed.Session.Flow.EnterRecovery("dialogue view stopped responding");

            Assert.That(bed.Session.Dialogue.IsOpen, Is.False);
            Assert.That(bed.CurrentLine, Is.Null);
            Assert.That(bed.Session.Flow.Owner, Is.EqualTo(ControlOwner.Player));
            Assert.That(bed.Session.Flow.Presentation, Is.EqualTo(DialoguePresentation.None));
            Assert.That(bed.Session.Scenario.GetStatus(EntityId.Parse("event.talk_trinity")), Is.EqualTo(EventStatus.Failed));
            Assert.That(bed.Flag("met_trinity"), Is.False);
            Assert.That(bed.Errors, Is.EqualTo(new[] { DiagnosticCode.RecoveryEntered, DiagnosticCode.ChainAborted }));
            Assert.That(bed.Session.Save.CanSave, Is.True);
        }

        // ================= Game Flow =================

        [Test]
        public void PermissionsFollowTheThreeDimensions()
        {
            var flow = new GameFlow(new DiagnosticBag());
            Assert.That(flow.Permissions, Is.EqualTo(PlayerPermissions.None), "boot");
            flow.EndSystemOperation(flow.BootToken);

            Assert.That(flow.Permissions, Is.EqualTo(PlayerPermissions.Move | PlayerPermissions.Interact | PlayerPermissions.Inventory | PlayerPermissions.SaveMenu));

            Assert.That(flow.TryAcquireScenarioControl(out var token), Is.True);
            Assert.That(flow.Permissions, Is.EqualTo(PlayerPermissions.None));
            flow.SetPresentation(DialoguePresentation.Normal);
            Assert.That(flow.Permissions, Is.EqualTo(PlayerPermissions.AdvanceDialogue));
            flow.SetPresentation(DialoguePresentation.None);
            Assert.That(flow.TryAcquireScenarioControl(out _), Is.False, "one owner at a time");
            flow.ReleaseScenarioControl(token, ReleaseReason.Completed);

            flow.SetTransition(true);
            Assert.That(flow.Permissions, Is.EqualTo(PlayerPermissions.None));
            Assert.That(flow.TryAcquireScenarioControl(out _), Is.False, "no scene starts during a transition");
            flow.SetTransition(false);

            Assert.Throws<System.InvalidOperationException>(() => flow.ReleaseScenarioControl(token, ReleaseReason.Completed));
        }

        [Test]
        public void SystemOperationsExcludeScenes()
        {
            var flow = new GameFlow(new DiagnosticBag());
            flow.EndSystemOperation(flow.BootToken);

            var load = flow.BeginSystemOperation(SystemOperation.LoadGame);
            Assert.That(flow.TryAcquireScenarioControl(out _), Is.False);
            Assert.Throws<System.InvalidOperationException>(() => flow.BeginSystemOperation(SystemOperation.NewGame));
            flow.EndSystemOperation(load);

            flow.TryAcquireScenarioControl(out _);
            Assert.Throws<System.InvalidOperationException>(() => flow.BeginSystemOperation(SystemOperation.LoadGame), "recover first");
        }

        private sealed class NullPorts : ISceneLoader, IHeroReach, IDialogueView, IMovementPort, IAnimationPort, ISaveStorage
        {
            public void LoadLocationScene(string scene, string entry, System.Action<bool, string> done) => done(true, null);

            public bool CanReach(EntityId target) => true;

            public void FaceForInteraction(EntityId target)
            {
            }

            public bool IsMonologueShown(EntityId speaker) => false;

            public void HideMonologue(EntityId speaker)
            {
            }

            public void Show(DialogueLineView line)
            {
            }

            public void ShowChoices(DialogueChoiceView choices)
            {
            }

            public void Hide()
            {
            }

            public void Say(EntityId speaker, string text)
            {
            }

            public void ClearMonologues()
            {
            }

            public OperationStart MoveTo(OperationHandle handle, EntityId character, EntityId target) => OperationStart.CompletedImmediately;

            public OperationStart Play(OperationHandle handle, EntityId target, string clip) => OperationStart.CompletedImmediately;

            public bool TryWrite(string slot, string json, out string error)
            {
                error = null;
                return true;
            }

            public bool TryRead(string slot, out string json, out string error)
            {
                json = null;
                error = "none";
                return false;
            }
        }
    }
}
