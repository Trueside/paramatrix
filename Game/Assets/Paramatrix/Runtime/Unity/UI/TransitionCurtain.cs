using Paramatrix.Gameplay;
using UnityEngine;

namespace Paramatrix.Unity
{
    /// <summary>
    /// Covers the screen while a location transition is in progress (<see cref="GameFlow.InTransition"/>).
    /// A scene load takes several frames: the old scene is gone, the new one appears, the hero is placed at the
    /// entry point last. None of those intermediate frames may be seen. The transition stays instant (no fade).
    /// </summary>
    public sealed class TransitionCurtain : MonoBehaviour
    {
        [SerializeField]
        private GameObject curtain;

        private GameFlow _flow;

        public bool IsShown => curtain.activeSelf;

        public void Bind(GameFlow flow)
        {
            _flow = flow;
            _flow.Changed += Apply;
            Apply();
        }

        private void Apply() => curtain.SetActive(_flow.InTransition);

        private void OnDestroy()
        {
            if (_flow != null)
            {
                _flow.Changed -= Apply;
            }
        }
    }
}
