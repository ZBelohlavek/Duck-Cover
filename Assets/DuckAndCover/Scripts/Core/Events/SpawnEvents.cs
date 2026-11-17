namespace DuckAndCover.Core
{
    // Rule events published by the Spawn module and hero pick.

    public sealed class HeroPickedEvent : GameEvent
    {
        public int playerId;
        public TeamId team;
        public string heroId;
    }

    public sealed class PlayerEliminatedEvent : GameEvent
    {
        public int playerId;
        public TeamId team;
        /// <summary>The player who caused it, or -1 (the world, a disconnect).</summary>
        public int killerId;
        public bool disconnected;
        /// <summary>Match time the player may return (the wave may add a little).</summary>
        public float respawnAt;
    }

    /// <summary>A player is in the world: at Deploy (initial) or back from a respawn wait.</summary>
    public sealed class PlayerSpawnedEvent : GameEvent
    {
        public int playerId;
        public TeamId team;
        /// <summary>The forward zone they came back at, or None for the base.</summary>
        public ZoneId zone;
        public bool initial;
    }
}
