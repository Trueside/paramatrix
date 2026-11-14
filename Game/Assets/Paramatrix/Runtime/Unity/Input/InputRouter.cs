using Paramatrix.Gameplay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Paramatrix.Unity
{
    /// <summary>
    /// Input System → game (Architecture 0.3 §5.3): keyboard walks, left click = primary, right click = inspect,
    /// any key or mouse button advances dialogue. Every action is gated by <see cref="GameFlow.Permissions"/>; this class
    /// never locks input itself and contains no game rules. Public methods are the same entry points tests use.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class InputRouter : MonoBehaviour
    {
        [SerializeField]
        private Camera worldCamera;

        [SerializeField]
        [Tooltip("Clicks over UI (inventory) never reach the world.")]
        private EventSystem eventSystem;

        private InputAction _move;
        private InputAction _primary;
        private InputAction _secondary;
        private InputAction _advance;
        private GameSession _session;
        private HeroController _hero;
        private RepresentationRegistry _representations;
        private DialogueView _dialogue;

        public void Bind(GameSession session, HeroController hero, RepresentationRegistry representations, DialogueView dialogue)
        {
            _session = session;
            _hero = hero;
            _representations = representations;
            _dialogue = dialogue;
        }

        private void OnEnable()
        {
            _move = new InputAction("move");
            _move.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/a").With("Positive", "<Keyboard>/d");
            _move.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/leftArrow").With("Positive", "<Keyboard>/rightArrow");
            _primary = new InputAction("primary", binding: "<Mouse>/leftButton");
            _secondary = new InputAction("secondary", binding: "<Mouse>/rightButton");
            // Any key or mouse button advances a dialogue: the player must never be stuck on a line.
            _advance = new InputAction("advance");
            _advance.AddBinding("<Keyboard>/anyKey");
            _advance.AddBinding("<Mouse>/leftButton");
            _advance.AddBinding("<Mouse>/rightButton");
            _advance.AddBinding("<Mouse>/middleButton");
            _move.Enable();
            _primary.Enable();
            _secondary.Enable();
            _advance.Enable();
        }

        private void OnDisable()
        {
            _move.Dispose();
            _primary.Dispose();
            _secondary.Dispose();
            _advance.Dispose();
        }

        private void Update()
        {
            if (_session == null)
            {
                return;
            }

            var permissions = _session.Flow.Permissions;
            if ((permissions & PlayerPermissions.AdvanceDialogue) != 0)
            {
                if (_session.Dialogue.IsChoosing)
                {
                    ReadChoice(); // a choice is answered only by taking an option: other keys do nothing
                }
                else if (_advance.WasPressedThisFrame())
                {
                    AdvanceDialogue();
                }

                return;
            }

            Walk(_move.ReadValue<float>(), Time.deltaTime);

            var pointer = Mouse.current;
            if (pointer == null || worldCamera == null)
            {
                return;
            }

            if ((_primary.WasPressedThisFrame() || _secondary.WasPressedThisFrame()) && !IsPointerOverUi())
            {
                var screen = pointer.position.ReadValue();
                var world = worldCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -worldCamera.transform.position.z));
                ClickAt(world, _primary.WasPressedThisFrame() ? PointerButton.Primary : PointerButton.Secondary);
            }
        }

        /// <summary>Keyboard walking: the only way the hero gets somewhere (Р11).</summary>
        public void Walk(float direction, float deltaTime)
        {
            if (_session == null || (_session.Flow.Permissions & PlayerPermissions.Move) == 0 || Mathf.Approximately(direction, 0f))
            {
                return;
            }

            _hero.Walk(direction, deltaTime);
        }

        /// <summary>A click at a world position: the topmost shown representation under it, or empty space.</summary>
        public void ClickAt(Vector2 world, PointerButton button)
        {
            if (_session == null)
            {
                return;
            }

            var view = ViewAt(world);
            if (view != null)
            {
                _session.Interaction.ClickWorld(view.Id, button);
            }
            else
            {
                _session.Interaction.ClickEmpty(button);
            }
        }

        /// <summary>The hero's choice (Р12): the option's number key (1..9, main row or keypad) or a left click on it.</summary>
        private void ReadChoice()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                for (var i = 0; i < 9; i++)
                {
                    if (keyboard[Key.Digit1 + i].wasPressedThisFrame || keyboard[Key.Numpad1 + i].wasPressedThisFrame)
                    {
                        ChooseOption(i);
                        return;
                    }
                }
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && _dialogue != null)
            {
                var option = _dialogue.ChoiceAt(mouse.position.ReadValue());
                if (option >= 0)
                {
                    ChooseOption(option);
                }
            }
        }

        /// <summary>Takes option <paramref name="index"/> (from 0) of the hero's choice; false when there is no such option.</summary>
        public bool ChooseOption(int index) => _session != null && _session.Dialogue.Choose(index);

        /// <summary>While the line is being typed, the first press shows it whole (Р10); the next one advances.</summary>
        public void AdvanceDialogue()
        {
            if (_dialogue != null && _dialogue.IsTyping)
            {
                _dialogue.CompleteTyping();
                return;
            }

            _session?.Dialogue.Advance();
        }

        private bool IsPointerOverUi() => eventSystem != null && eventSystem.IsPointerOverGameObject();

        /// <summary>The topmost shown, registered representation under a world point; null for empty space.</summary>
        public EntityView ViewAt(Vector2 world)
        {
            // Hit test against sprite bounds of registered views: no physics, so positions are never stale.
            EntityView best = null;
            var bestOrder = int.MinValue;
            foreach (var view in _representations.AllViews)
            {
                if (!view.IsShown || !view.Contains(world))
                {
                    continue;
                }

                // The hero is drawn in front of everything; he is never the target of his own clicks.
                if (view is CharacterView character && character.IsPlayer)
                {
                    continue;
                }

                var order = view.SortingOrder;
                if (best == null || order > bestOrder)
                {
                    best = view;
                    bestOrder = order;
                }
            }

            return best;
        }
    }
}
