using System;
using Paramatrix.Content;
using Paramatrix.Scenario;
using Paramatrix.State;

namespace Paramatrix.Gameplay
{
    /// <summary>
    /// One active location at a time, one Unity scene per location (Architecture 0.3 §3).
    /// Ordinary transitions start a new Visit and publish <c>location_entered</c>; technical loads (Load Game) do not.
    /// The hero's location is the built-in <c>location</c> State of the player character.
    /// </summary>
    public sealed class LocationSystem : ILocationPort
    {
        private readonly ContentRegistry _registry;
        private readonly StateSystem _state;
        private readonly GameFlow _flow;
        private readonly ISceneLoader _scenes;
        private readonly IDiagnosticsSink _diagnostics;
        private ScenarioRuntime _scenario;

        public LocationSystem(ContentRegistry registry, StateSystem state, GameFlow flow, ISceneLoader scenes, IDiagnosticsSink diagnostics)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _flow = flow ?? throw new ArgumentNullException(nameof(flow));
            _scenes = scenes ?? throw new ArgumentNullException(nameof(scenes));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        private StateAddress HeroLocation => new StateAddress(_registry.Game.PlayerCharacter, BuiltInStates.Location);

        /// <summary>The location the hero is in, according to Logical State.</summary>
        public EntityId CurrentLocation => EntityId.Parse(_state.Get(HeroLocation).AsEnum);

        internal void Attach(ScenarioRuntime scenario) => _scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));

        /// <summary>Ordinary transition requested by a scene (Р7).</summary>
        public OperationStart Transition(OperationHandle handle, EntityId location, string entry)
        {
            if (_flow.InTransition)
            {
                return OperationStart.Failed("another transition is in progress");
            }

            if (!_registry.TryGet<LocationDefinition>(location, out var definition))
            {
                return OperationStart.Failed($"{location} is not a location");
            }

            _flow.SetTransition(true);
            _scenes.LoadLocationScene(definition.Scene, entry, (ok, error) =>
            {
                _flow.SetTransition(false);
                if (!ok)
                {
                    Report(definition, error);
                    _scenario.CompleteOperation(handle, OperationOutcome.Failed);
                    return;
                }

                // Order: State, then the new Visit and its trigger, then completion of the waiting action.
                _state.Set(HeroLocation, StateValue.Enum(location.Value), MutationSource.System);
                _scenario.BeginLocationVisit(location);
                _scenario.CompleteOperation(handle, OperationOutcome.Completed);
            });

            return OperationStart.Started;
        }

        /// <summary>New Game (Р2): ordinary entry into the hero's initial location at the default entry point.</summary>
        public void EnterInitial(Action<bool> done)
        {
            var location = CurrentLocation;
            var definition = _registry.Get<LocationDefinition>(location);
            _flow.SetTransition(true);
            _scenes.LoadLocationScene(definition.Scene, null, (ok, error) =>
            {
                _flow.SetTransition(false);
                if (!ok)
                {
                    Report(definition, error);
                    done(false);
                    return;
                }

                _scenario.BeginLocationVisit(location);
                done(true);
            });
        }

        /// <summary>
        /// Load Game (Р2, Р9): loads the scene of the restored hero location at its default entry point.
        /// No Visit, no trigger, no State change.
        /// </summary>
        public void LoadTechnical(Action<bool> done)
        {
            var definition = _registry.Get<LocationDefinition>(CurrentLocation);
            _flow.SetTransition(true);
            _scenes.LoadLocationScene(definition.Scene, null, (ok, error) =>
            {
                _flow.SetTransition(false);
                if (!ok)
                {
                    Report(definition, error);
                }

                done(ok);
            });
        }

        private void Report(LocationDefinition definition, string error) =>
            _diagnostics.Report(new Diagnostic(
                DiagnosticCode.SceneLoadFailed,
                Severity.Error,
                $"Scene '{definition.Scene}' of {definition.Id} could not be loaded: {error}",
                definition.Source));
    }
}
