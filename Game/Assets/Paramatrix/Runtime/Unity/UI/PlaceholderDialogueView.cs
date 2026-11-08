using Paramatrix.Gameplay;
using UnityEngine;

namespace Paramatrix.Unity
{
    /// <summary>
    /// Temporary dialogue presentation for stage 9: one text box at the bottom of the screen.
    /// Replaced by speech bubbles and the VN panel in stage 10.
    /// </summary>
    public sealed class PlaceholderDialogueView : MonoBehaviour, IDialogueView
    {
        private DialogueLineView? _line;

        public DialogueLineView? CurrentLine => _line;

        public void Show(DialogueLineView line) => _line = line;

        public void Hide() => _line = null;

        private void OnGUI()
        {
            if (!_line.HasValue)
            {
                return;
            }

            var line = _line.Value;
            var height = 90f;
            var rect = new Rect(20, Screen.height - height - 20, Screen.width - 40, height);
            GUI.Box(rect, GUIContent.none);
            GUI.Label(new Rect(rect.x + 12, rect.y + 8, rect.width - 24, rect.height - 16), $"<b>{line.SpeakerName}</b>\n{line.Text}\n<i>(click / Space)</i>",
                new GUIStyle(GUI.skin.label) { richText = true, fontSize = 18, wordWrap = true });
        }
    }
}
