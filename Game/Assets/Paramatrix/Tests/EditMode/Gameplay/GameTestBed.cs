using System;
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
    /// A whole game session with a fake Unity layer. Scene loads complete immediately; every target is within the
    /// hero's reach unless listed in <see cref="OutOfReach"/>; dialogue lines are recorded and advanced explicitly; saves
    /// live in memory.
    /// </summary>
    internal sealed class GameTestBed : ISceneLoader, IHeroReach, IDialogueView, IMovementPort, IAnimationPort, ISaveStorage
    {
        private readonly Dictionary<string, string> _saves;

        private GameTestBed(Dictionary<string, string> saves)
        {
            _saves = saves;
        }

        public GameSession Session { get; private set; }

        public DiagnosticBag Diagnostics { get; } = new DiagnosticBag();

        public List<string> Log { get; } = new List<string>();

        public DialogueLineView? CurrentLine { get; private set; }

        /// <summary>Targets (IDs) the hero cannot reach now (Р11).</summary>
        public SortedSet<string> OutOfReach { get; } = new SortedSet<string>(StringComparer.Ordinal);

        public bool FailSceneLoad { get; set; }

        public string LoadedScene { get; private set; }

        public string LoadedEntry { get; private set; }

        public IReadOnlyDictionary<string, string> Saves => _saves;

        public static GameTestBed Create(string[] definitions, Dictionary<string, string> saves = null)
        {
            var bed = new GameTestBed(saves ?? new Dictionary<string, string>());
            var sources = new[] { TestContent.Base(), TestContent.FallbackSource(), new ContentSource("game.json", TestContent.File(definitions)) };
            bed.Session = GameSession.Create(
                sources,
                new PresentationPorts(bed, bed, bed, bed, bed, bed),
                bed.Diagnostics,
                out var content);
            Assert.That(bed.Session, Is.Not.Null, TestContent.Describe(content));
            return bed;
        }

        // ---------- driving ----------

        public void NewGame()
        {
            var ok = false;
            Session.StartNewGame(result => ok = result);
            Assert.That(ok, Is.True, TestContent.Describe(Diagnostics.Items));
        }

        public void Click(string target, PointerButton button = PointerButton.Primary) =>
            Session.Interaction.ClickWorld(EntityId.Parse(target), button);

        public void ClickItem(string item, PointerButton button = PointerButton.Primary) =>
            Session.Interaction.ClickInventoryItem(EntityId.Parse(item), button);

        public void Advance() => Assert.That(Session.Dialogue.Advance(), Is.True, "no dialogue to advance");

        /// <summary>Advances until no dialogue is open (bounded).</summary>
        public void FinishDialogues()
        {
            for (var guard = 0; Session.Dialogue.IsOpen; guard++)
            {
                Assert.That(guard, Is.LessThan(50));
                Advance();
            }
        }

        public void ClickEmpty(PointerButton button = PointerButton.Secondary) => Session.Interaction.ClickEmpty(button);

        public StateValue Get(string entity, string property) => Session.State.Get(StateAddress.Parse(entity, property));

        public bool Flag(string property) => Get("story.flags", property).AsBoolean;

        public string Location => Session.Location.CurrentLocation.Value;

        public DiagnosticCode[] Errors => Diagnostics.Items.Where(d => d.Severity == Severity.Error).Select(d => d.Code).ToArray();

        public void AssertNoErrors() =>
            Assert.That(Diagnostics.Items.Where(d => d.Severity == Severity.Error), Is.Empty, TestContent.Describe(Diagnostics.Items));

        public string[] TakeLog()
        {
            var copy = Log.ToArray();
            Log.Clear();
            return copy;
        }

        public SaveLoadResult SaveGame(string slot = "slot1") => Session.Save.Save(slot);

        /// <summary>Starts a fresh session on the same content and saves, and loads the slot from boot.</summary>
        public static GameTestBed LoadIntoNewSession(string[] definitions, IReadOnlyDictionary<string, string> saves, string slot, out SaveLoadResult result)
        {
            var bed = Create(definitions, new Dictionary<string, string>(saves.ToDictionary(p => p.Key, p => p.Value)));
            SaveLoadResult captured = null;
            bed.Session.LoadGame(slot, r => captured = r);
            Assert.That(captured, Is.Not.Null);
            result = captured;
            return bed;
        }

        // ---------- ISceneLoader ----------

        public void LoadLocationScene(string scene, string entry, Action<bool, string> done)
        {
            Log.Add($"scene:{scene}:{entry ?? "default"}");
            if (FailSceneLoad)
            {
                done(false, "fake scene failure");
                return;
            }

            LoadedScene = scene;
            LoadedEntry = entry;
            done(true, null);
        }

        // ---------- IHeroReach ----------

        public bool CanReach(EntityId target) => !OutOfReach.Contains(target.Value);

        public void FaceForInteraction(EntityId target) => Log.Add("face:" + target);

        // ---------- IDialogueView ----------

        public void Show(DialogueLineView line)
        {
            CurrentLine = line;
            CurrentChoices = null;
            Log.Add($"say:{line.Speaker.Name}:{line.Text}");
        }

        public void Hide()
        {
            CurrentLine = null;
            CurrentChoices = null;
            Log.Add("hide");
        }

        public DialogueChoiceView CurrentChoices { get; private set; }

        public void ShowChoices(DialogueChoiceView choices)
        {
            CurrentLine = null;
            CurrentChoices = choices;
            Log.Add("choose:" + string.Join("|", choices.Labels));
        }

        public void Choose(int index) => Assert.That(Session.Dialogue.Choose(index), Is.True, "no such option to take");

        /// <summary>Monologue bubbles shown now, by speaker name (Р10).</summary>
        public SortedDictionary<string, string> Bubbles { get; } = new SortedDictionary<string, string>(StringComparer.Ordinal);

        public void Say(EntityId speaker, string text)
        {
            Bubbles[speaker.Name] = text;
            Log.Add($"bubble:{speaker.Name}:{text}");
        }

        public bool IsMonologueShown(EntityId speaker) => Bubbles.ContainsKey(speaker.Name);

        public void HideMonologue(EntityId speaker)
        {
            if (Bubbles.Remove(speaker.Name))
            {
                Log.Add("bubble:hide:" + speaker.Name);
            }
        }

        public void ClearMonologues()
        {
            if (Bubbles.Count > 0)
            {
                Bubbles.Clear();
                Log.Add("bubbles:clear");
            }
        }

        // ---------- movement / animation: complete at once ----------

        public OperationStart MoveTo(OperationHandle handle, EntityId character, EntityId target)
        {
            Log.Add($"move:{character.Name}->{target.Name}");
            return OperationStart.CompletedImmediately;
        }

        public OperationStart Play(OperationHandle handle, EntityId target, string clip)
        {
            Log.Add($"anim:{target.Name}:{clip}");
            return OperationStart.CompletedImmediately;
        }

        // ---------- ISaveStorage ----------

        public bool TryWrite(string slot, string json, out string error)
        {
            _saves[slot] = json;
            error = null;
            return true;
        }

        public bool TryRead(string slot, out string json, out string error)
        {
            if (_saves.TryGetValue(slot, out json))
            {
                error = null;
                return true;
            }

            error = "no such slot";
            return false;
        }
    }
}
