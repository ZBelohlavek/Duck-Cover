using DuckAndCover.Core;
using Unity.Netcode;
using UnityEngine;

namespace DuckAndCover.Net
{
    /// <summary>
    /// The <see cref="INetBridge"/> the rest of the game holds. It forwards to the active
    /// <see cref="NetSession"/>'s host, and drops everything when this machine isn't hosting, so
    /// rules code can call it without asking who is the server. This folder is the only code that
    /// may touch the network library, so swapping it (for example to Photon Fusion) stays a change
    /// to Net alone.
    /// </summary>
    public class NetBridge : INetBridge
    {
        static SessionHost Host => NetSession.Active == null ? null : NetSession.Active.Host;

        public void Broadcast(GameEvent e) { var h = Host; if (h != null) h.Broadcast(e); }

        public void SendToTeam(TeamId team, GameEvent e) { var h = Host; if (h != null) h.SendToTeam(team, e); }

        public void SendToPlayer(int playerId, GameEvent e) { var h = Host; if (h != null) h.SendToPlayer(playerId, e); }

        public void SendSnapshot(int playerId, MatchSnapshot s) { var h = Host; if (h != null) h.SendSnapshot(playerId, s); }

        /// <summary>One line describing the network state, for logs and the debug overlay.</summary>
        public static string Status
        {
            get
            {
                var nm = NetworkManager.Singleton;
                if (nm == null) return "offline (no NetworkManager)";
                if (!nm.IsListening) return "offline";
                if (nm.IsHost) return "host";
                if (nm.IsServer) return "server";
                return nm.IsConnectedClient ? "client" : "connecting";
            }
        }

        /// <summary>True when this process decides outcomes: offline play or the host/server.</summary>
        public static bool IsAuthority
        {
            get
            {
                var nm = NetworkManager.Singleton;
                return nm == null || !nm.IsListening || nm.IsServer;
            }
        }

        internal static void Log(string message) => Debug.Log("Duck and Cover net: " + message);
    }
}
