using System.Text;
using Paramatrix.Content;
using UnityEngine;

namespace Paramatrix.Unity
{
    /// <summary>
    /// Shows runtime errors on top of the game in the Editor and Development builds (T20, Р0):
    /// a broken scene must be visible, not only in the Console. Release behaviour is decided later.
    /// </summary>
    public sealed class DiagnosticsOverlay : MonoBehaviour
    {
        private const int MaxShown = 8;

        private UnityDiagnostics _diagnostics;
        private Vector2 _scroll;
        private bool _collapsed;

        public void Bind(UnityDiagnostics diagnostics) => _diagnostics = diagnostics;

        public int ErrorCount
        {
            get
            {
                var count = 0;
                if (_diagnostics != null)
                {
                    foreach (var item in _diagnostics.Items)
                    {
                        if (item.Severity == Severity.Error)
                        {
                            count++;
                        }
                    }
                }

                return count;
            }
        }

        private void OnGUI()
        {
            if (!Debug.isDebugBuild || _diagnostics == null || ErrorCount == 0)
            {
                return;
            }

            var width = Mathf.Min(Screen.width - 20, 900);
            GUILayout.BeginArea(new Rect(10, 10, width, _collapsed ? 30 : Screen.height * 0.45f), GUI.skin.box);
            if (GUILayout.Button($"Paramatrix: {ErrorCount} error(s) — {(_collapsed ? "show" : "hide")}"))
            {
                _collapsed = !_collapsed;
            }

            if (!_collapsed)
            {
                _scroll = GUILayout.BeginScrollView(_scroll);
                var shown = 0;
                for (var i = _diagnostics.Items.Count - 1; i >= 0 && shown < MaxShown; i--)
                {
                    var item = _diagnostics.Items[i];
                    if (item.Severity != Severity.Error)
                    {
                        continue;
                    }

                    var text = new StringBuilder();
                    text.Append(item.Code).Append(": ").Append(item.Message);
                    if (item.Location.Source != null)
                    {
                        text.Append("\n   at ").Append(item.Location);
                    }

                    GUILayout.Label(text.ToString());
                    shown++;
                }

                GUILayout.EndScrollView();
            }

            GUILayout.EndArea();
        }
    }
}
