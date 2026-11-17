using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// A network inside one process: a <see cref="SessionHost"/> and any number of
    /// <see cref="SessionClient"/>s exchanging the same bytes they would over the wire, delivered
    /// when <see cref="Pump"/> runs. Used by the tests and the cloud smoke test, and good for a
    /// solo practice match later.
    /// </summary>
    public sealed class LoopbackNetwork : IServerTransport
    {
        sealed class Link : IClientTransport
        {
            public LoopbackNetwork net;
            public ulong connection;
            public SessionClient client;
            public bool open;
            public void Send(byte[] message)
            {
                if (open) net.toServer.Enqueue(new KeyValuePair<ulong, byte[]>(connection, message));
            }
        }

        readonly Dictionary<ulong, Link> links = new Dictionary<ulong, Link>();
        readonly Queue<KeyValuePair<ulong, byte[]>> toServer = new Queue<KeyValuePair<ulong, byte[]>>();
        readonly Queue<KeyValuePair<ulong, byte[]>> toClients = new Queue<KeyValuePair<ulong, byte[]>>();
        ulong nextConnection = 1;

        public SessionHost Host;
        public long BytesToClients { get; private set; }
        public long BytesToServer { get; private set; }

        /// <summary>
        /// Connect a new client. The first is the host's own player unless <paramref name="isHost"/>
        /// says otherwise. Check <see cref="SessionClient.RefusedReason"/> afterwards.
        /// </summary>
        public SessionClient Connect(string name, string token, TeamId preferredTeam = TeamId.None, bool? isHost = null)
        {
            if (Host == null) throw new InvalidOperationException("set Host first");
            var link = new Link { net = this, connection = nextConnection++ };
            link.client = new SessionClient(link, name, token, preferredTeam);
            string refusal = Host.Approve(link.connection, link.client.HelloBytes(), isHost ?? link.connection == 1);
            if (refusal != null)
            {
                link.client.MarkRefused(refusal);
                return link.client;
            }
            link.open = true;
            links[link.connection] = link;
            Host.Connected(link.connection);
            return link.client;
        }

        /// <summary>The client's connection drops.</summary>
        public void Drop(SessionClient client)
        {
            foreach (var kv in links)
            {
                if (kv.Value.client != client) continue;
                kv.Value.open = false;
                links.Remove(kv.Key);
                Host.Left(kv.Key);
                return;
            }
        }

        /// <summary>Send raw bytes as if from a client's connection (tests of bad input).</summary>
        public void InjectToServer(SessionClient client, byte[] bytes)
        {
            foreach (var kv in links)
                if (kv.Value.client == client) toServer.Enqueue(new KeyValuePair<ulong, byte[]>(kv.Key, bytes));
        }

        void IServerTransport.Send(ulong connection, byte[] message) =>
            toClients.Enqueue(new KeyValuePair<ulong, byte[]>(connection, message));

        void IServerTransport.Disconnect(ulong connection, string reason)
        {
            if (!links.TryGetValue(connection, out var link)) return;
            // Deliver what was already sent (the refusal message), then close.
            Pump();
            link.open = false;
            links.Remove(connection);
            if (!link.client.IsWelcomed && link.client.RefusedReason == null) link.client.MarkRefused(reason);
        }

        /// <summary>Deliver everything in flight, including what the deliveries send in turn.</summary>
        public void Pump()
        {
            for (int guard = 0; guard < 100000 && (toServer.Count > 0 || toClients.Count > 0); guard++)
            {
                if (toClients.Count > 0)
                {
                    var m = toClients.Dequeue();
                    BytesToClients += m.Value.Length;
                    if (links.TryGetValue(m.Key, out var link) && link.open) link.client.Receive(m.Value);
                }
                else
                {
                    var m = toServer.Dequeue();
                    BytesToServer += m.Value.Length;
                    Host.Receive(m.Key, m.Value);
                }
            }
        }
    }
}
