using System.Linq;
using NUnit.Framework;
using Paramatrix.Content;
using Paramatrix.Gameplay;

namespace Paramatrix.Tests.Gameplay
{
    /// <summary>The hero's choices in a conversation and the novel it may lead into (Р12).</summary>
    public sealed class DialogueChoiceTests
    {
        private static string Line(string speaker, string text, string emotion = null) =>
            "{ 'speaker': 'character." + speaker + "', 'text': '" + text + "'" + (emotion == null ? string.Empty : ", 'emotion': '" + emotion + "'") + " }";

        private static readonly string[] Bar =
        {
            "{ 'id': 'character.girl', 'name': 'Девушка', 'initial_location': 'location.office', 'primary_interaction': 'talk', 'emotions': ['default', 'smirk'] }",
            "{ 'id': 'story.flags', 'states': { 'talked': { 'type': 'boolean', 'default': false, 'persistence': 'persistent', 'mutation_policy': 'scenario_or_system' } } }",

            "{ 'id': 'dialogue.flirt', 'mode': 'normal', 'lines': [ " + Line("girl", "Привет.") + ", " +
            "  { 'choice': [ { 'label': 'Поздороваться', 'lines': [ " + Line("paranoik", "Привет и тебе.") + " ] }, { 'label': 'Промолчать' } ] }, " +
            Line("girl", "Хочешь узнать друг друга лучше?") + ", " +
            "  { 'choice': [ { 'label': 'Да', 'lines': [ " + Line("paranoik", "Да, я давно хотел этого.") + " ], 'next': 'dialogue.flirt_novel' }, " +
            "                { 'label': 'Мне пора', 'lines': [ " + Line("paranoik", "В другой раз.") + " ], 'end': true } ] } ] }",

            "{ 'id': 'dialogue.flirt_novel', 'mode': 'novel', 'partner': 'character.girl', 'lines': [ " + Line("girl", "Вот так лучше.", "smirk") + ", " +
            "  { 'choice': [ { 'label': 'Комплимент', 'lines': [ " + Line("paranoik", "Ты красивая.") + " ] }, { 'label': 'Молчать' } ] }, " +
            Line("girl", "Мне пора.") + " ] }",

            "{ 'id': 'event.talk_girl', 'execution': 'exclusive', 'repeat': 'repeatable', 'triggers': [ { 'type': 'interaction', 'interaction': 'talk', 'target': 'character.girl' } ]," +
            "  'actions': [ { 'action': 'dialogue', 'dialogue': 'dialogue.flirt' }, { 'action': 'set_state', 'target': 'story.flags', 'property': 'talked', 'value': true } ] }",
        };

        private static GameTestBed Talk()
        {
            var bed = GameTestBed.Create(Bar);
            bed.NewGame();
            bed.FinishDialogues();
            bed.Click("character.girl");
            bed.TakeLog();
            return bed;
        }

        [Test]
        public void AChoiceWaitsForAnOption()
        {
            var bed = Talk();
            Assert.That(bed.CurrentLine.Value.Text, Is.EqualTo("Привет."));

            bed.Advance();

            Assert.That(bed.Session.Dialogue.IsChoosing, Is.True);
            Assert.That(bed.CurrentChoices.Labels, Is.EqualTo(new[] { "Поздороваться", "Промолчать" }));
            Assert.That(bed.CurrentChoices.Speaker, Is.EqualTo(EntityId.Parse("character.paranoik")), "the hero answers");
            Assert.That(DialogueChoiceView.Format(0, "Поздороваться"), Is.EqualTo("1. [Поздороваться]"));
            Assert.That(bed.Session.Dialogue.Advance(), Is.False, "a choice is answered only by taking an option");
            Assert.That(bed.Session.Dialogue.Choose(2), Is.False, "there is no third option");
            Assert.That(bed.Session.Dialogue.IsChoosing, Is.True);
        }

        [Test]
        public void AnOptionPlaysItsLinesThenTheDialogueGoesOn()
        {
            var bed = Talk();
            bed.Advance();

            bed.Choose(0);
            Assert.That(bed.CurrentLine.Value.Text, Is.EqualTo("Привет и тебе."), "the hero's words were hidden behind the label");
            bed.Advance();
            Assert.That(bed.CurrentLine.Value.Text, Is.EqualTo("Хочешь узнать друг друга лучше?"), "back to the dialogue after the choice");

            var other = Talk();
            other.Advance();
            other.Choose(1);
            Assert.That(other.CurrentLine.Value.Text, Is.EqualTo("Хочешь узнать друг друга лучше?"), "an option without lines goes straight on");
            other.AssertNoErrors();
        }

        [Test]
        public void AnEndingOptionEndsTheConversation()
        {
            var bed = Talk();
            bed.Advance();
            bed.Choose(1);
            bed.Advance();

            bed.Choose(1);
            Assert.That(bed.CurrentLine.Value.Text, Is.EqualTo("В другой раз."));
            bed.Advance();

            Assert.That(bed.Session.Dialogue.IsOpen, Is.False);
            Assert.That(bed.Flag("talked"), Is.True, "the scene went on after the dialogue");
            Assert.That(bed.Session.Flow.Presentation, Is.EqualTo(DialoguePresentation.None));
            bed.AssertNoErrors();
        }

        [Test]
        public void AnOptionGoesOnIntoTheNovelAndBackToTheWorld()
        {
            var bed = Talk();
            bed.Advance();
            bed.Choose(1);
            bed.Advance();
            bed.Choose(0);
            Assert.That(bed.CurrentLine.Value.Mode, Is.EqualTo(DialogueMode.Normal));
            bed.Advance(); // "Да, я давно хотел этого." -> the novel

            Assert.That(bed.Session.Flow.Presentation, Is.EqualTo(DialoguePresentation.Novel));
            Assert.That(bed.Session.Dialogue.Current, Is.EqualTo(EntityId.Parse("dialogue.flirt_novel")));
            var line = bed.CurrentLine.Value;
            Assert.That(line.Mode, Is.EqualTo(DialogueMode.Novel));
            Assert.That(line.Partner, Is.EqualTo(EntityId.Parse("character.girl")));
            Assert.That(line.SpeakerName, Is.EqualTo("Девушка"), "a novel shows names");
            Assert.That(line.PartnerEmotion, Is.EqualTo("smirk"), "her picture follows her line");

            bed.Advance();
            Assert.That(bed.CurrentChoices.Mode, Is.EqualTo(DialogueMode.Novel));
            Assert.That(bed.CurrentChoices.PartnerEmotion, Is.EqualTo("smirk"), "her picture stays while the hero chooses");
            bed.Choose(0);
            Assert.That(bed.CurrentLine.Value.SpeakerName, Is.EqualTo("Параноик"));
            Assert.That(bed.CurrentLine.Value.PartnerEmotion, Is.EqualTo("smirk"), "the hero's line keeps her picture");
            bed.Advance();
            Assert.That(bed.CurrentLine.Value.PartnerEmotion, Is.EqualTo("default"), "her line without an emotion: default");
            Assert.That(bed.Flag("talked"), Is.False, "the scene waits for the whole conversation");

            bed.Advance();
            Assert.That(bed.Session.Dialogue.IsOpen, Is.False);
            Assert.That(bed.Session.Flow.Presentation, Is.EqualTo(DialoguePresentation.None), "back to the world");
            Assert.That(bed.Flag("talked"), Is.True);
            Assert.That(bed.TakeLog().Count(e => e == "hide"), Is.EqualTo(1), "one conversation: closed once");
            bed.AssertNoErrors();
        }

        [Test]
        public void ANovelWhosePartnerIsElsewhereIsRefusedBeforeTheConversationStarts()
        {
            // The conversation starts with the girl here, but its novel is with someone in the corridor.
            var content = Bar.Select(d => d.Replace("'partner': 'character.girl'", "'partner': 'character.date'")
                    .Replace("character.girl', 'text': 'Вот так", "character.date', 'text': 'Вот так")
                    .Replace("character.girl', 'text': 'Мне пора.", "character.date', 'text': 'Мне пора."))
                .Concat(new[]
                {
                    "{ 'id': 'location.corridor', 'scene': 'Location_Corridor' }",
                    "{ 'id': 'character.date', 'name': 'Свидание', 'initial_location': 'location.corridor', 'primary_interaction': 'talk', 'emotions': ['default', 'smirk'] }",
                }).ToArray();
            var bed = GameTestBed.Create(content);
            bed.NewGame();
            bed.FinishDialogues();

            bed.Click("character.girl");

            Assert.That(bed.Session.Dialogue.IsOpen, Is.False, "not halfway: before the first line");
            Assert.That(bed.Errors, Is.EqualTo(new[] { DiagnosticCode.OperationFailed }));
        }
    }
}
