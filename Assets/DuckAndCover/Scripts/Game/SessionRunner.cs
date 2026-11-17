using System;
using DuckAndCover.Core;
using DuckAndCover.Net;
using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// Owns the running match on this machine: builds the rules when hosting, starts or joins a
    /// <see cref="NetSession"/>, and drives it every frame. Game and UI code read the match through
    /// <see cref="Client"/> (state, events, sending commands), which works the same on the host and
    /// on a machine that joined.
    ///
    /// Until the first map (WP9) and the player controller (WP4) exist, a hosted match uses a
    /// stand-in objective layout and a world where nobody stands on anything.
    /// </summary>
    public class SessionRunner : MonoBehaviour
    {
        public static SessionRunner Instance { get; private set; }

        /// <summary>A random id for this run of the game, so a dropped player gets their place back.</summary>
        static readonly string Token = Guid.NewGuid().ToString("N");

        public NetSettings Settings = NetSettings.Default;
        /// <summary>Match time multiplier on the host. Above 1 only for the smoke test.</summary>
        public float Speed = 1f;

        public NetSession Session { get; private set; }
        public SessionClient Client => Session == null ? null : Session.Client;
        /// <summary>The server's rules, on the host only.</summary>
        public MatchRules Match { get; private set; }
        public ScriptedWorld World { get; private set; }

        public static SessionRunner Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("SessionRunner");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<SessionRunner>();
            return Instance;
        }

        public static ModeDef ModeById(string id) =>
            id == "king_of_the_hill" ? ModeDef.KingOfTheHill() : ModeDef.CaptureAndHold();

        /// <summary>Host a match. With <paramref name="useJoinCode"/> it goes through Unity Relay.</summary>
        public NetSession Host(string modeId, string playerName, bool useJoinCode, int port = 0, bool scripted = false)
        {
            Leave();
            var events = new GameEventBus();
            World = new ScriptedWorld();
            var rules = scripted ? ScriptedPlay.SmokeRuleset() : Ruleset.Default;
            Match = new MatchRules(rules, ModeById(modeId), ScriptedPlay.PlaceholderMap(), World, events);
            Session = useJoinCode
                ? NetSession.HostWithCode(Match, events, Settings, playerName, Token)
                : NetSession.HostDirect(Match, events, Settings, port > 0 ? port : Settings.defaultPort, playerName, Token);
            scriptedHost = scripted;
            scriptedAttached = false;
            hostEvents = events;
            return Session;
        }

        public NetSession Join(string address, int port, string playerName)
        {
            Leave();
            Session = NetSession.JoinDirect(address, port > 0 ? port : Settings.defaultPort, Settings, playerName, Token);
            return Session;
        }

        public NetSession JoinWithCode(string code, string playerName)
        {
            Leave();
            Session = NetSession.JoinWithCode(code, Settings, playerName, Token);
            return Session;
        }

        public void Leave()
        {
            if (Session != null) Session.Shutdown();
            Session = null;
            Match = null;
            World = null;
            hostEvents = null;
        }

        bool scriptedHost;
        bool scriptedAttached;
        GameEventBus hostEvents;

        void Update()
        {
            if (Session == null) return;
            // A relay host's SessionHost appears a moment after Host() returns.
            if (scriptedHost && !scriptedAttached && Session.Host != null)
            {
                ScriptedPlay.Attach(Session.Host, World, hostEvents);
                scriptedAttached = true;
            }
            Session.Update(Time.unscaledDeltaTime, Speed);
        }

        void OnDestroy()
        {
            Leave();
            if (Instance == this) Instance = null;
        }

        void OnApplicationQuit() => Leave();
    }
}
