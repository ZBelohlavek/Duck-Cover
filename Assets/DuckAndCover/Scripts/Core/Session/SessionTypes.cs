using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    // ---- Transports: what the session needs from a network library ----

    /// <summary>
    /// The server's side of a network library: send bytes to one connection, or drop it. Net
    /// implements it on Netcode for GameObjects; tests use <see cref="LoopbackNetwork"/>.
    /// Messages to one connection must arrive reliably and in order.
    /// </summary>
    public interface IServerTransport
    {
        void Send(ulong connection, byte[] message);
        void Disconnect(ulong connection, string reason);
    }

    /// <summary>A client's side: send bytes to the server, reliably and in order.</summary>
    public interface IClientTransport
    {
        void Send(byte[] message);
    }

    /// <summary>Network numbers. Not balance, so they live here rather than in the Ruleset.</summary>
    [Serializable]
    public class NetSettings
    {
        /// <summary>Server ticks per second. MatchRules.Tick runs with dt = 1 / tickRate.</summary>
        public int tickRate = 20;
        /// <summary>A full state copy goes to every player this many ticks apart (2 = 10 a second).</summary>
        public int snapshotEveryTicks = 2;
        /// <summary>Longest player name; longer names are cut.</summary>
        public int maxNameLength = 20;
        public int defaultPort = 7777;

        public float TickSeconds => 1f / Math.Max(1, tickRate);

        public static NetSettings Default => new NetSettings();
    }

    // ---- Session messages: the envelope around everything on the wire ----

    /// <summary>Base of the messages the session itself sends. Every wire message is one of these.</summary>
    public abstract class SessionMessage { }

    /// <summary>Client -> server, as the connection payload: who I am and which build I run.</summary>
    public sealed class HelloMessage : SessionMessage
    {
        /// <summary><see cref="WireCodec.Fingerprint"/> of the client's build.</summary>
        public string fingerprint = "";
        /// <summary>
        /// A random id the client keeps for the session, so a player who drops and comes back gets
        /// their own player, team, hero and level back.
        /// </summary>
        public string token = "";
        public string name = "";
        /// <summary>The team the player asks for, or None to be balanced.</summary>
        public TeamId preferredTeam = TeamId.None;
    }

    /// <summary>Server -> one client after joining: your player id and team.</summary>
    public sealed class WelcomeMessage : SessionMessage
    {
        public int playerId;
        public TeamId team = TeamId.None;
        /// <summary>The player allowed to start the match (the host's own player), or -1.</summary>
        public int hostPlayerId = -1;
        public bool rejoined;
        public int tickRate;
    }

    /// <summary>Server -> one client: you can't join, and why. The connection is closed after it.</summary>
    public sealed class RefusedMessage : SessionMessage
    {
        public string reason = "";
    }

    /// <summary>Client -> server: a request.</summary>
    public sealed class CommandMessage : SessionMessage
    {
        public MatchCommand command;
    }

    /// <summary>Server -> client: something happened.</summary>
    public sealed class EventMessage : SessionMessage
    {
        public GameEvent gameEvent;
    }

    /// <summary>Server -> client: the whole state.</summary>
    public sealed class SnapshotMessage : SessionMessage
    {
        public MatchSnapshot snapshot;
    }

    // ---- Session commands and events ----

    /// <summary>Move to the other team (lobby only, if it isn't full).</summary>
    public sealed class ChooseTeamCommand : MatchCommand
    {
        public TeamId team = TeamId.None;
    }

    /// <summary>Leave the lobby and start hero pick. Only the host's player may send it.</summary>
    public sealed class StartMatchCommand : MatchCommand { }

    /// <summary>Sent to the one player whose command was refused, so the HUD can say why.</summary>
    public sealed class CommandRejectedEvent : GameEvent
    {
        /// <summary>The command's type name, for example "PickHeroCommand".</summary>
        public string command = "";
        public string reason = "";
    }

    /// <summary>A player connected. Broadcast.</summary>
    public sealed class PlayerJoinedEvent : GameEvent
    {
        public int playerId;
        public TeamId team = TeamId.None;
        public string name = "";
        public bool rejoined;
    }

    /// <summary>A player left (their record stays, so they can rejoin). Broadcast.</summary>
    public sealed class PlayerLeftEvent : GameEvent
    {
        public int playerId;
        public string name = "";
    }

    /// <summary>A player changed team in the lobby. Broadcast.</summary>
    public sealed class TeamChangedEvent : GameEvent
    {
        public int playerId;
        public TeamId team = TeamId.None;
    }

    /// <summary>One row of the session's player list: what isn't match state (names, who hosts).</summary>
    [Serializable]
    public class SessionPlayer
    {
        public int id;
        public string name = "";
        public bool connected;
        public bool host;

        public SessionPlayer Clone() => (SessionPlayer)MemberwiseClone();
    }
}
