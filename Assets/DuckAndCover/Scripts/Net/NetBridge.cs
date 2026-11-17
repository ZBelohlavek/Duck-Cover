using DuckAndCover.Core;
using Unity.Netcode;
using UnityEngine;

namespace DuckAndCover.Net
{
    /// <summary>
    /// The <see cref="INetBridge"/> on top of Netcode for GameObjects. This folder is the only code
    /// that may touch the network library, so swapping it (for example to Photon Fusion) stays a
    /// change to Net alone.
    ///
    /// Bootstrap stub: WP3 (network foundation) fills in sending, team routing and snapshots.
    /// Until then events are dropped, and only <see cref="Status"/> is real.
    /// </summary>
    public class NetBridge : INetBridge
    {
        public void Broadcast(GameEvent e) { }

        public void SendToTeam(TeamId team, GameEvent e) { }

        public void SendSnapshot(int playerId, MatchSnapshot s) { }

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
