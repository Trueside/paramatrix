using System;
using System.Collections.Generic;
using Paramatrix.Conditions;
using Paramatrix.Content;
using Paramatrix.Scenario;
using Paramatrix.State;

namespace Paramatrix.Gameplay
{
    /// <summary>
    /// Composition root of the game logic: builds every system from validated content and runs the start order (Р2).
    /// Owns no Unity objects; the Unity layer supplies <see cref="PresentationPorts"/> and calls <see cref="Tick"/> every frame.
    /// </summary>
    public sealed class GameSession
    {
        private GameSession(ContentRegistry registry, PresentationPorts ports, IDiagnosticsSink diagnostics, ScenarioLimits limits)
        {
            Registry = registry;
            Diagnostics = diagnostics;
            State = new StateSystem(registry, diagnostics);
            Conditions = new ConditionEvaluator(registry, State);
            Flow = new GameFlow(diagnostics);
            Dialogue = new DialogueSystem(registry, State, Flow, ports.Dialogue);
            Inventory = new InventorySystem(registry, State);
            Location = new LocationSystem(registry, State, Flow, ports.Scenes, diagnostics);
            Interaction = new InteractionSystem(registry, Flow, Inventory, Dialogue, ports.Reach);

            Scenario = new ScenarioRuntime(
                registry,
                State,
                Conditions,
                new ScenarioPorts(Flow, Dialogue, ports.Movement, ports.Animation, Location, Inventory),
                limits ?? ScenarioLimits.Default,
                diagnostics);

            Flow.Attach(Scenario);
            Dialogue.Attach(Scenario);
            Inventory.Attach(Scenario);
            Location.Attach(Scenario);
            Interaction.Attach(Scenario);
            Save = new SaveService(registry, State, Scenario, Flow, Location, ports.Storage);

            Scenario.Start(); // indexes and monitor baselines from default State; no triggers (0.3.1 §7)
        }

        public ContentRegistry Registry { get; }

        public IDiagnosticsSink Diagnostics { get; }

        public StateSystem State { get; }

        public ConditionEvaluator Conditions { get; }

        public GameFlow Flow { get; }

        public DialogueSystem Dialogue { get; }

        public InventorySystem Inventory { get; }

        public LocationSystem Location { get; }

        public InteractionSystem Interaction { get; }

        public ScenarioRuntime Scenario { get; }

        public SaveService Save { get; }

        /// <summary>True between session creation and the end of New Game / the first successful Load.</summary>
        public bool IsBooting { get; private set; } = true;

        /// <summary>
        /// Loads and validates content and builds the session. Returns null when content has errors:
        /// invalid content blocks the start (0.3.1 §10); the errors are in <paramref name="contentDiagnostics"/>.
        /// </summary>
        public static GameSession Create(
            IReadOnlyList<ContentSource> sources,
            PresentationPorts ports,
            IDiagnosticsSink diagnostics,
            out IReadOnlyList<Diagnostic> contentDiagnostics,
            ScenarioLimits limits = null)
        {
            if (ports == null)
            {
                throw new ArgumentNullException(nameof(ports));
            }

            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            var loaded = ContentLoader.Load(sources);
            contentDiagnostics = loaded.Diagnostics;
            foreach (var diagnostic in loaded.Diagnostics)
            {
                diagnostics.Report(diagnostic);
            }

            return loaded.Succeeded ? new GameSession(loaded.Registry, ports, diagnostics, limits) : null;
        }

        /// <summary>
        /// New Game (Р2): ordinary entry into the hero's initial location after every system is ready,
        /// so the opening scene starts from <c>location_entered</c>.
        /// </summary>
        public void StartNewGame(Action<bool> done)
        {
            RequireBooting();
            Location.EnterInitial(ok =>
            {
                if (ok)
                {
                    IsBooting = false;
                    Flow.EndSystemOperation(Flow.BootToken);
                    Scenario.Pump(); // the opening scene was queued while System held control
                }

                done?.Invoke(ok);
            });
        }

        /// <summary>
        /// Load Game (Р2, Р9): restore State and Scenario, load the location technically. From boot (main menu)
        /// or from free roam. On failure the world stays as it was.
        /// </summary>
        public void LoadGame(string slot, Action<SaveLoadResult> done)
        {
            SystemToken token;
            if (IsBooting)
            {
                token = Flow.BootToken;
            }
            else
            {
                if (!Save.CanSave)
                {
                    done?.Invoke(new SaveLoadResult(new[]
                    {
                        new Diagnostic(DiagnosticCode.SaveFailed, Severity.Error, "Loading is possible only in free roam while no scene is running.", new SourceLocation(slot, null, 0, 0)),
                    }));
                    return;
                }

                token = Flow.BeginSystemOperation(SystemOperation.LoadGame);
            }

            Save.Load(slot, result =>
            {
                if (result.Succeeded || !IsBooting)
                {
                    IsBooting = false;
                    Flow.EndSystemOperation(token);
                    Scenario.Pump();
                }

                done?.Invoke(result);
            });
        }

        /// <summary>Called every frame: processes State changes made outside Scenario and resumes queued scenes.</summary>
        public void Tick() => Scenario.Pump();

        private void RequireBooting()
        {
            if (!IsBooting)
            {
                throw new InvalidOperationException("A game is already running.");
            }
        }
    }
}
