using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// Plays through the shot list, saves a PNG of each, and writes log.txt with every warning and
    /// error from the run. Lets Claude (or anyone) check the game's look without clicking through
    /// it. Started by "Duck and Cover > Capture Screenshots" or Tools/unity capture.
    /// When you add a screen or a scene worth checking, add a shot to <see cref="Shots"/>.
    /// </summary>
    public class ScreenshotDirector : MonoBehaviour
    {
        public string outputDirectory;

        const int Width = 1920;
        const int Height = 1080;
        const float TimeoutSeconds = 300f;

        /// <summary>One screenshot: set the game up, wait, then save name.png.</summary>
        public struct Shot
        {
            public string name;
            public Action setup;
            public float wait;

            public Shot(string name, Action setup, float wait) { this.name = name; this.setup = setup; this.wait = wait; }
        }

        /// <summary>The shot list, in order. Names start with a number so the files sort.</summary>
        static List<Shot> Shots() => new List<Shot>
        {
            new Shot("01-boot-room", null, 1.5f),
        };

        readonly List<string> messages = new List<string>();
        readonly List<string> saved = new List<string>();
        int errors;
        bool finished;

        void OnEnable() => Application.logMessageReceived += OnLog;
        void OnDisable() => Application.logMessageReceived -= OnLog;

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Log) return;
            if (type == LogType.Exception && IsEditorSearchIndexing(stack))
            {
                // Unity's own Search window indexer throws once on a project with a fresh Library.
                // It is not the game's, so list it without failing the run.
                messages.Add("[Editor, ignored] " + message);
                return;
            }
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++;
            messages.Add("[" + type + "] " + message + (type == LogType.Exception ? "\n" + stack : ""));
        }

        static bool IsEditorSearchIndexing(string stack) =>
            stack != null && stack.Contains("UnityEditor.Search.SearchInit") && !stack.Contains("DuckAndCover");

        IEnumerator Start()
        {
            StartCoroutine(Watchdog());
            Directory.CreateDirectory(outputDirectory);
            foreach (var old in Directory.GetFiles(outputDirectory, "*.png")) File.Delete(old);

            foreach (var shot in Shots())
            {
                try { shot.setup?.Invoke(); }
                catch (Exception ex) { Debug.LogException(ex); }
                yield return new WaitForSecondsRealtime(shot.wait);
                Save(shot.name);
                if (finished) yield break;
            }
            Finish("Done.");
        }

        IEnumerator Watchdog()
        {
            yield return new WaitForSecondsRealtime(TimeoutSeconds);
            if (finished) yield break;
            errors++;
            messages.Add("[Error] Screenshot run timed out after " + TimeoutSeconds + " seconds.");
            Finish("Timed out.");
        }

        void Save(string name)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("Duck and Cover: no main camera to capture " + name + ".");
                return;
            }
            var target = new RenderTexture(Width, Height, 24);
            var readback = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var previous = cam.targetTexture;
            try
            {
                cam.targetTexture = target;
                cam.Render();
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                readback.Apply();
                string path = Path.Combine(outputDirectory, name + ".png");
                File.WriteAllBytes(path, readback.EncodeToPNG());
                saved.Add(path);
            }
            finally
            {
                cam.targetTexture = previous;
                RenderTexture.active = null;
                target.Release();
                Destroy(target);
                Destroy(readback);
            }
        }

        void Finish(string outcome)
        {
            if (finished) return;
            finished = true;

            var report = new StringBuilder();
            report.AppendLine("Duck and Cover screenshot run: " + outcome);
            report.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine("Unity: " + Application.unityVersion + ", graphics: " + SystemInfo.graphicsDeviceType);
            report.AppendLine("Screenshots: " + saved.Count);
            foreach (var p in saved) report.AppendLine("  " + Path.GetFileName(p));
            report.AppendLine("Errors: " + errors + ", warnings and errors logged: " + messages.Count);
            foreach (var m in messages) report.AppendLine(m);
            try { File.WriteAllText(Path.Combine(outputDirectory, "log.txt"), report.ToString()); }
            catch (Exception ex) { Debug.LogWarning("Duck and Cover: could not write capture log. " + ex.Message); }
            Debug.Log("Duck and Cover: saved " + saved.Count + " screenshots to " + outputDirectory + " (" + errors + " errors).");

#if UNITY_EDITOR
            if (CaptureRequest.ExitWhenDone) UnityEditor.EditorApplication.Exit(errors > 0 ? 1 : 0);
            else UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(errors > 0 ? 1 : 0);
#endif
        }
    }
}
