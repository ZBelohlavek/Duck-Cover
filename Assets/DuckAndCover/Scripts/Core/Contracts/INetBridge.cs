namespace DuckAndCover.Core
{
    /// <summary>
    /// Core -> Net. The server's rules hand events and snapshots to this; the Net layer decides how
    /// they travel. Core never knows which network library is underneath. Implemented by
    /// <see cref="SessionHost"/> (Core/Session), which encodes and routes over any transport.
    /// </summary>
    public interface INetBridge
    {
        /// <summary>Send to every connected player.</summary>
        void Broadcast(GameEvent e);

        /// <summary>Send only to one team (pings, camera feeds, budgets).</summary>
        void SendToTeam(TeamId team, GameEvent e);

        /// <summary>Send only to one player (a refused command, a private notice).</summary>
        void SendToPlayer(int playerId, GameEvent e);

        /// <summary>Send a full state copy to one player (join, late join, regular state updates).</summary>
        void SendSnapshot(int playerId, MatchSnapshot s);
    }

    /// <summary>
    /// Base of every rule event (a panel opened, a player went down, the kit was planted...).
    /// Each module adds its own subclasses in its own folder.
    /// </summary>
    public abstract class GameEvent
    {
        /// <summary>Server tick the event happened on.</summary>
        public int tick;
    }

    /// <summary>
    /// An event only one team may see (pings, a teammate's build). The session sends it to
    /// <see cref="audience"/> alone instead of broadcasting it.
    /// </summary>
    public abstract class TeamEvent : GameEvent
    {
        public TeamId audience = TeamId.None;
    }

    /// <summary>
    /// A full copy of the match state for one player. Partial on purpose: each module adds its part
    /// in its own file (for example MatchSnapshot.Destruction.cs), so packages don't edit one file.
    /// </summary>
    public partial class MatchSnapshot
    {
        /// <summary>Server tick the snapshot was taken on.</summary>
        public int tick;
    }
}
