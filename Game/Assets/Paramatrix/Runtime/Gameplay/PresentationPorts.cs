using System;
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

    public enum ApproachOutcome
    {
        Arrived = 1,

        /// <summary>The player moved the hero with the keyboard (T13). Not an error.</summary>
        Cancelled,

        /// <summary>The target has no representation or approach point in the scene.</summary>
        Failed,
    }

    /// <summary>Free Roam walk of the hero to an object's approach point before an interaction (T13).</summary>
    public interface IHeroNavigation
    {
        /// <param name="done">Called exactly once, unless <see cref="Cancel"/> is called first.</param>
        void Approach(EntityId target, Action<ApproachOutcome, string> done);

        /// <summary>Stops the current approach; its callback is not called.</summary>
        void Cancel();
    }

    public readonly struct DialogueLineView
    {
        public DialogueLineView(EntityId speaker, string speakerName, string text, DialogueMode mode)
        {
            Speaker = speaker;
            SpeakerName = speakerName;
            Text = text;
            Mode = mode;
        }

        public EntityId Speaker { get; }

        public string SpeakerName { get; }

        public string Text { get; }

        public DialogueMode Mode { get; }
    }

    /// <summary>Shows dialogue lines; the player advances them through <see cref="DialogueSystem.Advance"/>.</summary>
    public interface IDialogueView
    {
        void Show(DialogueLineView line);

        void Hide();
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
            IHeroNavigation navigation,
            IDialogueView dialogue,
            IMovementPort movement,
            IAnimationPort animation,
            ISaveStorage storage)
        {
            Scenes = scenes ?? throw new ArgumentNullException(nameof(scenes));
            Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            Dialogue = dialogue ?? throw new ArgumentNullException(nameof(dialogue));
            Movement = movement ?? throw new ArgumentNullException(nameof(movement));
            Animation = animation ?? throw new ArgumentNullException(nameof(animation));
            Storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        public ISceneLoader Scenes { get; }

        public IHeroNavigation Navigation { get; }

        public IDialogueView Dialogue { get; }

        public IMovementPort Movement { get; }

        public IAnimationPort Animation { get; }

        public ISaveStorage Storage { get; }
    }
}
