using System;
using Paramatrix.Content;
using Paramatrix.Scenario;

namespace Paramatrix.Gameplay
{
    public enum ControlOwner
    {
        Player = 1,
        Scenario,
        System,
    }

    /// <summary>Monologues are not a presentation: they never take control (Р10).</summary>
    public enum DialoguePresentation
    {
        None = 1,

        /// <summary>A conversation: the world pauses, only advancing the dialogue is allowed.</summary>
        Normal,

        /// <summary>A novel (Р12): as a conversation, on its own dreamy screen with the partner in the middle.</summary>
        Novel,
    }

    [Flags]
    public enum PlayerPermissions
    {
        None = 0,
        Move = 1,
        Interact = 2,
        Inventory = 4,
        SaveMenu = 8,
        AdvanceDialogue = 16,
    }

    public enum SystemOperation
    {
        Boot = 1,
        NewGame,
        LoadGame,
    }

    public readonly struct SystemToken : IEquatable<SystemToken>
    {
        internal SystemToken(int value, SystemOperation operation)
        {
            Value = value;
            Operation = operation;
        }

        public int Value { get; }

        public SystemOperation Operation { get; }

        public bool Equals(SystemToken other) => Value == other.Value;

        public override bool Equals(object obj) => obj is SystemToken other && Equals(other);

        public override int GetHashCode() => Value;
    }

    /// <summary>
    /// Who controls the game and what the player may do (Architecture 0.3 §1, T16). Three independent dimensions:
    /// control owner, dialogue presentation, transition in progress. Permissions are derived from them;
    /// there is no global "input locked" flag. Starts owned by System (boot) until the session is ready.
    /// </summary>
    public sealed class GameFlow : IGameFlowPort
    {
        private readonly IDiagnosticsSink _diagnostics;
        private ScenarioRuntime _scenario;
        private ControlToken? _scenarioToken;
        private SystemToken? _systemToken;
        private int _nextToken = 1;

        public GameFlow(IDiagnosticsSink diagnostics)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            Owner = ControlOwner.System;
            BootToken = new SystemToken(_nextToken++, SystemOperation.Boot);
            _systemToken = BootToken;
        }

        public ControlOwner Owner { get; private set; }

        public DialoguePresentation Presentation { get; private set; } = DialoguePresentation.None;

        public bool InTransition { get; private set; }

        /// <summary>The system operation the game starts in; ended by <see cref="GameSession"/> when ready.</summary>
        public SystemToken BootToken { get; }

        public PlayerPermissions Permissions
        {
            get
            {
                if (Owner == ControlOwner.Player && !InTransition && Presentation == DialoguePresentation.None)
                {
                    return PlayerPermissions.Move | PlayerPermissions.Interact | PlayerPermissions.Inventory | PlayerPermissions.SaveMenu;
                }

                return Presentation != DialoguePresentation.None && !InTransition ? PlayerPermissions.AdvanceDialogue : PlayerPermissions.None;
            }
        }

        /// <summary>Raised after any dimension changed; the input layer and UI re-read <see cref="Permissions"/>.</summary>
        public event Action Changed;

        /// <summary>Raised at the start of <see cref="EnterRecovery"/> so presentation systems drop their state.</summary>
        public event Action Recovering;

        internal void Attach(ScenarioRuntime scenario) => _scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));

        // ---------- Scenario control ----------

        public bool TryAcquireScenarioControl(out ControlToken token)
        {
            token = default;
            if (Owner != ControlOwner.Player || InTransition)
            {
                return false;
            }

            token = new ControlToken(_nextToken++);
            _scenarioToken = token;
            Owner = ControlOwner.Scenario;
            Changed?.Invoke();
            return true;
        }

        public void ReleaseScenarioControl(ControlToken token, ReleaseReason reason)
        {
            if (!_scenarioToken.HasValue || !_scenarioToken.Value.Equals(token))
            {
                throw new InvalidOperationException("Scenario released control it does not hold.");
            }

            _scenarioToken = null;
            Owner = _systemToken.HasValue ? ControlOwner.System : ControlOwner.Player;
            Changed?.Invoke();
        }

        // ---------- System operations (boot, new game, load) ----------

        public SystemToken BeginSystemOperation(SystemOperation operation)
        {
            if (Owner == ControlOwner.Scenario)
            {
                throw new InvalidOperationException("A system operation cannot start while a scene holds control; recover first.");
            }

            if (_systemToken.HasValue)
            {
                throw new InvalidOperationException($"System operation {_systemToken.Value.Operation} is already in progress.");
            }

            var token = new SystemToken(_nextToken++, operation);
            _systemToken = token;
            Owner = ControlOwner.System;
            Changed?.Invoke();
            return token;
        }

        public void EndSystemOperation(SystemToken token)
        {
            if (!_systemToken.HasValue || !_systemToken.Value.Equals(token))
            {
                throw new InvalidOperationException("Ended a system operation that is not in progress.");
            }

            _systemToken = null;
            Owner = ControlOwner.Player;
            Changed?.Invoke();
        }

        // ---------- presentation dimensions ----------

        public void SetPresentation(DialoguePresentation presentation)
        {
            Presentation = presentation;
            Changed?.Invoke();
        }

        public void SetTransition(bool active)
        {
            InTransition = active;
            Changed?.Invoke();
        }

        /// <summary>
        /// Safe return of control after a hung or broken execution (0.3 §1.3, T7): presentation systems reset,
        /// the active chain is aborted (its events become Failed), queued requests are dropped, the player gets control.
        /// The cause is reported as an Error (Р0).
        /// </summary>
        public void EnterRecovery(string reason)
        {
            if (_scenario == null)
            {
                throw new InvalidOperationException("GameFlow is not attached to a scenario runtime.");
            }

            _diagnostics.Report(new Diagnostic(DiagnosticCode.RecoveryEntered, Severity.Error, "Recovery: " + reason, SourceLocation.None));
            Recovering?.Invoke();
            _scenario.AbortActiveChain(reason);
            _scenario.ClearPendingRequests();

            _scenarioToken = null;
            Presentation = DialoguePresentation.None;
            InTransition = false;
            Owner = _systemToken.HasValue ? ControlOwner.System : ControlOwner.Player;
            Changed?.Invoke();
        }
    }
}
