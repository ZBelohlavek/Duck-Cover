namespace DuckAndCover.Core
{
    /// <summary>
    /// Core -> Net. The server's rules hand events and snapshots to this; the Net layer (WP3)
    /// decides how they travel. Core never knows which network library is underneath.
    /// </summary>
    public interface INetBridge
    {
        /// <summary>Send to every connected player.</summary>
        void Broadcast(GameEvent e);

        /// <summary>Send only to one team (pings, camera feeds, budgets).</summary>
        void SendToTeam(TeamId team, GameEvent e);

        /// <summary>Send a full state copy to one player (round start, late join, drift repair).</summary>
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
    /// A full copy of the match state for one player. Partial on purpose: each module adds its part
    /// in its own file (for example MatchSnapshot.Destruction.cs), so packages don't edit one file.
    /// </summary>
    public partial class MatchSnapshot
    {
        /// <summary>Server tick the snapshot was taken on.</summary>
        public int tick;
    }
}
