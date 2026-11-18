using System;

namespace DuckAndCover.Core
{
    /// <summary>Where the Flag Rush flag is in its life.</summary>
    public enum FlagStatus
    {
        /// <summary>Not on the map: it appears at <see cref="FlagState.nextSpot"/> at <see cref="FlagState.appearAt"/>.</summary>
        Waiting = 0,
        /// <summary>Standing at a flag spot, untouched since it appeared.</summary>
        AtSpot = 1,
        /// <summary>A player has it (<see cref="FlagState.carrierId"/>).</summary>
        Carried = 2,
        /// <summary>On the ground where its carrier went down; it resets at <see cref="FlagState.resetAt"/>.</summary>
        Dropped = 3,
    }

    /// <summary>
    /// The one flag of a Flag Rush match (WP21). Plain data: <see cref="FlagServer"/> changes it,
    /// snapshots copy it (<see cref="MatchSnapshot.flag"/>), Game and UI draw it.
    /// </summary>
    [Serializable]
    public class FlagState
    {
        public FlagStatus status = FlagStatus.Waiting;
        /// <summary>Where it is: its spot, where it fell, or the carrier's feet. Meaningless while Waiting.</summary>
        public Vec3 position;
        /// <summary>The spot it appeared at last, or -1 before the first.</summary>
        public int spot = -1;
        /// <summary>The spot it appears at next, chosen (and shown) as soon as it is scheduled; -1 before Play.</summary>
        public int nextSpot = -1;
        /// <summary>Where <see cref="nextSpot"/> is, so clients needn't know the map's spot list.</summary>
        public Vec3 nextPosition;
        /// <summary>Match time (MatchState.elapsed) it appears, while Waiting.</summary>
        public float appearAt;
        /// <summary>Match time a dropped flag vanishes, while Dropped.</summary>
        public float resetAt;
        /// <summary>The player carrying it, or -1.</summary>
        public int carrierId = -1;
        public TeamId carrierTeam = TeamId.None;
        /// <summary>Captures so far, both teams (the score has them per team).</summary>
        public int captures;

        public bool OnMap => status != FlagStatus.Waiting;
        public bool IsCarrier(int playerId) => status == FlagStatus.Carried && carrierId == playerId && playerId >= 0;

        public FlagState Clone() => (FlagState)MemberwiseClone();
    }
}
