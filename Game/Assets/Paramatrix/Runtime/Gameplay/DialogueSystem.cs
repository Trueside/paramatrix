using System;
using System.Collections.Generic;
using Paramatrix.Content;
using Paramatrix.Scenario;
using Paramatrix.State;

namespace Paramatrix.Gameplay
{
    /// <summary>
    /// Plays linear dialogues line by line (Architecture 0.3 §2). Decides nothing about the story:
    /// it reports completion, and Scenario continues. A dialogue completes only when its last line is advanced;
    /// a technical reset (Recovery) is not a completion.
    /// </summary>
    public sealed class DialogueSystem : IDialoguePort
    {
        private readonly ContentRegistry _registry;
        private readonly StateSystem _state;
        private readonly GameFlow _flow;
        private readonly IDialogueView _view;
        private ScenarioRuntime _scenario;

        private OperationHandle _handle;
        private List<DialogueLineView> _lines;
        private int _index;

        public DialogueSystem(ContentRegistry registry, StateSystem state, GameFlow flow, IDialogueView view)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _flow = flow ?? throw new ArgumentNullException(nameof(flow));
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _flow.Recovering += Reset;
        }

        public bool IsOpen => _lines != null;

        internal void Attach(ScenarioRuntime scenario) => _scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));

        public OperationStart Show(OperationHandle handle, EntityId dialogue)
        {
            if (!_registry.TryGet<DialogueDefinition>(dialogue, out var definition))
            {
                return OperationStart.Failed($"dialogue {dialogue} is not defined");
            }

            var lines = new List<DialogueLineView>();
            foreach (var line in definition.Lines)
            {
                lines.Add(new DialogueLineView(line.Speaker, SpeakerName(line.Speaker), line.Text, definition.Mode));
            }

            return Open(handle, lines);
        }

        public OperationStart ShowLine(OperationHandle handle, EntityId speaker, string text) =>
            Open(handle, new List<DialogueLineView> { new DialogueLineView(speaker, SpeakerName(speaker), text, DialogueMode.Minimal) });

        /// <summary>Player confirmation (click / Space). Returns false when no dialogue is open.</summary>
        public bool Advance()
        {
            if (_lines == null || (_flow.Permissions & PlayerPermissions.AdvanceDialogue) == 0)
            {
                return false;
            }

            _index++;
            if (_index < _lines.Count)
            {
                _view.Show(_lines[_index]);
                return true;
            }

            var handle = _handle;
            Close();
            _scenario.CompleteOperation(handle, OperationOutcome.Completed);
            return true;
        }

        private OperationStart Open(OperationHandle handle, List<DialogueLineView> lines)
        {
            if (_lines != null)
            {
                return OperationStart.Failed("another dialogue is already open");
            }

            // A bubble needs its speaker on screen: in Minimalistic mode every speaker must be in the current location.
            foreach (var line in lines)
            {
                if (line.Mode == DialogueMode.Minimal && !IsPresent(line.Speaker))
                {
                    return OperationStart.Failed($"{line.Speaker} speaks in a speech bubble but is not in the current location");
                }
            }

            _handle = handle;
            _lines = lines;
            _index = 0;
            _flow.SetPresentation(lines[0].Mode == DialogueMode.VisualNovel ? DialoguePresentation.VisualNovel : DialoguePresentation.Minimal);
            _view.Show(lines[0]);
            return OperationStart.Started;
        }

        private bool IsPresent(EntityId speaker)
        {
            var hero = _registry.Game.PlayerCharacter;
            if (speaker == hero)
            {
                return true;
            }

            var heroLocation = _state.Get(new StateAddress(hero, BuiltInStates.Location));
            var speakerLocation = _state.Get(new StateAddress(speaker, BuiltInStates.Location));
            return heroLocation == speakerLocation;
        }

        private string SpeakerName(EntityId speaker) =>
            _registry.TryGet<CharacterDefinition>(speaker, out var character) ? character.Name : speaker.Value;

        private void Close()
        {
            _lines = null;
            _view.Hide();
            _flow.SetPresentation(DialoguePresentation.None);
        }

        /// <summary>Technical reset on Recovery: closes the view without reporting completion.</summary>
        private void Reset()
        {
            if (_lines != null)
            {
                Close();
            }
        }
    }
}
