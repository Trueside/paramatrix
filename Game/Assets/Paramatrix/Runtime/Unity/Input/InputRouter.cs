using Paramatrix.Gameplay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Paramatrix.Unity
{
    /// <summary>
    /// Input System → game (Architecture 0.3 §5.3): keyboard walks, left click = primary, right click = inspect,
    /// click / Space advances dialogue. Every action is gated by <see cref="GameFlow.Permissions"/>; this class
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

        public void Bind(GameSession session, HeroController hero, RepresentationRegistry representations)
        {
            _session = session;
            _hero = hero;
            _representations = representations;
        }

        private void OnEnable()
        {
            _move = new InputAction("move");
            _move.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/a").With("Positive", "<Keyboard>/d");
            _move.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/leftArrow").With("Positive", "<Keyboard>/rightArrow");
            _primary = new InputAction("primary", binding: "<Mouse>/leftButton");
            _secondary = new InputAction("secondary", binding: "<Mouse>/rightButton");
            _advance = new InputAction("advance", binding: "<Keyboard>/space");
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
                if (_advance.WasPressedThisFrame() || _primary.WasPressedThisFrame())
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

        /// <summary>Keyboard walking; walking during an approach cancels the pending interaction (T13).</summary>
        public void Walk(float direction, float deltaTime)
        {
            if (_session == null || (_session.Flow.Permissions & PlayerPermissions.Move) == 0 || Mathf.Approximately(direction, 0f))
            {
                return;
            }

            if (_session.Interaction.IsApproaching)
            {
                _session.Interaction.CancelApproach();
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

        public void AdvanceDialogue() => _session?.Dialogue.Advance();

        private bool IsPointerOverUi() => eventSystem != null && eventSystem.IsPointerOverGameObject();

        private EntityView ViewAt(Vector2 world)
        {
            EntityView best = null;
            var bestOrder = int.MinValue;
            foreach (var hit in Physics2D.OverlapPointAll(world))
            {
                var view = hit.GetComponentInParent<EntityView>();
                if (view == null || !view.IsShown || _representations.Find(view.Id) != view)
                {
                    continue;
                }

                var sprite = view.GetComponentInChildren<SpriteRenderer>();
                var order = sprite == null ? 0 : sprite.sortingOrder;
                if (order > bestOrder)
                {
                    best = view;
                    bestOrder = order;
                }
            }

            return best;
        }
    }
}
