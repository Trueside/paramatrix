using System.Linq;
using Paramatrix.Content;
using Paramatrix.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Paramatrix.Unity
{
    /// <summary>
    /// Quick save (F5) and quick load (F9) into one slot, with a short status line. Slots and a menu are later work
    /// (T21); refusals and failures are always shown, never silent (Р0).
    /// </summary>
    public sealed class SaveControls : MonoBehaviour
    {
        public const string QuickSlot = "quicksave";

        [SerializeField]
        private TextMeshProUGUI status;

        [SerializeField]
        private float statusSeconds = 2.5f;

        private InputAction _save;
        private InputAction _load;
        private GameSession _session;
        private IDiagnosticsSink _diagnostics;
        private float _hideAt;

        public string Status => status.gameObject.activeSelf ? status.text : null;

        public void Bind(GameSession session, IDiagnosticsSink diagnostics, TMP_FontAsset font)
        {
            _session = session;
            _diagnostics = diagnostics;
            status.font = font;
            status.gameObject.SetActive(false);
        }

        /// <summary>Same entry point as F5.</summary>
        public SaveLoadResult QuickSave(string slot = QuickSlot)
        {
            if ((_session.Flow.Permissions & PlayerPermissions.SaveMenu) == 0 || !_session.Save.CanSave)
            {
                Show("Сейчас сохранить нельзя.");
                return null;
            }

            var result = _session.Save.Save(slot);
            Report(result, "Игра сохранена.", "Не удалось сохранить.");
            return result;
        }

        /// <summary>Same entry point as F9. The result arrives when the location scene is loaded.</summary>
        public void QuickLoad(string slot = QuickSlot, System.Action<SaveLoadResult> done = null)
        {
            if ((_session.Flow.Permissions & PlayerPermissions.SaveMenu) == 0)
            {
                Show("Сейчас загрузить нельзя.");
                return;
            }

            _session.LoadGame(slot, result =>
            {
                Report(result, "Игра загружена.", "Не удалось загрузить.");
                done?.Invoke(result);
            });
        }

        private void OnEnable()
        {
            _save = new InputAction("quick_save", binding: "<Keyboard>/f5");
            _load = new InputAction("quick_load", binding: "<Keyboard>/f9");
            _save.Enable();
            _load.Enable();
        }

        private void OnDisable()
        {
            _save.Dispose();
            _load.Dispose();
        }

        private void Update()
        {
            if (_session != null)
            {
                if (_save.WasPressedThisFrame())
                {
                    QuickSave();
                }
                else if (_load.WasPressedThisFrame())
                {
                    QuickLoad();
                }
            }

            if (status.gameObject.activeSelf && Time.unscaledTime >= _hideAt)
            {
                status.gameObject.SetActive(false);
            }
        }

        private void Report(SaveLoadResult result, string success, string failure)
        {
            if (result.Succeeded)
            {
                Show(success);
                return;
            }

            foreach (var diagnostic in result.Diagnostics)
            {
                _diagnostics.Report(diagnostic); // details go to the error overlay
            }

            Show(failure + " " + result.Diagnostics.First().Message);
        }

        private void Show(string text)
        {
            status.text = text;
            status.gameObject.SetActive(true);
            _hideAt = Time.unscaledTime + statusSeconds;
        }
    }
}
