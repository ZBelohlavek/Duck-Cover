namespace DuckAndCover.Core
{
    // Rule events published by the Zones module.

    /// <summary>A zone went live (points unlock after Deploy; a new hill in king of the hill).</summary>
    public sealed class ZoneActivatedEvent : GameEvent
    {
        public ZoneId zone;
        public TeamId owner;
    }

    /// <summary>A hill moved on. The zone is neutral again and no longer scores.</summary>
    public sealed class ZoneDeactivatedEvent : GameEvent
    {
        public ZoneId zone;
    }

    /// <summary>King of the hill: the next live zone, announced rotationWarningSeconds ahead.</summary>
    public sealed class ZoneUpcomingEvent : GameEvent
    {
        public ZoneId zone;
        public float secondsUntilLive;
    }

    /// <summary>A team took a neutral zone.</summary>
    public sealed class ZoneCapturedEvent : GameEvent
    {
        public ZoneId zone;
        public TeamId team;
        /// <summary>Living players of the team on the zone when it fell (experience is shared among them).</summary>
        public int[] players;
    }

    /// <summary>A team knocked an enemy zone back to neutral.</summary>
    public sealed class ZoneNeutralisedEvent : GameEvent
    {
        public ZoneId zone;
        public TeamId team;
        public TeamId previousOwner;
        public int[] players;
    }

    /// <summary>Both teams are on the zone (contested = true), or one of them left it.</summary>
    public sealed class ZoneContestChangedEvent : GameEvent
    {
        public ZoneId zone;
        public bool contested;
    }
}
