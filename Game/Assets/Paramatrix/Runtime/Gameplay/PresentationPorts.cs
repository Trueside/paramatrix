using System;
using System.Collections.Generic;
using Paramatrix.Content;
using Paramatrix.Scenario;

namespace Paramatrix.Gameplay
{
    // Ports implemented by the Unity layer. Gameplay logic decides; Unity only shows, moves and loads.

    /// <summary>Unloads the current location scene, loads another, places the hero, registers representations.</summary>
    public interface ISceneLoader
    {
        /// <param name="entry">Entry point name; null = the scene's default entry point.</param>
        /// <param name="done">Called exactly once: true on success, false with an error message otherwise.</param>
        void LoadLocationScene(string scene, string entry, Action<bool, string> done);
    }

    /// <summary>
    /// The hero's reach (Р11): the player walks the hero himself; a target counts only within the hero's interaction
    /// radius. The hero is never walked to a target by a click.
    /// </summary>
    public interface IHeroReach
    {
        /// <summary>The target is shown in the current scene and within the hero's interaction radius.</summary>
        bool CanReach(EntityId target);

        /// <summary>A scene starts on the target: the hero stops where he is and looks at it; a character looks back.</summary>
        void FaceForInteraction(EntityId target);
    }

    /// <summary>
    /// One line of a conversation (Р10, Р12). Normal mode: the speaker's avatar with this emotion and a bubble. Novel: the
    /// partner's large picture (by the emotion of the partner's last line) and the line in the panel under the speaker's name.
    /// </summary>
    public readonly struct DialogueLineView
    {
        public DialogueLineView(EntityId speaker, string speakerName, string text, string emotion, DialogueMode mode, EntityId? partner, string partnerEmotion)
        {
            Speaker = speaker;
            SpeakerName = speakerName;
            Text = text;
            Emotion = emotion;
            Mode = mode;
            Partner = partner;
            PartnerEmotion = partnerEmotion;
        }

        public EntityId Speaker { get; }

        /// <summary>Shown in a novel's panel; a normal conversation shows no names.</summary>
        public string SpeakerName { get; }

        public string Text { get; }

        public string Emotion { get; }

        public DialogueMode Mode { get; }

        /// <summary>Novel only: whose picture is in the middle of the screen.</summary>
        public EntityId? Partner { get; }

        /// <summary>Novel only: the partner's picture now — the emotion of the partner's last line, default before it.</summary>
        public string PartnerEmotion { get; }
    }

    /// <summary>
    /// The hero's choice (Р12): numbered options with their labels; the player takes one by a click or its number key.
    /// Normal mode: in the hero's bubble by his avatar. Novel: in the panel.
    /// </summary>
    public sealed class DialogueChoiceView
    {
        public DialogueChoiceView(EntityId speaker, string speakerName, IReadOnlyList<string> labels, DialogueMode mode, EntityId? partner, string partnerEmotion)
        {
            Speaker = speaker;
            SpeakerName = speakerName;
            Labels = labels ?? throw new ArgumentNullException(nameof(labels));
            Mode = mode;
            Partner = partner;
            PartnerEmotion = partnerEmotion;
        }

        /// <summary>The hero: he is the one who answers.</summary>
        public EntityId Speaker { get; }

        public string SpeakerName { get; }

        public IReadOnlyList<string> Labels { get; }

        public DialogueMode Mode { get; }

        public EntityId? Partner { get; }

        public string PartnerEmotion { get; }

        /// <summary>How an option is written on screen: "1. [Да]".</summary>
        public static string Format(int index, string label) => $"{index + 1}. [{label}]";
    }

    /// <summary>
    /// Shows conversations and monologues (Р10). Conversation lines are advanced by the player through
    /// <see cref="DialogueSystem.Advance"/>; monologue bubbles live by themselves (timer, next line of the same speaker).
    /// </summary>
    public interface IDialogueView
    {
        /// <summary>Shows a conversation line in place of the previous one.</summary>
        void Show(DialogueLineView line);

        /// <summary>Shows the hero's choice in place of the line; it stays until an option is taken.</summary>
        void ShowChoices(DialogueChoiceView choices);

        /// <summary>Closes the conversation.</summary>
        void Hide();

        /// <summary>A monologue bubble over the speaker; replaces that speaker's bubble if it is still shown.</summary>
        void Say(EntityId speaker, string text);

        /// <summary>Removes every monologue bubble (a conversation opens, the location changes, Recovery).</summary>
        void ClearMonologues();

        /// <summary>The speaker's monologue bubble is shown now (its timer has not run out).</summary>
        bool IsMonologueShown(EntityId speaker);

        /// <summary>Removes the speaker's monologue bubble; nothing happens if none is shown.</summary>
        void HideMonologue(EntityId speaker);
    }

    public interface ISaveStorage
    {
        bool TryWrite(string slot, string json, out string error);

        bool TryRead(string slot, out string json, out string error);
    }

    /// <summary>Everything the Unity layer provides to a <see cref="GameSession"/>.</summary>
    public sealed class PresentationPorts
    {
        public PresentationPorts(
            ISceneLoader scenes,
            IHeroReach reach,
            IDialogueView dialogue,
            IMovementPort movement,
            IAnimationPort animation,
            ISaveStorage storage)
        {
            Scenes = scenes ?? throw new ArgumentNullException(nameof(scenes));
            Reach = reach ?? throw new ArgumentNullException(nameof(reach));
            Dialogue = dialogue ?? throw new ArgumentNullException(nameof(dialogue));
            Movement = movement ?? throw new ArgumentNullException(nameof(movement));
            Animation = animation ?? throw new ArgumentNullException(nameof(animation));
            Storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        public ISceneLoader Scenes { get; }

        public IHeroReach Reach { get; }

        public IDialogueView Dialogue { get; }

        public IMovementPort Movement { get; }

        public IAnimationPort Animation { get; }

        public ISaveStorage Storage { get; }
    }
}
