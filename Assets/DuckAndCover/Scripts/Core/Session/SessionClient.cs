using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// A client's half of a session, with no network library in it. It sends commands, and keeps the
    /// latest state copy and a local event stream that Game and UI read and subscribe to, exactly as
    /// they would on the host.
    /// </summary>
    public sealed class SessionClient
    {
        readonly IClientTransport transport;

        public HelloMessage Hello { get; }

        /// <summary>My player id once welcomed, otherwise -1.</summary>
        public int PlayerId { get; private set; } = -1;
        public TeamId Team { get; private set; } = TeamId.None;
        public int HostPlayerId { get; private set; } = -1;
        public bool IsWelcomed => PlayerId > 0;
        public bool IsHostPlayer => IsWelcomed && PlayerId == HostPlayerId;
        /// <summary>Why the server turned this client away, or null.</summary>
        public string RefusedReason { get; private set; }

        /// <summary>The latest state from the server, or null before the first copy arrives.</summary>
        public MatchState State { get; private set; }
        public List<SessionPlayer> Roster { get; private set; } = new List<SessionPlayer>();
        public int SnapshotTick { get; private set; } = -1;
        public int SnapshotsReceived { get; private set; }

        /// <summary>Rule events as they arrive from the server.</summary>
        public readonly GameEventBus Events = new GameEventBus();

        public event Action<WelcomeMessage> Welcomed;
        public event Action<string> Refused;
        public event Action SnapshotReceived;

        public SessionClient(IClientTransport transport, string name, string token, TeamId preferredTeam = TeamId.None)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            Hello = new HelloMessage
            {
                fingerprint = WireCodec.Fingerprint,
                token = token ?? "",
                name = name ?? "",
                preferredTeam = preferredTeam,
            };
        }

        /// <summary>What the client hands the transport when it connects.</summary>
        public byte[] HelloBytes() => WireCodec.Encode(Hello);

        public PlayerState Me => State == null ? null : State.Player(PlayerId);

        public string NameOf(int playerId)
        {
            foreach (var p in Roster) if (p.id == playerId) return p.name;
            return "Player " + playerId;
        }

        /// <summary>Ask the server for something. The answer comes back as events and state.</summary>
        public void Send(MatchCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            command.playerId = PlayerId;
            command.tick = Math.Max(0, SnapshotTick);
            transport.Send(WireCodec.Encode(new CommandMessage { command = command }));
        }

        /// <summary>The connection was refused or closed by the server before a welcome.</summary>
        public void MarkRefused(string reason)
        {
            RefusedReason = string.IsNullOrEmpty(reason) ? "The server closed the connection." : reason;
            Refused?.Invoke(RefusedReason);
        }

        /// <summary>Bytes from the server. Throws <see cref="WireException"/> if they can't be read.</summary>
        public void Receive(byte[] data)
        {
            switch (WireCodec.Decode(data))
            {
                case WelcomeMessage w:
                    PlayerId = w.playerId;
                    Team = w.team;
                    HostPlayerId = w.hostPlayerId;
                    Welcomed?.Invoke(w);
                    break;
                case RefusedMessage r:
                    MarkRefused(r.reason);
                    break;
                case SnapshotMessage s:
                    if (s.snapshot == null || s.snapshot.match == null) throw new WireException("empty snapshot");
                    if (s.snapshot.tick < SnapshotTick) break;
                    SnapshotTick = s.snapshot.tick;
                    State = s.snapshot.match;
                    Roster = s.snapshot.roster ?? new List<SessionPlayer>();
                    var me = Me;
                    if (me != null) Team = me.team;
                    foreach (var p in Roster) if (p.host) HostPlayerId = p.id;
                    SnapshotsReceived++;
                    SnapshotReceived?.Invoke();
                    break;
                case EventMessage e:
                    if (e.gameEvent == null) throw new WireException("empty event");
                    Events.Publish(e.gameEvent);
                    break;
                default:
                    throw new WireException("a message a client shouldn't get");
            }
        }
    }
}
