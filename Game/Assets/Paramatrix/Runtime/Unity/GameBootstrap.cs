using System;
using System.Collections.Generic;
using Paramatrix.Content;
using Paramatrix.Gameplay;
using UnityEngine;

namespace Paramatrix.Unity
{
    /// <summary>
    /// Composition root in the persistent Bootstrap scene (plan 0.2, II.7): loads content, builds the GameSession
    /// with Unity implementations of its ports, starts a new game, and drives Scenario every frame.
    /// No static state: everything lives in this component's session.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField]
        private ContentManifest manifest;

        [SerializeField]
        private HeroController hero;

        [SerializeField]
        private InputRouter input;

        [SerializeField]
        private DialogueView dialogueView;

        [SerializeField]
        private InventoryView inventoryView;

        [SerializeField]
        private WorldPause worldPause;

        [SerializeField]
        private SaveControls saveControls;

        [SerializeField]
        private TransitionCurtain transitionCurtain;

        [SerializeField]
        private DiagnosticsOverlay overlay;

        [SerializeField]
        [Tooltip("Start a new game immediately (until there is a main menu).")]
        private bool startNewGameOnPlay = true;

        private SceneLoader _scenes;

        public GameSession Session { get; private set; }

        public UnityDiagnostics Diagnostics { get; private set; }

        public RepresentationRegistry Representations { get; private set; }

        public SceneLoader Scenes => _scenes;

        public HeroController Hero => hero;

        public InputRouter Input => input;

        public DialogueView DialogueView => dialogueView;

        public InventoryView InventoryView => inventoryView;

        public WorldPause WorldPause => worldPause;

        public SaveControls SaveControls => saveControls;

        public TransitionCurtain TransitionCurtain => transitionCurtain;

        public TMPro.TMP_FontAsset Font { get; private set; }

        /// <summary>True once the game is running (new game or load finished).</summary>
        public bool IsReady { get; private set; }

        /// <summary>True when content or start-up failed; details are in <see cref="Diagnostics"/>.</summary>
        public bool HasFailed { get; private set; }

        private void Start()
        {
            Diagnostics = new UnityDiagnostics();
            overlay.Bind(Diagnostics);

            List<ContentSource> sources;
            try
            {
                sources = manifest.ToSources();
            }
            catch (InvalidOperationException exception)
            {
                Fail(exception.Message);
                return;
            }

            Font = UiFonts.CreateSystemFont(out var fontError);
            if (Font == null)
            {
                Fail("UI font: " + fontError);
                return;
            }

            _scenes = new SceneLoader(hero);
            var operations = new WorldOperations();
            var storage = new FileSaveStorage(Application.persistentDataPath);
            var ports = new PresentationPorts(_scenes, hero, dialogueView, operations, operations, storage);

            Session = GameSession.Create(sources, ports, Diagnostics, out _);
            if (Session == null)
            {
                HasFailed = true; // content errors are already reported to the overlay
                return;
            }

            Representations = new RepresentationRegistry(Session.Registry, Session.State, Diagnostics);
            if (!Representations.RegisterPersistent(hero.View))
            {
                HasFailed = true;
                return;
            }

            if (hero.View.Id != Session.Registry.Game.PlayerCharacter)
            {
                Fail($"The hero in the Bootstrap scene is '{hero.View.Id}', but game.settings names '{Session.Registry.Game.PlayerCharacter}'.");
                return;
            }

            _scenes.Bind(Session.Registry, Representations);
            hero.Bind(Representations);
            operations.Bind(Representations, Session.Scenario);
            input.Bind(Session, hero, Representations);
            dialogueView.Bind(Representations, Diagnostics, Font);
            inventoryView.Bind(Session, Font);
            worldPause.Bind(Session.Flow);
            transitionCurtain.Bind(Session.Flow); // before StartNewGame: the first scene load is covered too
            saveControls.Bind(Session, Diagnostics, Font);

            if (startNewGameOnPlay)
            {
                Session.StartNewGame(ok =>
                {
                    IsReady = ok;
                    HasFailed = !ok;
                });
            }
        }

        private void Update()
        {
            if (IsReady)
            {
                Session.Tick();
            }
        }

        private void Fail(string message)
        {
            HasFailed = true;
            Diagnostics.Report(new Diagnostic(DiagnosticCode.StartupFailed, Severity.Error, message, SourceLocation.None));
        }
    }
}
