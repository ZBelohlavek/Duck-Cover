namespace DuckAndCover.Core
{
    // Flag Rush events (WP21). Everyone sees all of them: the flag's place is never a secret.

    /// <summary>The next flag has its spot: it appears there in <see cref="seconds"/>.</summary>
    public sealed class FlagUpcomingEvent : GameEvent
    {
        public int spot;
        public Vec3 position;
        public float seconds;
    }

    /// <summary>The flag is on the map at a flag spot.</summary>
    public sealed class FlagAppearedEvent : GameEvent
    {
        public int spot;
        public Vec3 position;
    }

    /// <summary>A player touched the flag and carries it.</summary>
    public sealed class FlagTakenEvent : GameEvent
    {
        public int playerId;
        public TeamId team;
        public Vec3 position;
        /// <summary>Picked up where it fell rather than at its spot.</summary>
        public bool fromDrop;
    }

    /// <summary>The carrier went down (or left): the flag lies where they were.</summary>
    public sealed class FlagDroppedEvent : GameEvent
    {
        public int playerId;
        public TeamId team;
        public Vec3 position;
        /// <summary>Who brought the carrier down, or -1.</summary>
        public int killerId = -1;
        /// <summary>Seconds until it vanishes if nobody touches it.</summary>
        public float resetSeconds;
    }

    /// <summary>A dropped flag lay untouched too long and vanished; a new one is on its way.</summary>
    public sealed class FlagResetEvent : GameEvent
    {
        public Vec3 position;
    }

    /// <summary>The carrier reached the enemy base: their team scores one.</summary>
    public sealed class FlagCapturedEvent : GameEvent
    {
        public int playerId;
        public TeamId team;
        public int scoreA;
        public int scoreB;
        /// <summary>Teammates near the carrier when they scored.</summary>
        public int[] assists;
    }
}
