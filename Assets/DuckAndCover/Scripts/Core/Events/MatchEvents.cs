namespace DuckAndCover.Core
{
    // Rule events published by the Match module. Net forwards them, Game and UI draw them, and other
    // modules react to them (Progression grants experience from zone and elimination events).

    public sealed class MatchStartedEvent : GameEvent
    {
        public string modeId;
    }

    public sealed class PhaseChangedEvent : GameEvent
    {
        public MatchPhase from;
        public MatchPhase to;
        /// <summary>Length of the new phase in seconds, or 0 for phases with no countdown (Play, Overtime).</summary>
        public float seconds;
    }

    /// <summary>The first team reached the mode's mapShiftScore: the map opens its new route. Fires once.</summary>
    public sealed class MapShiftEvent : GameEvent
    {
        public TeamId team;
        public int score;
    }

    public sealed class OvertimeStartedEvent : GameEvent
    {
        /// <summary>The team that reached the cap (or led at the time cap).</summary>
        public TeamId leader;
    }

    public sealed class MatchEndedEvent : GameEvent
    {
        public TeamId winner;
        public MatchEndReason reason;
        public int scoreA;
        public int scoreB;
        /// <summary>Seconds of play, overtime included.</summary>
        public float elapsed;
    }
}
