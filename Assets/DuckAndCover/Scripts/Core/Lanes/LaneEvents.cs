namespace DuckAndCover.Core
{
    // Rule events of the Lanes module (WP15). Minion and guard shots show up as DamagedEvent with
    // abilityId LaneRuleset.AttackId; minions and guards coming and going as UnitSpawnedEvent and
    // UnitDestroyedEvent.

    /// <summary>A team's wave left its end of a lane.</summary>
    public sealed class WaveSpawnedEvent : GameEvent
    {
        public TeamId team;
        public string laneId;
        /// <summary>Counts from 1 for each team.</summary>
        public int wave;
        public int[] unitIds;
        /// <summary>A Juggernaut walks with this wave.</summary>
        public bool juggernaut;
    }

    /// <summary>A camp's guards are all down: its pad can be taken.</summary>
    public sealed class CampOpenedEvent : GameEvent
    {
        public string campId;
        public CampKind kind;
    }

    /// <summary>A team took a camp. <see cref="heroes"/> got its reward (helpers for experience, the heroes on the pad for speed).</summary>
    public sealed class CampTakenEvent : GameEvent
    {
        public string campId;
        public CampKind kind;
        public TeamId team;
        public int[] heroes;
        /// <summary>Seconds until its guards are back.</summary>
        public float respawnSeconds;
    }

    /// <summary>A camp's guards are back.</summary>
    public sealed class CampRespawnedEvent : GameEvent
    {
        public string campId;
        public CampKind kind;
    }

    /// <summary>A hero's camp boost ran out (or was replaced by a fresh one, which comes with its own CampTakenEvent).</summary>
    public sealed class CampBoostEndedEvent : GameEvent
    {
        public int playerId;
        public CampKind kind;
    }
}
