using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DuckAndCover.Core;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
#if DUCK_RELAY
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
#endif

namespace DuckAndCover.Net
{
    public enum NetState
    {
        Offline = 0,
        /// <summary>Asking Unity Relay for a session or a join code.</summary>
        Preparing = 1,
        Connecting = 2,
        /// <summary>Hosting, with the host's own player in the match.</summary>
        Hosting = 3,
        /// <summary>Joined someone else's match.</summary>
        Joined = 4,
        /// <summary>Refused, dropped or failed; see <see cref="NetSession.Error"/>.</summary>
        Failed = 5,
    }

    /// <summary>
    /// One network session on Netcode for GameObjects: host a match or join one, by address or by
    /// Unity Relay join code. It is only a pipe. All the session logic (who is who, teams, command
    /// routing, state copies) is Core's <see cref="SessionHost"/> and <see cref="SessionClient"/>;
    /// this class moves their bytes as Netcode named messages and reports connections.
    ///
    /// The NetworkManager is created in code, so there is no prefab or scene object to keep in step.
    /// Nothing here uses NetworkObjects yet; player bodies arrive with the player controller (WP4).
    /// The host's own player doesn't cross the network at all: its messages go through a local queue.
    /// </summary>
    public sealed class NetSession : IServerTransport, IClientTransport
    {
        /// <summary>The session in use, or null. Game and UI read the match through this.</summary>
        public static NetSession Active { get; private set; }

        const string Channel = "DuckAndCover";
        const string RelayProtocol = "dtls";

        public NetState State { get; private set; } = NetState.Offline;
        /// <summary>Why the session failed, in words a player can read.</summary>
        public string Error { get; private set; }
        /// <summary>The code to share with friends, when hosting through Unity Relay.</summary>
        public string JoinCode { get; private set; }
        public string Address { get; private set; }
        public int Port { get; private set; }

        /// <summary>The server's half. Null on a machine that joined.</summary>
        public SessionHost Host { get; private set; }
        /// <summary>This machine's player, on the host as well.</summary>
        public SessionClient Client { get; private set; }
        public bool IsHost => Host != null;

        /// <summary>True when join codes can work in this build (the Unity services package is installed).</summary>
        public static bool RelayAvailable
        {
            get
            {
#if DUCK_RELAY
                return true;
#else
                return false;
#endif
            }
        }

        GameObject root;
        NetworkManager manager;
        UnityTransport utp;
        ulong localId;
        // The host's own player: delivered on the next Update instead of the wire.
        readonly Queue<byte[]> toLocalClient = new Queue<byte[]>();
        readonly Queue<byte[]> fromLocalClient = new Queue<byte[]>();

        // ---- Starting ----

        /// <summary>
        /// Host on this machine's network. Friends on the same network (or with the port forwarded)
        /// join with this machine's address and <paramref name="port"/>.
        /// </summary>
        public static NetSession HostDirect(MatchRules match, IGameEvents events, NetSettings settings, int port, string playerName, string token)
        {
            var s = Begin();
            s.Port = port;
            s.Address = "0.0.0.0";
            s.CreateManager(settings);
            s.utp.SetConnectionData("127.0.0.1", (ushort)port, "0.0.0.0");
            s.StartHosting(match, events, settings, playerName, token);
            return s;
        }

        /// <summary>Join a host by address.</summary>
        public static NetSession JoinDirect(string address, int port, NetSettings settings, string playerName, string token)
        {
            var s = Begin();
            s.Address = address;
            s.Port = port;
            s.CreateManager(settings);
            s.utp.SetConnectionData(address, (ushort)port);
            s.StartJoining(playerName, token);
            return s;
        }

        /// <summary>
        /// Host through Unity Relay, so nobody has to open a router port. Returns at once in the
        /// Preparing state; <see cref="JoinCode"/> is set when the session is up.
        /// </summary>
        public static NetSession HostWithCode(MatchRules match, IGameEvents events, NetSettings settings, string playerName, string token)
        {
            var s = Begin();
            s.State = NetState.Preparing;
            s.RunRelay(s.HostRelayAsync(match, events, settings, playerName, token));
            return s;
        }

        /// <summary>Join a host by the code they shared.</summary>
        public static NetSession JoinWithCode(string joinCode, NetSettings settings, string playerName, string token)
        {
            var s = Begin();
            s.State = NetState.Preparing;
            s.JoinCode = (joinCode ?? "").Trim().ToUpperInvariant();
            s.RunRelay(s.JoinRelayAsync(settings, playerName, token));
            return s;
        }

        static NetSession Begin()
        {
            if (Active != null) Active.Shutdown();
            var s = new NetSession();
            Active = s;
            return s;
        }

        void CreateManager(NetSettings settings)
        {
            root = new GameObject("NetworkManager");
            UnityEngine.Object.DontDestroyOnLoad(root);
            utp = root.AddComponent<UnityTransport>();
            manager = root.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = utp,
                ConnectionApproval = true,
                EnableSceneManagement = false,
                TickRate = (uint)Math.Max(1, settings.tickRate),
            };
            manager.OnClientConnectedCallback += OnClientConnected;
            manager.OnClientDisconnectCallback += OnClientDisconnected;
        }

        void StartHosting(MatchRules match, IGameEvents events, NetSettings settings, string playerName, string token)
        {
            Host = new SessionHost(match, events, this, settings);
            Client = new SessionClient(this, playerName, token);
            manager.ConnectionApprovalCallback = Approve;
            if (!manager.StartHost())
            {
                Fail("Could not start hosting. Is port " + Port + " already in use?");
                return;
            }
            localId = manager.LocalClientId;
            manager.CustomMessagingManager.RegisterNamedMessageHandler(Channel, OnNamedMessage);

            string refusal = Host.Approve(localId, Client.HelloBytes(), isHost: true);
            if (refusal != null) { Fail(refusal); return; }
            Host.Connected(localId);
            State = NetState.Hosting;
            NetBridge.Log("hosting" + (JoinCode != null ? " with join code " + JoinCode : " on port " + Port) + ", build " + WireCodec.Fingerprint);
        }

        void StartJoining(string playerName, string token)
        {
            Client = new SessionClient(this, playerName, token);
            Client.Refused += reason => Fail(reason);
            manager.NetworkConfig.ConnectionData = Client.HelloBytes();
            if (!manager.StartClient())
            {
                Fail("Could not start the connection.");
                return;
            }
            localId = manager.LocalClientId;
            manager.CustomMessagingManager.RegisterNamedMessageHandler(Channel, OnNamedMessage);
            State = NetState.Connecting;
            NetBridge.Log("joining " + (JoinCode != null ? "with code " + JoinCode : Address + ":" + Port) + ", build " + WireCodec.Fingerprint);
        }

        // ---- Unity Relay (join codes) ----

        async void RunRelay(Task work)
        {
            try { await work; }
            catch (Exception e)
            {
                Debug.LogWarning("Duck and Cover net: " + e.Message);
                Fail("Could not reach Unity Relay: " + e.Message);
            }
        }

#if DUCK_RELAY
        static async Task SignInAsync()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized) await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        async Task HostRelayAsync(MatchRules match, IGameEvents events, NetSettings settings, string playerName, string token)
        {
            await SignInAsync();
            // Relay counts connections besides the host.
            int others = Math.Max(1, match.Rules.maxPlayersPerTeam * 2 - 1);
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(others);
            string code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            if (Active != this) return; // cancelled while waiting
            JoinCode = code;
            CreateManager(settings);
            utp.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, RelayProtocol));
            StartHosting(match, events, settings, playerName, token);
        }

        async Task JoinRelayAsync(NetSettings settings, string playerName, string token)
        {
            await SignInAsync();
            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(JoinCode);
            if (Active != this) return;
            CreateManager(settings);
            utp.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, RelayProtocol));
            StartJoining(playerName, token);
        }
#else
        const string NoRelay = "Join codes need Unity's Multiplayer Services package and a linked Unity Cloud project. Use a direct address for now.";

        Task HostRelayAsync(MatchRules match, IGameEvents events, NetSettings settings, string playerName, string token)
        {
            Fail(NoRelay);
            return Task.CompletedTask;
        }

        Task JoinRelayAsync(NetSettings settings, string playerName, string token)
        {
            Fail(NoRelay);
            return Task.CompletedTask;
        }
#endif

        // ---- Running ----

        /// <summary>
        /// Call every frame. Delivers the host's own player's messages and, on the host, runs the
        /// server at its fixed tick rate. <paramref name="speed"/> above 1 fast-forwards the match.
        /// </summary>
        public void Update(float realSeconds, float speed = 1f)
        {
            if (State == NetState.Failed || State == NetState.Offline) return;
            PumpLocal();
            if (Host != null && State == NetState.Hosting)
            {
                Host.Update(realSeconds, speed);
                PumpLocal();
            }
        }

        void PumpLocal()
        {
            for (int guard = 0; guard < 10000 && (toLocalClient.Count > 0 || fromLocalClient.Count > 0); guard++)
            {
                if (toLocalClient.Count > 0) Client.Receive(toLocalClient.Dequeue());
                else Host.Receive(localId, fromLocalClient.Dequeue());
            }
        }

        /// <summary>Leave or stop hosting, and remove the NetworkManager.</summary>
        public void Shutdown()
        {
            if (Host != null) Host.Close();
            if (manager != null)
            {
                manager.OnClientConnectedCallback -= OnClientConnected;
                manager.OnClientDisconnectCallback -= OnClientDisconnected;
                if (manager.IsListening) manager.Shutdown();
            }
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
            manager = null;
            utp = null;
            if (State != NetState.Failed) State = NetState.Offline;
            if (Active == this) Active = null;
        }

        void Fail(string reason)
        {
            if (State == NetState.Failed) return;
            Error = string.IsNullOrEmpty(reason) ? "The connection was lost." : reason;
            State = NetState.Failed;
            NetBridge.Log("failed: " + Error);
        }

        // ---- Netcode callbacks ----

        void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = false;
            response.Pending = false;
            if (request.ClientNetworkId == NetworkManager.ServerClientId)
            {
                response.Approved = true; // the host's own player joins through the local queue
                return;
            }
            string refusal = Host.Approve(request.ClientNetworkId, request.Payload ?? new byte[0]);
            response.Approved = refusal == null;
            response.Reason = refusal;
        }

        void OnClientConnected(ulong clientId)
        {
            if (Host != null)
            {
                if (clientId != localId) Host.Connected(clientId);
                return;
            }
            if (clientId == manager.LocalClientId && State == NetState.Connecting) State = NetState.Joined;
        }

        void OnClientDisconnected(ulong clientId)
        {
            if (Host != null)
            {
                if (clientId != localId) Host.Left(clientId);
                return;
            }
            // On a client this only ever reports its own connection.
            // Netcode puts its own transport notes first and the host's reason on the last line.
            string reason = manager != null ? manager.DisconnectReason : null;
            if (!string.IsNullOrEmpty(reason) && reason.IndexOf('\n') >= 0) reason = reason.Substring(reason.LastIndexOf('\n') + 1).Trim();
            if (string.IsNullOrEmpty(reason))
                reason = State == NetState.Connecting ? "Could not reach the host." : "The connection to the host was lost.";
            Fail(reason);
        }

        void OnNamedMessage(ulong senderClientId, FastBufferReader reader)
        {
            byte[] bytes;
            try
            {
                reader.ReadValueSafe(out int length);
                if (length < 0 || length > reader.Length - reader.Position) throw new WireException("bad length");
                bytes = new byte[length];
                reader.ReadBytesSafe(ref bytes, length);
            }
            catch (Exception)
            {
                if (Host != null) ((IServerTransport)this).Disconnect(senderClientId, "Unreadable message.");
                return;
            }

            if (Host != null)
            {
                Host.Receive(senderClientId, bytes);
                return;
            }
            try { Client.Receive(bytes); }
            catch (WireException e) { Fail("The host sent something this build can't read (" + e.Message + ")."); }
        }

        void SendBytes(ulong clientId, byte[] message)
        {
            if (manager == null || !manager.IsListening || manager.CustomMessagingManager == null) return;
            using (var writer = new FastBufferWriter(message.Length + sizeof(int), Allocator.Temp))
            {
                writer.WriteValueSafe(message.Length);
                writer.WriteBytesSafe(message);
                manager.CustomMessagingManager.SendNamedMessage(Channel, clientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
            }
        }

        // ---- Transports for Core ----

        void IServerTransport.Send(ulong connection, byte[] message)
        {
            if (connection == localId) toLocalClient.Enqueue(message);
            else SendBytes(connection, message);
        }

        void IServerTransport.Disconnect(ulong connection, string reason)
        {
            if (connection == localId || manager == null) return;
            manager.DisconnectClient(connection, reason);
            Host.Left(connection);
        }

        void IClientTransport.Send(byte[] message)
        {
            if (Host != null) fromLocalClient.Enqueue(message);
            else SendBytes(NetworkManager.ServerClientId, message);
        }
    }
}
