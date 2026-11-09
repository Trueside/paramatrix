using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Paramatrix.Unity.Editor
{
    /// <summary>
    /// The game always starts from the Bootstrap scene (persistent systems + additive location scenes, Architecture 0.3 §3.2).
    /// Pressing Play with any other scene open would show that scene alone and never start the game, so on entering
    /// Play mode without a <see cref="GameBootstrap"/> the editor switches to the first enabled Build Settings scene.
    /// Test runs are left alone: they start from the test framework's own init scene and load scenes themselves.
    /// </summary>
    [InitializeOnLoad]
    public static class PlayFromBootstrap
    {
        private const string TestInitScenePrefix = "InitTestScene";

        static PlayFromBootstrap()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        /// <summary>The scene Play starts from: the first enabled Build Settings scene, or null if there is none.</summary>
        public static SceneAsset StartScene()
        {
            var first = EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled);
            return first == null ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(first.path);
        }

        /// <summary>Whether Play mode, as it is now, needs to be redirected to the start scene.</summary>
        public static bool NeedsRedirect(string activeSceneName, bool hasBootstrap) =>
            !hasBootstrap && !activeSceneName.StartsWith(TestInitScenePrefix);

        /// <summary>
        /// Frame rate cap for Play mode in the editor: the monitor refresh rate, as vertical sync gives a build.
        /// The Game view does not apply the quality level's vSync, so without a cap the editor renders hundreds of
        /// frames per second and behaves unlike the game. Null when the refresh rate is unknown (batch mode).
        /// </summary>
        public static int? EditorFrameRateCap(double refreshRate) =>
            refreshRate >= 1 ? (int)System.Math.Round(refreshRate) : (int?)null;

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode)
            {
                Application.targetFrameRate = -1;
                return;
            }

            if (change != PlayModeStateChange.EnteredPlayMode)
            {
                return;
            }

            var isTestRun = SceneManager.GetActiveScene().name.StartsWith(TestInitScenePrefix);
            var cap = EditorFrameRateCap(Screen.currentResolution.refreshRateRatio.value);
            if (!isTestRun && cap.HasValue)
            {
                Application.targetFrameRate = cap.Value;
            }

            var hasBootstrap = Object.FindAnyObjectByType<GameBootstrap>() != null;
            if (!NeedsRedirect(SceneManager.GetActiveScene().name, hasBootstrap))
            {
                return;
            }

            var start = StartScene();
            if (start == null)
            {
                Debug.LogError("[Paramatrix] No enabled scene in Build Settings: run Paramatrix > Build Demo Scenes.");
                return;
            }

            Debug.Log($"[Paramatrix] '{SceneManager.GetActiveScene().name}' is not the game's start scene; starting from '{start.name}'.");
            SceneManager.LoadScene(start.name, LoadSceneMode.Single);
        }
    }
}
