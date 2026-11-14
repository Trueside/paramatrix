using Paramatrix.Gameplay;
using UnityEngine;

namespace Paramatrix.Unity
{
    /// <summary>
    /// A conversation pauses the world (Architecture 0.3 §2.2, Р10, T16): walking and world animations use scaled time
    /// and stop; UI and Scenario do not depend on time. Monologues keep the world running.
    /// </summary>
    public sealed class WorldPause : MonoBehaviour
    {
        private GameFlow _flow;

        public bool IsPaused => Mathf.Approximately(Time.timeScale, 0f);

        public void Bind(GameFlow flow)
        {
            _flow = flow;
            _flow.Changed += Apply;
            Apply();
        }

        private void Apply() => Time.timeScale = _flow.Presentation == DialoguePresentation.None ? 1f : 0f;

        private void OnDestroy()
        {
            if (_flow != null)
            {
                _flow.Changed -= Apply;
            }

            Time.timeScale = 1f; // never leave the editor or the next test paused
        }
    }
}
