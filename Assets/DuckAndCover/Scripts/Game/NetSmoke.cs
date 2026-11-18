using System;
using System.Collections.Generic;
using System.IO;
using DuckAndCover.Core;
using DuckAndCover.Net;
using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// The network smoke test inside a built game. Tools/unity netsmoke builds a player and starts it
    /// twice with no graphics: one hosts, one joins, and scripted players run a whole match at high
    /// speed. Each writes what it ended up believing (result, score, zones, players and levels) to a
    /// file; the tool fails unless the two files are identical and neither logged an error.
    ///
    ///   -duckSmoke host|join      which side this instance plays
    ///   -duckSmokeOut file        where to write the result
    ///   -duckPlayers n            the host starts the match once n players are connected (default 2)
    ///   -duckMode id              capture_hold, king_of_the_hill, flag_rush or overrun (Breakwater's lane, waves, camps and Bastions)
    ///   -duckAddress a, -duckPort p, -duckSpeed x
    /// </summary>
    public class NetSmoke : MonoBehaviour
    {
        const float TimeoutSeconds = 240f;
        const float LingerSeconds = 3f;

        string role;
        string outFile;
        ScriptedPlayer script;
        SessionClient scriptedFor;
        readonly List<string> errors = new List<string>();
        float started;
        float finishedAt = -1f;
        bool done;

        /// <summary>Starts a smoke run if the command line asks for one. Returns true if it did.</summary>
        public static bool TryStart(GameObject owner)
        {
            string role = Arg("-duckSmoke");
            if (role != "host" && role != "join") return false;
            var smoke = owner.AddComponent<NetSmoke>();
            smoke.role = role;
            smoke.outFile = Arg("-duckSmokeOut") ?? ("netsmoke-" + role + ".txt");
            return true;
        }

        public static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        static int IntArg(string name, int fallback) => int.TryParse(Arg(name), out int v) ? v : fallback;

        static float FloatArg(string name, float fallback) =>
            float.TryParse(Arg(name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;

        void OnEnable() => Application.logMessageReceived += OnLog;
        void OnDisable() => Application.logMessageReceived -= OnLog;

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add("[" + type + "] " + message);
        }

        void Start()
        {
            started = Time.realtimeSinceStartup;
            Application.targetFrameRate = 120;
            var runner = SessionRunner.Ensure();
            runner.BuildMaps = false; // scripted play has its own stand-in layout, and nothing is drawn
            int port = IntArg("-duckPort", runner.Settings.defaultPort);
            if (role == "host")
            {
                runner.Speed = FloatArg("-duckSpeed", 20f);
                runner.Host(Arg("-duckMode") ?? "king_of_the_hill", "Smoke host", false, port, scripted: true);
            }
            else
            {
                runner.Join(Arg("-duckAddress") ?? "127.0.0.1", port, "Smoke client");
            }
        }

        void Update()
        {
            if (done) return;
            var runner = SessionRunner.Instance;
            var session = runner == null ? null : runner.Session;
            if (session == null) return;

            if (session.State == NetState.Failed)
            {
                // The host going away after the match is the normal end for a client that already has its result.
                if (finishedAt >= 0f) { Finish(); return; }
                errors.Add("[Error] Network: " + session.Error);
                Finish();
                return;
            }
            if (Time.realtimeSinceStartup - started > TimeoutSeconds)
            {
                errors.Add("[Error] Timed out after " + TimeoutSeconds + " seconds in state " + session.State + ".");
                Finish();
                return;
            }

            var client = session.Client;
            if (client != null && client != scriptedFor)
            {
                scriptedFor = client;
                script = new ScriptedPlayer(client, IntArg("-duckPlayers", 2));
                client.SnapshotReceived += script.Step;
            }
            if (script == null || !script.Finished) return;

            // Finished: the host lingers so the last state reaches everyone, then both write and quit.
            if (finishedAt < 0f) finishedAt = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup - finishedAt >= (role == "host" ? LingerSeconds : 1f)) Finish();
        }

        void Finish()
        {
            if (done) return;
            done = true;
            var runner = SessionRunner.Instance;
            // The host reports the rules' own state; a client reports what it was sent.
            MatchState state = runner == null ? null : runner.Match != null ? runner.Match.State : runner.Client == null ? null : runner.Client.State;
            var overrun = runner == null || runner.Client == null || runner.Client.Snapshot == null ? null : runner.Client.Snapshot.overrun;
            string text = SessionDigest.Of(state, overrun);
            if (script != null && script.CommandsSent == 0) errors.Add("[Error] The scripted player never sent a command.");
            foreach (var e in errors) text += e + "\n";
            try { File.WriteAllText(outFile, text); }
            catch (Exception ex) { Debug.LogError("Duck and Cover smoke: could not write " + outFile + ". " + ex.Message); }
            Debug.Log("Duck and Cover smoke (" + role + "): " + (errors.Count == 0 ? "finished" : errors.Count + " error(s)") + "\n" + text);
            if (runner != null) runner.Leave();
            int code = errors.Count > 0 || state == null || state.phase != MatchPhase.MatchEnd ? 1 : 0;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit(code);
#endif
        }
    }
}
