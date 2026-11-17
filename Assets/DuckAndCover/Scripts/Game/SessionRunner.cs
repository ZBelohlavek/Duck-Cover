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
    /// A hosted match also runs the combat rules (movement checks, weapons, health) against the
    /// scene's physics. Until the first map (WP9) exists it uses a stand-in objective layout, and
    /// until the control points do (WP6) nobody stands on any zone.
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
        /// <summary>Movement, weapons and health on the server; on the host only, once the session is up.</summary>
        public CombatServer Combat { get; private set; }
        /// <summary>
        /// The numbers this machine moves and draws by. Clients aren't sent the host's ruleset yet, so
        /// everyone uses the defaults; a host with changed movement numbers would correct its guests.
        /// </summary>
        public Ruleset ClientRules { get; private set; } = Ruleset.Default;
        /// <summary>The weapon table, the same on every machine.</summary>
        public WeaponLibrary Weapons { get; } = StarterWeapons.Library();

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
        /// <param name="rules">Other numbers than the defaults (the screenshot run shortens the waits).</param>
        public NetSession Host(string modeId, string playerName, bool useJoinCode, int port = 0, bool scripted = false, Ruleset rules = null)
        {
            Leave();
            var events = new GameEventBus();
            if (rules == null) rules = scripted ? ScriptedPlay.SmokeRuleset() : Ruleset.Default;
            IWorldQuery world;
            if (scripted) world = World = new ScriptedWorld();
            else world = new PhysicsWorld();
            Match = new MatchRules(rules, ModeById(modeId), ScriptedPlay.PlaceholderMap(), world, events);
            Session = useJoinCode
                ? NetSession.HostWithCode(Match, events, Settings, playerName, Token)
                : NetSession.HostDirect(Match, events, Settings, port > 0 ? port : Settings.defaultPort, playerName, Token);
            scriptedHost = scripted;
            attached = false;
            hostEvents = events;
            hostWorld = world;
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
            if (Combat != null) Combat.Dispose();
            Combat = null;
            Match = null;
            World = null;
            hostEvents = null;
            hostWorld = null;
        }

        bool scriptedHost;
        bool attached;
        GameEventBus hostEvents;
        IWorldQuery hostWorld;

        void Update()
        {
            if (Session == null) return;
            // A relay host's SessionHost appears a moment after Host() returns.
            if (Match != null && !attached && Session.Host != null)
            {
                if (scriptedHost) Combat = ScriptedPlay.Attach(Session.Host, World, hostEvents);
                else
                {
                    Combat = new CombatServer(Match, hostWorld, hostEvents, Settings.TickSeconds, ArenaLayout.Placeholder(), Weapons);
                    Combat.Attach(Session.Host);
                }
                attached = true;
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
