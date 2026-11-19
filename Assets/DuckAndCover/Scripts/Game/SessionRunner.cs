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
    /// scene's physics, on the first map (WP9): its zones, spawn spots and the zone cylinders that
    /// say who stands where, and in Flag Rush the flag (WP21) with its spots and base zones.
    /// In Overrun (WP19) the match plays on Breakwater: the host also runs the lanes, waves and camps
    /// (<see cref="LaneServer"/>) and the Bastions (<see cref="OverrunServer"/>).
    /// Each mode has its map (<see cref="MapCatalog.MapIdForMode"/>): the host builds it before hosting,
    /// and a machine that joins builds it once the first state copy says which mode is on.
    /// Scripted runs (the smoke test) keep their stand-in layout.
    /// </summary>
    public class SessionRunner : MonoBehaviour
    {
        public static SessionRunner Instance { get; private set; }

        /// <summary>A random id for this run of the game, so a dropped player gets their place back.</summary>
        static readonly string Token = Guid.NewGuid().ToString("N");

        public NetSettings Settings = NetSettings.Default;
        /// <summary>Match time multiplier on the host. Above 1 only for the smoke test.</summary>
        public float Speed = 1f;
        /// <summary>Build the mode's map when a match starts or is joined. The smoke test draws nothing, so it turns this off.</summary>
        public bool BuildMaps = true;

        public NetSession Session { get; private set; }
        public SessionClient Client => Session == null ? null : Session.Client;
        /// <summary>The server's rules, on the host only.</summary>
        public MatchRules Match { get; private set; }
        public ScriptedWorld World { get; private set; }
        /// <summary>Movement, weapons and health on the server; on the host only, once the session is up.</summary>
        public CombatServer Combat { get; private set; }
        /// <summary>Hero kits, leveling and upgrades on the server (WP11); on the host only, once the session is up.</summary>
        public HeroServer Heroes { get; private set; }
        /// <summary>The Flag Rush flag on the server (WP21); on a Flag Rush host only, once the session is up.</summary>
        public FlagServer Flag { get; private set; }
        /// <summary>Lanes, waves and camps (WP15); on an Overrun host only, once the session is up.</summary>
        public LaneServer Lanes { get; private set; }
        /// <summary>The Bastions and the rest of Overrun's rules (WP16); on an Overrun host only, once the session is up.</summary>
        public OverrunServer Overrun { get; private set; }
        /// <summary>
        /// The numbers this machine moves and draws by. Clients aren't sent the host's ruleset yet, so
        /// everyone uses the defaults; a host with changed movement numbers would correct its guests.
        /// </summary>
        public Ruleset ClientRules { get; private set; } = Ruleset.Default;
        /// <summary>The map this machine has built and plays: the match's mode picks it.</summary>
        public MapDef Map => MapScene.Current != null ? MapScene.Current : fallbackMap;
        readonly MapDef fallbackMap = MapCatalog.Default;
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
            id == "king_of_the_hill" ? ModeDef.KingOfTheHill() : id == "flag_rush" ? ModeDef.FlagRush()
            : id == "overrun" ? ModeDef.Overrun() : ModeDef.CaptureAndHold();

        /// <summary>
        /// Make sure the scene shows the map <paramref name="modeId"/> plays on, building it in place of
        /// the one there now if it differs. The old map is switched off at once (so no ray meets it again)
        /// and destroyed at the end of the frame. A scene laid out by hand (no map built from data) is left alone.
        /// </summary>
        public static void EnsureMap(string modeId)
        {
            string want = MapCatalog.MapIdForMode(modeId);
            var current = MapScene.Current;
            if (MapScene.Root == null || (current != null && current.id == want)) return;
            var next = MapCatalog.ById(want);
            if (next == null) return;
            var old = MapScene.Root;
            old.SetActive(false);
            Destroy(old);
            MapScene.Build(next);
            Physics.SyncTransforms(); // the new map's colliders answer rays this frame
            Debug.Log("Duck and Cover: built " + next.displayName + " for " + modeId + ".");
        }

        /// <summary>Host a match. With <paramref name="useJoinCode"/> it goes through Unity Relay.</summary>
        /// <param name="rules">Other numbers than the defaults (the screenshot run shortens the waits).</param>
        public NetSession Host(string modeId, string playerName, bool useJoinCode, int port = 0, bool scripted = false, Ruleset rules = null)
        {
            Leave();
            if (!scripted && BuildMaps) EnsureMap(modeId);
            var events = new GameEventBus();
            flagSeed = Environment.TickCount; // a different run of flag spots every match
            if (rules == null) rules = scripted ? ScriptedPlay.SmokeRuleset() : Ruleset.Default;
            IWorldQuery world;
            if (scripted) world = World = new ScriptedWorld();
            else world = new PhysicsWorld();
            var objective = scripted ? ScriptedPlay.PlaceholderMap() : Map.ToMatchMap();
            Match = new MatchRules(rules, ModeById(modeId), objective, world, events);
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
            if (Overrun != null) Overrun.Dispose();
            Overrun = null;
            if (Lanes != null) Lanes.Dispose();
            Lanes = null;
            if (Flag != null) Flag.Dispose();
            Flag = null;
            if (Heroes != null) Heroes.Dispose();
            Heroes = null;
            if (Combat != null) Combat.Dispose();
            Combat = null;
            Match = null;
            World = null;
            hostEvents = null;
            hostWorld = null;
        }

        bool scriptedHost;
        int flagSeed;
        bool attached;
        GameEventBus hostEvents;
        IWorldQuery hostWorld;

        void Update()
        {
            if (Session == null) return;
            // A relay host's SessionHost appears a moment after Host() returns.
            if (Match != null && !attached && Session.Host != null)
            {
                if (scriptedHost) Combat = ScriptedPlay.Attach(Session.Host, World, hostEvents, HeroData.Catalog, HeroData.Abilities);
                else
                {
                    var combat = Combat = new CombatServer(Match, hostWorld, hostEvents, Settings.TickSeconds, Map.ToLayout(), Weapons, HeroData.Abilities);
                    if (hostWorld is PhysicsWorld physics) physics.ZoneQuery = zone => Map.PlayersInZone(zone, combat.Movement.Motions);
                    Combat.Attach(Session.Host);
                    Heroes = new HeroServer(Combat, HeroData.Catalog, hostWorld, hostEvents);
                    Heroes.Attach(Session.Host);
                    // The map's jungle, if it has one (jungle upgrades read it in every mode).
                    Combat.Damage.Jungle = JungleArea.For(Map, Match.Rules);
                    if (Match.FlagMode)
                    {
                        // After the hero server: the flag wraps its ability check (no dashing with the flag).
                        Flag = new FlagServer(Combat, hostWorld, hostEvents, flagSeed, Heroes.Progression);
                        Flag.Attach(Session.Host);
                    }
                    if (Match.OverrunMode && Map.IsLaneMap)
                    {
                        // Lanes first: the Overrun rules stand on its unit kinds and act after its tick.
                        Lanes = new LaneServer(Match, Combat, hostWorld, hostEvents, Map, Heroes.Progression);
                        Lanes.Attach(Session.Host);
                        Overrun = new OverrunServer(Lanes, hostEvents, Map, Heroes.Progression);
                        Overrun.Attach(Session.Host);
                    }
                }
                attached = true;
            }
            // A machine that joined learns the mode from the first state copy, and builds its map then.
            if (BuildMaps && Match == null && Client != null && Client.State != null && !string.IsNullOrEmpty(Client.State.modeId)) EnsureMap(Client.State.modeId);
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
