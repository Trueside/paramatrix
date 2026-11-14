using System;
using System.Collections.Generic;
using Paramatrix.Content;
using Paramatrix.Scenario;
using Paramatrix.State;

namespace Paramatrix.Gameplay
{
    /// <summary>
    /// Conversations and monologues (Architecture 0.3 §2, Р10, Р12). Decides nothing about the story.
    /// A conversation is played step by step: a line waits for the player to advance; a choice waits for the player to
    /// take one option (<see cref="Choose"/>), whose lines are played next. After its lines an option goes back to the
    /// dialogue, ends it, or goes on in another dialogue (into a novel and back to the world). The conversation is reported
    /// complete only when it ends; a technical reset (Recovery) is not a completion. Choices are not remembered (Р12).
    /// A monologue line is shown at once and is never waited for.
    /// Every speaker must be in the current location: avatars and bubbles stand over the speaker's figure.
    /// </summary>
    public sealed class DialogueSystem : IDialoguePort
    {
        private readonly ContentRegistry _registry;
        private readonly StateSystem _state;
        private readonly GameFlow _flow;
        private readonly IDialogueView _view;
        private ScenarioRuntime _scenario;

        private OperationHandle _handle;
        private DialogueDefinition _dialogue;
        private readonly List<Frame> _stack = new List<Frame>();
        private DialogueChoice _choice;
        private string _partnerEmotion;

        public DialogueSystem(ContentRegistry registry, StateSystem state, GameFlow flow, IDialogueView view)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _flow = flow ?? throw new ArgumentNullException(nameof(flow));
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _flow.Recovering += Reset;

            // Leaving a location (transition, load) takes its monologues with it.
            _flow.Changed += () =>
            {
                if (_flow.InTransition)
                {
                    _view.ClearMonologues();
                }
            };
        }

        public bool IsOpen => _dialogue != null;

        /// <summary>The dialogue on screen now (it changes when an option goes on in another one).</summary>
        public EntityId? Current => _dialogue?.Id;

        /// <summary>A choice is waiting for the player: advancing does nothing, only <see cref="Choose"/> goes on.</summary>
        public bool IsChoosing => _choice != null;

        /// <summary>Labels of the options of the waiting choice; empty when there is none.</summary>
        public IReadOnlyList<string> Choices => _choice == null ? (IReadOnlyList<string>)Array.Empty<string>() : Labels(_choice);

        internal void Attach(ScenarioRuntime scenario) => _scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));

        public OperationStart Show(OperationHandle handle, EntityId dialogue)
        {
            if (!_registry.TryGet<DialogueDefinition>(dialogue, out var definition))
            {
                return OperationStart.Failed($"dialogue {dialogue} is not defined");
            }

            if (_dialogue != null)
            {
                return OperationStart.Failed("another dialogue is already open");
            }

            // Everything the conversation may show is checked before it starts, the dialogues it goes on in included:
            // a missing speaker found halfway would leave the player stuck in it.
            var problem = Check(definition, new SortedSet<EntityId>());
            if (problem != null)
            {
                return OperationStart.Failed(problem);
            }

            _handle = handle;
            _view.ClearMonologues(); // a conversation takes the screen
            Start(definition);
            return OperationStart.Started;
        }

        public string Say(EntityId speaker, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                throw new ArgumentException("A monologue line needs text.", nameof(text));
            }

            if (!_registry.TryGet<CharacterDefinition>(speaker, out _))
            {
                return $"{speaker} is not a character";
            }

            if (!IsPresent(speaker))
            {
                return $"{speaker} is not in the current location";
            }

            _view.Say(speaker, text);
            return null;
        }

        /// <summary>The speaker's monologue bubble is shown now.</summary>
        public bool IsSaying(EntityId speaker) => _view.IsMonologueShown(speaker);

        /// <summary>Closes the speaker's monologue bubble.</summary>
        public void Hush(EntityId speaker) => _view.HideMonologue(speaker);

        /// <summary>
        /// Player confirmation (click / key): the next step. Returns false when no dialogue is open or a choice is waiting —
        /// a choice is answered only by taking an option.
        /// </summary>
        public bool Advance()
        {
            if (_dialogue == null || _choice != null || (_flow.Permissions & PlayerPermissions.AdvanceDialogue) == 0)
            {
                return false;
            }

            _stack[_stack.Count - 1].Index++;
            ShowStep();
            return true;
        }

        /// <summary>
        /// The player takes option <paramref name="index"/> (from 0) of the waiting choice: its lines are played next. Only
        /// one option is taken, there is no way back. Returns false when no choice is waiting or there is no such option.
        /// </summary>
        public bool Choose(int index)
        {
            if (_choice == null || index < 0 || index >= _choice.Options.Count || (_flow.Permissions & PlayerPermissions.AdvanceDialogue) == 0)
            {
                return false;
            }

            var option = _choice.Options[index];
            _choice = null;
            _stack[_stack.Count - 1].Index++; // past the choice: where a continuing option comes back to
            _stack.Add(new Frame(option.Steps, option));
            ShowStep();
            return true;
        }

        private void Start(DialogueDefinition definition)
        {
            _dialogue = definition;
            _stack.Clear();
            _stack.Add(new Frame(definition.Steps, null));
            _choice = null;
            _partnerEmotion = Emotions.Default;
            _flow.SetPresentation(definition.Mode == DialogueMode.Novel ? DialoguePresentation.Novel : DialoguePresentation.Normal);
            ShowStep();
        }

        /// <summary>Shows the step under the cursor; a finished branch returns, ends, or goes on in another dialogue.</summary>
        private void ShowStep()
        {
            while (true)
            {
                var top = _stack[_stack.Count - 1];
                if (top.Index < top.Steps.Count)
                {
                    switch (top.Steps[top.Index])
                    {
                        case DialogueLine line:
                            if (_dialogue.Mode == DialogueMode.Novel && line.Speaker == _dialogue.Partner)
                            {
                                _partnerEmotion = line.Emotion;
                            }

                            _view.Show(new DialogueLineView(line.Speaker, NameOf(line.Speaker), line.Text, line.Emotion, _dialogue.Mode, _dialogue.Partner, _partnerEmotion));
                            return;
                        case DialogueChoice choice:
                            _choice = choice;
                            var hero = _registry.Game.PlayerCharacter;
                            _view.ShowChoices(new DialogueChoiceView(hero, NameOf(hero), Labels(choice), _dialogue.Mode, _dialogue.Partner, _partnerEmotion));
                            return;
                        default:
                            throw new InvalidOperationException($"Unknown dialogue step {top.Steps[top.Index].GetType().Name}.");
                    }
                }

                _stack.RemoveAt(_stack.Count - 1);
                if (top.Option == null)
                {
                    Finish(); // the dialogue's own lines are over
                    return;
                }

                switch (top.Option.Exit)
                {
                    case DialogueOptionExit.Continue:
                        continue; // the parent is already past the choice
                    case DialogueOptionExit.End:
                        Finish();
                        return;
                    case DialogueOptionExit.Next:
                        Start(_registry.Get<DialogueDefinition>(top.Option.Next.Value));
                        return;
                    default:
                        throw new InvalidOperationException($"Unknown option exit {top.Option.Exit}.");
                }
            }
        }

        private void Finish()
        {
            var handle = _handle;
            Close();
            _scenario.CompleteOperation(handle, OperationOutcome.Completed);
        }

        /// <summary>Why the dialogue cannot be shown here, or null: speakers present, a novel's speakers named.</summary>
        private string Check(DialogueDefinition definition, SortedSet<EntityId> visited)
        {
            if (!visited.Add(definition.Id))
            {
                return null; // already checked (a conversation may come back to a dialogue)
            }

            if (definition.Partner.HasValue && !IsPresent(definition.Partner.Value))
            {
                return $"{definition.Partner} is the partner of {definition.Id} but is not in the current location";
            }

            return CheckSteps(definition, definition.Steps, visited);
        }

        private string CheckSteps(DialogueDefinition definition, IReadOnlyList<DialogueStep> steps, SortedSet<EntityId> visited)
        {
            foreach (var step in steps)
            {
                switch (step)
                {
                    case DialogueLine line:
                        if (!IsPresent(line.Speaker))
                        {
                            return $"{line.Speaker} speaks in {definition.Id} but is not in the current location";
                        }

                        if (definition.Mode == DialogueMode.Novel && string.IsNullOrEmpty(_registry.Get<CharacterDefinition>(line.Speaker).Name))
                        {
                            return $"{line.Speaker} speaks in the novel {definition.Id} but has no name to show in its panel";
                        }

                        break;
                    case DialogueChoice choice:
                        foreach (var option in choice.Options)
                        {
                            var problem = CheckSteps(definition, option.Steps, visited)
                                ?? (option.Exit == DialogueOptionExit.Next ? Check(_registry.Get<DialogueDefinition>(option.Next.Value), visited) : null);
                            if (problem != null)
                            {
                                return problem;
                            }
                        }

                        break;
                }
            }

            return null;
        }

        private string NameOf(EntityId character) => _registry.Get<CharacterDefinition>(character).Name;

        private static IReadOnlyList<string> Labels(DialogueChoice choice)
        {
            var labels = new List<string>(choice.Options.Count);
            foreach (var option in choice.Options)
            {
                labels.Add(option.Label);
            }

            return labels;
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

        private void Close()
        {
            _dialogue = null;
            _stack.Clear();
            _choice = null;
            _view.Hide();
            _flow.SetPresentation(DialoguePresentation.None);
        }

        /// <summary>Technical reset on Recovery: closes the view without reporting completion.</summary>
        private void Reset()
        {
            _view.ClearMonologues();
            if (_dialogue != null)
            {
                Close();
            }
        }

        /// <summary>A list of steps being played and where in it the conversation is.</summary>
        private sealed class Frame
        {
            public Frame(IReadOnlyList<DialogueStep> steps, DialogueOption option)
            {
                Steps = steps;
                Option = option;
            }

            public IReadOnlyList<DialogueStep> Steps { get; }

            /// <summary>The option whose lines these are; null for the dialogue's own lines.</summary>
            public DialogueOption Option { get; }

            public int Index { get; set; }
        }
    }
}
