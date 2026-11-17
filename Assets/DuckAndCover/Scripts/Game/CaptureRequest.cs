using System;

namespace DuckAndCover.Game
{
    /// <summary>
    /// Tells the game it was started for a screenshot run. The editor's "Capture Screenshots"
    /// command sets this through EditorPrefs; a player build can pass -duckCapture &lt;folder&gt;.
    /// </summary>
    public static class CaptureRequest
    {
        public const string DirectoryKey = "DuckAndCover.CaptureDir";
        public const string ExitKey = "DuckAndCover.CaptureExit";

        /// <summary>True when the editor should quit after capturing (command-line runs).</summary>
        public static bool ExitWhenDone { get; private set; }

        /// <summary>Returns the output folder if a capture was requested, and clears the request.</summary>
        public static string Consume()
        {
#if UNITY_EDITOR
            string dir = UnityEditor.EditorPrefs.GetString(DirectoryKey, "");
            if (!string.IsNullOrEmpty(dir))
            {
                ExitWhenDone = UnityEditor.EditorPrefs.GetBool(ExitKey, false);
                UnityEditor.EditorPrefs.DeleteKey(DirectoryKey);
                UnityEditor.EditorPrefs.DeleteKey(ExitKey);
                return dir;
            }
#endif
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-duckCapture")
                {
                    ExitWhenDone = true;
                    return args[i + 1];
                }
            }
            return null;
        }
    }
}
