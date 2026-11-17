using DuckAndCover.Core;
using DuckAndCover.Net;
using UnityEngine;

namespace DuckAndCover.Game
{
    /// <summary>
    /// A plain developer panel for hosting, joining and watching a match before the real menus and
    /// HUD exist (WP6, WP12). F1 shows or hides it. It only sends the same commands a real screen
    /// would, so it can be deleted without touching anything else.
    /// </summary>
    public class SessionDebugPanel : MonoBehaviour
    {
        static readonly string[] HeroIds = { "vanguard", "striker", "support", "controller" };

        public const float Width = 380f;
        /// <summary>The panel is on screen (so clicks on it aren't clicks into the game).</summary>
        public static bool Showing { get; private set; }

        public bool visible = true;

        void OnDisable() => Showing = false;

        string playerName = "Player";
        string address = "127.0.0.1";
        string port = "";
        string joinCode = "";
        string modeId = "capture_hold";
        string lastRejection = "";
        SessionClient subscribedTo;

        void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame) visible = !visible;
            Showing = visible;

            var runner = SessionRunner.Instance;
            var client = runner == null ? null : runner.Client;
            if (client != subscribedTo)
            {
                if (subscribedTo != null) subscribedTo.Events.Unsubscribe(OnEvent);
                subscribedTo = client;
                lastRejection = "";
                if (client != null) client.Events.Subscribe(OnEvent);
            }
        }

        void OnEvent(GameEvent e)
        {
            if (e is CommandRejectedEvent r) lastRejection = r.reason;
        }

        void OnGUI()
        {
            if (!visible) return;
            GUILayout.BeginArea(new Rect(12f, 12f, Width, Screen.height - 24f), GUI.skin.box);
            GUILayout.Label("Duck and Cover network (F1 hides this)");
            var runner = SessionRunner.Ensure();
            var session = runner.Session;
            if (session == null || session.State == NetState.Offline) DrawMenu(runner);
            else DrawSession(runner, session);
            GUILayout.EndArea();
        }

        void DrawMenu(SessionRunner runner)
        {
            GUILayout.Label("Your name");
            playerName = GUILayout.TextField(playerName, 20);
            GUILayout.Label("Mode: " + modeId);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Capture and Hold")) modeId = "capture_hold";
            if (GUILayout.Button("King of the Hill")) modeId = "king_of_the_hill";
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("Port (blank = " + runner.Settings.defaultPort + ")");
            port = GUILayout.TextField(port, 5);
            int.TryParse(port, out int portNumber);
            if (GUILayout.Button("Host on this network")) runner.Host(modeId, playerName, false, portNumber);
            GUILayout.Label("Host address");
            address = GUILayout.TextField(address, 64);
            if (GUILayout.Button("Join by address")) runner.Join(address.Trim(), portNumber, playerName);

            GUILayout.Space(8f);
            if (NetSession.RelayAvailable)
            {
                if (GUILayout.Button("Host with a join code")) runner.Host(modeId, playerName, true);
                GUILayout.Label("Join code");
                joinCode = GUILayout.TextField(joinCode, 12);
                if (GUILayout.Button("Join by code")) runner.JoinWithCode(joinCode, playerName);
            }
            else
            {
                GUILayout.Label("Join codes are off: they need Unity's Multiplayer Services package and a linked Unity Cloud project.");
            }
        }

        void DrawSession(SessionRunner runner, NetSession session)
        {
            GUILayout.Label("State: " + session.State + (session.JoinCode != null ? "   Join code: " + session.JoinCode : ""));
            if (session.State == NetState.Failed) GUILayout.Label(session.Error);
            if (GUILayout.Button(session.State == NetState.Failed ? "Back" : "Leave"))
            {
                runner.Leave();
                return;
            }

            var client = session.Client;
            var state = client == null ? null : client.State;
            if (state == null)
            {
                GUILayout.Label("Waiting for the host...");
                return;
            }
            var me = client.Me;
            GUILayout.Label(state.modeId + "   " + state.phase +
                            (state.phaseTimeLeft > 0f ? "   " + Mathf.CeilToInt(state.phaseTimeLeft) + " s" : "") +
                            "   Score " + state.scoreA + " : " + state.scoreB);
            if (state.phase == MatchPhase.MatchEnd) GUILayout.Label("Team " + state.winner + " wins (" + state.endReason + ")");

            foreach (var z in state.zones)
                if (z.active)
                    GUILayout.Label("  " + z.id + "  owner " + z.owner + "  " + Mathf.RoundToInt(z.progress * 100f) + "%" + (z.contested ? "  contested" : ""));

            foreach (var team in new[] { TeamId.A, TeamId.B })
            {
                GUILayout.Label("Team " + team);
                foreach (var p in state.players)
                {
                    if (p.team != team) continue;
                    GUILayout.Label("  " + client.NameOf(p.id) + (p.id == client.PlayerId ? " (you)" : "") +
                                    (p.id == client.HostPlayerId ? " (host)" : "") +
                                    "  " + (p.heroId == "" ? "no hero" : p.heroId) + "  level " + p.level +
                                    (!p.connected ? "  left" : p.alive ? "" : state.IsPlaying ? "  respawning" : ""));
                }
            }

            if (me == null) return;
            if (state.phase == MatchPhase.Lobby)
            {
                if (GUILayout.Button("Switch to team " + me.team.Other())) client.Send(new ChooseTeamCommand { team = me.team.Other() });
                if (client.IsHostPlayer && GUILayout.Button("Add a practice dummy to team " + me.team.Other())) client.Send(new AddDummyCommand { team = me.team.Other() });
                if (client.IsHostPlayer && GUILayout.Button("Start match")) client.Send(new StartMatchCommand());
            }
            if (state.phase == MatchPhase.Lobby || state.phase == MatchPhase.HeroPick)
            {
                GUILayout.BeginHorizontal();
                foreach (var hero in HeroIds)
                    if (GUILayout.Button(hero)) client.Send(new PickHeroCommand { heroId = hero });
                GUILayout.EndHorizontal();
            }
            if (state.IsPlaying || state.phase == MatchPhase.Deploy)
                GUILayout.Label("Click the game to take the mouse, Esc frees it. WASD, Shift sprint, Ctrl crouch, Space jump or vault, R reload, 1/2 weapons.");
            if (lastRejection != "") GUILayout.Label("Refused: " + lastRejection);
        }
    }
}
