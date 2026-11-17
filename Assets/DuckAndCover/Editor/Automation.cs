using System;
using System.IO;
using DuckAndCover.Game;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DuckAndCover.EditorTools
{
    /// <summary>
    /// Entry points for running the project without clicking through the editor, either from the
    /// "Duck and Cover" menu or from the command line with -executeMethod (see Tools/unity.ps1 and
    /// Tools/unity.sh). Claude uses these to check its own work.
    /// </summary>
    public static class Automation
    {
        const string DefaultCaptureDir = "Screenshots";

        [MenuItem("Duck and Cover/Capture Screenshots", priority = 40)]
        static void CaptureFromMenu() => StartCapture(DefaultCaptureDir, false);

        [MenuItem("Duck and Cover/Rebuild Scenes", priority = 41)]
        static void ScenesFromMenu() => SceneSetup.Build();

        /// <summary>
        /// -executeMethod DuckAndCover.EditorTools.Automation.CaptureScreenshots [-captureDir folder]
        /// Plays the Boot scene, saves a PNG of every shot plus log.txt, then quits
        /// (exit code 1 if anything logged an error).
        /// </summary>
        public static void CaptureScreenshots() =>
            StartCapture(Argument("-captureDir") ?? DefaultCaptureDir, Application.isBatchMode);

        /// <summary>-executeMethod DuckAndCover.EditorTools.Automation.CreateScenes</summary>
        public static void CreateScenes()
        {
            SceneSetup.Build();
            QuitIfBatch(0);
        }

        /// <summary>
        /// -executeMethod DuckAndCover.EditorTools.Automation.BuildSmokePlayer [-smokeBuild path]
        /// Builds a development player for the network smoke test (Tools/unity netsmoke runs it twice).
        /// </summary>
        public static void BuildSmokePlayer()
        {
            SceneSetup.EnsureScenes();
            var target = EditorUserBuildSettings.activeBuildTarget;
            string fallback = "Builds/NetSmoke/DuckAndCover" + (target == BuildTarget.StandaloneWindows64 || target == BuildTarget.StandaloneWindows ? ".exe" : "");
            var options = new BuildPlayerOptions
            {
                scenes = new[] { SceneSetup.BootPath },
                locationPathName = Argument("-smokeBuild") ?? fallback,
                target = target,
                options = BuildOptions.Development,
            };
            var report = BuildPipeline.BuildPlayer(options);
            bool ok = report != null && report.summary.result == BuildResult.Succeeded;
            Debug.Log("Duck and Cover: smoke player build " + (ok ? "succeeded: " : "FAILED: ") + options.locationPathName);
            QuitIfBatch(ok ? 0 : 1);
        }

        static void StartCapture(string folder, bool exitWhenDone)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("Duck and Cover: stop Play mode before capturing screenshots.");
                return;
            }
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            SceneSetup.EnsureScenes();
            EditorSceneManager.OpenScene(SceneSetup.BootPath);

            string dir = Path.GetFullPath(folder);
            EditorPrefs.SetString(CaptureRequest.DirectoryKey, dir);
            EditorPrefs.SetBool(CaptureRequest.ExitKey, exitWhenDone);
            Debug.Log("Duck and Cover: capturing screenshots to " + dir);
            EditorApplication.EnterPlaymode();
        }

        static void QuitIfBatch(int code)
        {
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
