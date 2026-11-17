using System;

namespace DuckAndCover.Core
{
    /// <summary>One zone of the mode's zone set and who is winning it.</summary>
    [Serializable]
    public class ZoneState
    {
        public ZoneId id;
        /// <summary>Live: can be captured and scores. Locked zones keep their owner but do nothing.</summary>
        public bool active;
        public TeamId owner = TeamId.None;
        /// <summary>0..1 of a capture (on a neutral zone) or a neutralise (on an enemy zone).</summary>
        public float progress;
        /// <summary>The team the progress belongs to, or None when progress is 0.</summary>
        public TeamId progressTeam = TeamId.None;
        /// <summary>Both teams have living players on the zone; progress is frozen.</summary>
        public bool contested;
        /// <summary>Living players of each team on the zone, as of the last tick.</summary>
        public int countA;
        public int countB;
        /// <summary>From the map: the team that may respawn here while holding it.</summary>
        public TeamId forwardSpawnFor = TeamId.None;
        /// <summary>From the map: the owner whenever the zone goes live.</summary>
        public TeamId startOwner = TeamId.None;

        public int Count(TeamId team) => team == TeamId.A ? countA : team == TeamId.B ? countB : 0;

        /// <summary>The team is on this zone and it isn't safely theirs: they're attacking or defending it.</summary>
        public bool IsFightingOn(TeamId team) =>
            active && Count(team) > 0 && (owner != team || contested || progress > 0f);

        public ZoneState Clone() => (ZoneState)MemberwiseClone();
    }
}
