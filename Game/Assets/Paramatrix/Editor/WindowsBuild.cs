using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Paramatrix.Unity.Editor
{
    /// <summary>
    /// The Windows build of the game (Windows 10+, 64-bit, Mono) by the Build Profile <see cref="ProfilePath"/>: its
    /// settings and scene list (the profile may keep the Build Settings list) apply, the result goes to
    /// <see cref="Folder"/>/<see cref="Executable"/>. The same build as the profile's Build button, with two guards: the
    /// project is validated first, and a build of a project with errors is refused (Р0). Menu "Paramatrix → Build Windows", or in batch mode:
    /// Unity -executeMethod Paramatrix.Unity.Editor.WindowsBuild.BuildBatch (exit code 1 on failure).
    /// </summary>
    public static class WindowsBuild
    {
        /// <summary>Next to Assets, ignored by git (Game/Builds). Emptied before every build: no files of an older build stay.</summary>
        public const string Folder = "Builds/Windows";

        public const string Executable = "Paramatrix.exe";

        /// <summary>The Windows Build Profile (File → Build Profiles): build settings live there, not in this code.</summary>
        public const string ProfilePath = "Assets/Settings/Build Profiles/Windows.asset";

        [MenuItem("Paramatrix/Build Windows")]
        public static void BuildFromMenu() => Build(development: false);

        [MenuItem("Paramatrix/Build Windows (Development)")]
        public static void BuildDevelopmentFromMenu() => Build(development: true);

        public static void BuildBatch() => EditorApplication.Exit(Build(development: false) ? 0 : 1);

        /// <summary>True when the build succeeded; every refusal and failure is logged as an error.</summary>
        public static bool Build(bool development)
        {
            var report = ProjectValidator.ValidateProject();
            if (report.ErrorCount > 0)
            {
                ProjectValidator.Print(report);
                Debug.LogError($"[Paramatrix] Windows build refused: Validate Project found {report.ErrorCount} error(s).");
                return false;
            }

            var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(ProfilePath);
            if (profile == null)
            {
                Debug.LogError($"[Paramatrix] Windows build refused: no Build Profile at {ProfilePath} (File → Build Profiles → Add Build Profile → Windows).");
                return false;
            }

            var scenes = profile.GetScenesForBuild().Where(s => s.enabled).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[Paramatrix] Windows build refused: the profile has no scenes (run Paramatrix → Build Scenes).");
                return false;
            }

            // Only what is inside: the folder itself may be open in Explorer, and then it cannot be deleted.
            if (Directory.Exists(Folder))
            {
                foreach (var directory in Directory.GetDirectories(Folder))
                {
                    Directory.Delete(directory, recursive: true);
                }

                foreach (var file in Directory.GetFiles(Folder))
                {
                    File.Delete(file);
                }
            }

            var options = new BuildPlayerWithProfileOptions
            {
                buildProfile = profile,
                locationPathName = Path.Combine(Folder, Executable),
                options = development ? BuildOptions.Development : BuildOptions.None,
            };

            var result = BuildPipeline.BuildPlayer(options);
            var summary = result.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[Paramatrix] Windows build {summary.result}: {summary.totalErrors} error(s). See the build log above.");
                return false;
            }

            Debug.Log($"[Paramatrix] Windows build: {Path.GetFullPath(options.locationPathName)} ({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:F0} s, {scenes.Length} scenes).");
            return true;
        }
    }
}
