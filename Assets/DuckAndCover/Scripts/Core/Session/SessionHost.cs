using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// The server's half of a network session, with no network library in it. It owns who is
    /// connected, gives each connection a player and a team, turns bytes from clients into commands
    /// for <see cref="MatchRules"/>, and sends every rule event and regular state copies back out.
    /// It is also the <see cref="INetBridge"/>.
    ///
    /// A transport (Net's Netcode one, or <see cref="LoopbackNetwork"/> in tests) calls
    /// <see cref="Approve"/>, <see cref="Connected"/>, <see cref="Receive"/> and <see cref="Left"/>;
    /// the game loop calls <see cref="Update"/>.
    /// </summary>
    public sealed class SessionHost : INetBridge
    {
        sealed class Peer
        {
            public ulong connection;
            public int playerId;
            public string token;
            public string name;
            public TeamId preferredTeam;
            public bool connected;
            public bool pending;
            public bool host;
        }

        public MatchRules Match { get; }
        public NetSettings Settings { get; }

        readonly IGameEvents events;
        readonly IServerTransport transport;
        readonly List<Peer> peers = new List<Peer>();
        readonly List<Func<MatchCommand, CommandResult>> handlers = new List<Func<MatchCommand, CommandResult>>();
        readonly List<Action<float>> tickHandlers = new List<Action<float>>();
        readonly List<Action<MatchSnapshot>> snapshotFillers = new List<Action<MatchSnapshot>>();
        int nextPlayerId = 1;
        float accumulator;

        /// <summary>Most ticks one <see cref="Update"/> may run, so a long frame can't snowball.</summary>
        public int MaxTicksPerUpdate = 200;

        /// <summary>Every command after the rules answered it (logs, tests).</summary>
        public event Action<MatchCommand, CommandResult> CommandHandled;

        public SessionHost(MatchRules match, IGameEvents events, IServerTransport transport, NetSettings settings = null)
        {
            Match = match ?? throw new ArgumentNullException(nameof(match));
            this.events = events ?? throw new ArgumentNullException(nameof(events));
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            Settings = settings ?? NetSettings.Default;
            events.Subscribe(Route);
        }

        /// <summary>Stop forwarding events (the session is over).</summary>
        public void Close() => events.Unsubscribe(Route);

        /// <summary>
        /// Commands the Match module doesn't handle (fire, abilities, upgrades, pings) are offered to
        /// these in order. Return NotHandled to pass a command on.
        /// </summary>
        public void AddCommandHandler(Func<MatchCommand, CommandResult> handler)
        {
            if (handler != null) handlers.Add(handler);
        }

        /// <summary>
        /// Other server systems that move on with the match (abilities, weapons): called every tick
        /// right after MatchRules.Tick, with the same dt, before any state goes out.
        /// </summary>
        public void AddTickHandler(Action<float> handler)
        {
            if (handler != null) tickHandlers.Add(handler);
        }

        /// <summary>Other server systems that add their state to every snapshot (health, positions, ammo).</summary>
        public void AddSnapshotFiller(Action<MatchSnapshot> filler)
        {
            if (filler != null) snapshotFillers.Add(filler);
        }

        public int HostPlayerId
        {
            get { foreach (var p in peers) if (p.host && p.playerId > 0) return p.playerId; return -1; }
        }

        public int ConnectedCount
        {
            get { int n = 0; foreach (var p in peers) if (p.connected) n++; return n; }
        }

        /// <summary>The player on a connection, or -1.</summary>
        public int PlayerOf(ulong connection)
        {
            var p = ByConnection(connection);
            return p == null ? -1 : p.playerId;
        }

        // ---- Connections ----

        /// <summary>
        /// A connection asks to join, with its <see cref="HelloMessage"/> bytes. Returns null to let
        /// it in, or the reason it can't. Nothing is sent yet: call <see cref="Connected"/> once the
        /// transport can deliver to it.
        /// </summary>
        public string Approve(ulong connection, byte[] helloBytes, bool isHost = false)
        {
            HelloMessage hello;
            try { hello = WireCodec.Decode(helloBytes) as HelloMessage; }
            catch (WireException) { hello = null; }
            if (hello == null) return "This game is a different version.";
            if (hello.fingerprint != WireCodec.Fingerprint) return "This game is a different version.";
            if (string.IsNullOrEmpty(hello.token)) return "No player token.";
            if (Match.State.phase == MatchPhase.MatchEnd) return "The match is over.";

            var known = ByToken(hello.token);
            if (known != null && (known.connected || known.pending)) return "This player is already connected.";
            if (ByConnection(connection) != null) return "This connection already joined.";

            int open = 0;
            foreach (var team in new[] { TeamId.A, TeamId.B })
                open += Math.Max(0, Match.Rules.maxPlayersPerTeam - Match.State.ConnectedCount(team));
            foreach (var p in peers) if (p.pending) open--;
            if (open <= 0) return "The match is full.";

            if (known == null)
            {
                known = new Peer { token = hello.token };
                peers.Add(known);
            }
            known.connection = connection;
            known.pending = true;
            known.host = isHost;
            known.preferredTeam = hello.preferredTeam;
            known.name = CleanName(hello.name);
            return null;
        }

        /// <summary>The approved connection can now receive: add its player, welcome it, send the state.</summary>
        public void Connected(ulong connection)
        {
            var peer = ByConnection(connection);
            if (peer == null || !peer.pending) return;
            peer.pending = false;

            bool rejoined = peer.playerId > 0;
            if (!rejoined) peer.playerId = nextPlayerId++;
            if (string.IsNullOrEmpty(peer.name)) peer.name = "Player " + peer.playerId;

            var existing = Match.State.Player(peer.playerId);
            var team = existing != null ? existing.team : PickTeam(peer.preferredTeam);
            var result = Match.AddPlayer(peer.playerId, team);
            if (!result.IsAccepted)
            {
                Send(connection, new RefusedMessage { reason = result.reason });
                transport.Disconnect(connection, result.reason);
                if (rejoined) peer.connection = 0; else peers.Remove(peer);
                return;
            }
            peer.connected = true;

            Send(connection, new WelcomeMessage
            {
                playerId = peer.playerId,
                team = team,
                hostPlayerId = HostPlayerId,
                rejoined = rejoined,
                tickRate = Settings.tickRate,
            });
            SendSnapshot(peer.playerId, MakeSnapshot());
            Publish(new PlayerJoinedEvent { playerId = peer.playerId, team = team, name = peer.name, rejoined = rejoined });
        }

        /// <summary>A connection closed. Safe to call twice.</summary>
        public void Left(ulong connection)
        {
            var peer = ByConnection(connection);
            if (peer == null) return;
            if (peer.pending)
            {
                peer.pending = false;
                if (peer.playerId <= 0) peers.Remove(peer);
                return;
            }
            if (!peer.connected) return;
            peer.connected = false;
            Match.RemovePlayer(peer.playerId);
            Publish(new PlayerLeftEvent { playerId = peer.playerId, name = peer.name });
        }

        /// <summary>Bytes from a connection. Anything that isn't a readable command drops the connection.</summary>
        public void Receive(ulong connection, byte[] data)
        {
            var peer = ByConnection(connection);
            if (peer == null || !peer.connected) return;
            MatchCommand command = null;
            try { command = (WireCodec.Decode(data) as CommandMessage)?.command; }
            catch (WireException) { }
            if (command == null)
            {
                transport.Disconnect(connection, "Unreadable message.");
                Left(connection);
                return;
            }
            Handle(peer, command);
        }

        // ---- Commands ----

        void Handle(Peer peer, MatchCommand command)
        {
            command.playerId = peer.playerId; // never trust the sender's own id
            var result = Execute(peer, command);
            if (result.status == CommandStatus.NotHandled)
                result = CommandResult.Reject("nothing handles " + command.GetType().Name + " yet");
            CommandHandled?.Invoke(command, result);
            if (!result.IsAccepted)
                SendToPlayer(peer.playerId, new CommandRejectedEvent { tick = Match.State.tick, command = command.GetType().Name, reason = result.reason });
        }

        CommandResult Execute(Peer peer, MatchCommand command)
        {
            switch (command)
            {
                case StartMatchCommand _:
                    if (!peer.host) return CommandResult.Reject("only the host starts the match");
                    return Match.StartMatch();
                case ChooseTeamCommand choose:
                    return ChangeTeam(peer, choose.team);
            }
            var result = Match.Apply(command);
            if (result.status != CommandStatus.NotHandled) return result;
            foreach (var h in handlers)
            {
                result = h(command);
                if (result.status != CommandStatus.NotHandled) return result;
            }
            return CommandResult.NotHandled;
        }

        CommandResult ChangeTeam(Peer peer, TeamId team)
        {
            var state = Match.State;
            if (team != TeamId.A && team != TeamId.B) return CommandResult.Reject("pick team A or B");
            if (state.phase != MatchPhase.Lobby) return CommandResult.Reject("teams are fixed once the match starts");
            var p = state.Player(peer.playerId);
            if (p == null) return CommandResult.Reject("unknown player");
            if (p.team == team) return CommandResult.Accepted;
            if (state.ConnectedCount(team) >= Match.Rules.maxPlayersPerTeam) return CommandResult.Reject("team " + team + " is full");
            var old = p.team;
            Match.RemovePlayer(peer.playerId); // in the lobby this forgets the player, hero pick included
            var added = Match.AddPlayer(peer.playerId, team);
            if (!added.IsAccepted)
            {
                Match.AddPlayer(peer.playerId, old);
                return added;
            }
            Publish(new TeamChangedEvent { playerId = peer.playerId, team = team });
            return CommandResult.Accepted;
        }

        // ---- Time ----

        /// <summary>
        /// Run the server for <paramref name="realSeconds"/> of real time at the fixed tick rate.
        /// <paramref name="speed"/> above 1 fast-forwards (the smoke test). Returns the ticks run.
        /// </summary>
        public int Update(float realSeconds, float speed = 1f)
        {
            float step = Settings.TickSeconds;
            accumulator += Math.Max(0f, realSeconds) * Math.Max(0f, speed);
            int ticks = 0;
            while (accumulator >= step && ticks < MaxTicksPerUpdate)
            {
                accumulator -= step;
                Tick();
                ticks++;
            }
            if (accumulator > step * MaxTicksPerUpdate) accumulator = 0f; // too far behind to catch up
            return ticks;
        }

        /// <summary>One server tick: the rules move on, then state goes out if it is due.</summary>
        public void Tick()
        {
            Match.Tick(Settings.TickSeconds);
            foreach (var h in tickHandlers) h(Settings.TickSeconds);
            if (Match.State.tick % Math.Max(1, Settings.snapshotEveryTicks) == 0) BroadcastSnapshot();
        }

        // ---- Sending ----

        public MatchSnapshot MakeSnapshot()
        {
            var s = Match.TakeSnapshot();
            foreach (var f in snapshotFillers) f(s);
            foreach (var p in peers)
            {
                if (p.playerId <= 0) continue;
                s.roster.Add(new SessionPlayer { id = p.playerId, name = p.name, connected = p.connected, host = p.host });
            }
            return s;
        }

        public void BroadcastSnapshot()
        {
            if (ConnectedCount == 0) return;
            var bytes = WireCodec.Encode(new SnapshotMessage { snapshot = MakeSnapshot() });
            foreach (var p in peers) if (p.connected) transport.Send(p.connection, bytes);
        }

        public void Broadcast(GameEvent e)
        {
            var bytes = WireCodec.Encode(new EventMessage { gameEvent = e });
            foreach (var p in peers) if (p.connected) transport.Send(p.connection, bytes);
        }

        public void SendToTeam(TeamId team, GameEvent e)
        {
            byte[] bytes = null;
            foreach (var p in peers)
            {
                if (!p.connected) continue;
                var player = Match.State.Player(p.playerId);
                if (player == null || player.team != team) continue;
                if (bytes == null) bytes = WireCodec.Encode(new EventMessage { gameEvent = e });
                transport.Send(p.connection, bytes);
            }
        }

        public void SendToPlayer(int playerId, GameEvent e)
        {
            foreach (var p in peers)
                if (p.connected && p.playerId == playerId) Send(p.connection, new EventMessage { gameEvent = e });
        }

        public void SendSnapshot(int playerId, MatchSnapshot s)
        {
            foreach (var p in peers)
                if (p.connected && p.playerId == playerId) Send(p.connection, new SnapshotMessage { snapshot = s });
        }

        void Send(ulong connection, SessionMessage message) => transport.Send(connection, WireCodec.Encode(message));

        /// <summary>Every rule event goes to everyone, except team events, which go to their team.</summary>
        void Route(GameEvent e)
        {
            if (e is TeamEvent t) SendToTeam(t.audience, e);
            else Broadcast(e);
        }

        void Publish(GameEvent e)
        {
            e.tick = Match.State.tick;
            events.Publish(e);
        }

        // ---- Helpers ----

        /// <summary>The asked-for team if it has room, otherwise the smaller team (A on a tie).</summary>
        TeamId PickTeam(TeamId preferred)
        {
            int a = Match.State.ConnectedCount(TeamId.A), b = Match.State.ConnectedCount(TeamId.B);
            int max = Match.Rules.maxPlayersPerTeam;
            if (preferred == TeamId.A && a < max) return TeamId.A;
            if (preferred == TeamId.B && b < max) return TeamId.B;
            return a <= b ? TeamId.A : TeamId.B;
        }

        string CleanName(string name)
        {
            if (name == null) return "";
            var chars = new System.Text.StringBuilder();
            foreach (char c in name.Trim())
            {
                if (char.IsControl(c)) continue;
                if (chars.Length >= Settings.maxNameLength) break;
                chars.Append(c);
            }
            return chars.ToString();
        }

        Peer ByConnection(ulong connection)
        {
            foreach (var p in peers) if ((p.connected || p.pending) && p.connection == connection) return p;
            return null;
        }

        Peer ByToken(string token)
        {
            foreach (var p in peers) if (p.token == token) return p;
            return null;
        }
    }
}
