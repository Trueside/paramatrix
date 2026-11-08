using Paramatrix.Content;
using Paramatrix.Gameplay;
using TMPro;
using UnityEngine;
using EntityId = Paramatrix.Content.EntityId;

namespace Paramatrix.Unity
{
    /// <summary>
    /// Shows dialogue lines (Architecture 0.3 §2.2): Minimalistic — a speech bubble above the speaker;
    /// Visual Novel — a panel with the speaker's name. Advancing is input's job (click / Space).
    /// </summary>
    public sealed class DialogueView : MonoBehaviour, IDialogueView
    {
        [SerializeField]
        private Camera worldCamera;

        [SerializeField]
        private RectTransform bubble;

        [SerializeField]
        private TextMeshProUGUI bubbleText;

        [SerializeField]
        private RectTransform novelPanel;

        [SerializeField]
        private TextMeshProUGUI novelName;

        [SerializeField]
        private TextMeshProUGUI novelText;

        [SerializeField]
        [Tooltip("Gap between the top of the speaker and the bubble, in world units.")]
        private float bubbleOffset = 0.35f;

        private RepresentationRegistry _representations;
        private IDiagnosticsSink _diagnostics;
        private DialogueLineView? _line;

        public DialogueLineView? CurrentLine => _line;

        public bool IsBubbleShown => bubble.gameObject.activeSelf;

        public bool IsNovelPanelShown => novelPanel.gameObject.activeSelf;

        public string BubbleText => bubbleText.text;

        public string NovelName => novelName.text;

        public string NovelText => novelText.text;

        /// <summary>Screen position of the bubble's anchor (bottom centre).</summary>
        public Vector2 BubbleScreenPosition => bubble.position;

        public void Bind(RepresentationRegistry representations, IDiagnosticsSink diagnostics, TMP_FontAsset font)
        {
            _representations = representations;
            _diagnostics = diagnostics;
            bubbleText.font = font;
            novelName.font = font;
            novelText.font = font;
            Hide();
        }

        public void Show(DialogueLineView line)
        {
            _line = line;
            if (line.Mode == DialogueMode.VisualNovel)
            {
                bubble.gameObject.SetActive(false);
                novelPanel.gameObject.SetActive(true);
                novelName.text = line.SpeakerName;
                novelText.text = line.Text;
                return;
            }

            novelPanel.gameObject.SetActive(false);
            bubble.gameObject.SetActive(true);
            bubbleText.text = line.Text;
            if (SpeakerView(line.Speaker) == null)
            {
                // DialogueSystem already checked the speaker is in the location; a missing view is a scene defect.
                _diagnostics.Report(new Diagnostic(DiagnosticCode.UnknownReference, Severity.Error,
                    $"{line.Speaker} speaks in a bubble but has no shown representation in the scene.", SourceLocation.None));
            }

            PlaceBubble();
        }

        public void Hide()
        {
            _line = null;
            bubble.gameObject.SetActive(false);
            novelPanel.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_line.HasValue && _line.Value.Mode == DialogueMode.Minimal)
            {
                PlaceBubble(); // follows the speaker
            }
        }

        private void PlaceBubble()
        {
            var view = _line.HasValue ? SpeakerView(_line.Value.Speaker) : null;
            if (view == null)
            {
                bubble.position = new Vector2(Screen.width / 2f, Screen.height * 0.75f);
                return;
            }

            var renderer = view.GetComponentInChildren<SpriteRenderer>();
            var top = renderer != null
                ? new Vector3(renderer.bounds.center.x, renderer.bounds.max.y + bubbleOffset, 0f)
                : view.transform.position + (Vector3.up * bubbleOffset);
            bubble.position = worldCamera.WorldToScreenPoint(top);
        }

        private EntityView SpeakerView(EntityId speaker)
        {
            var view = _representations?.Find(speaker);
            return view != null && view.IsShown ? view : null;
        }
    }
}
