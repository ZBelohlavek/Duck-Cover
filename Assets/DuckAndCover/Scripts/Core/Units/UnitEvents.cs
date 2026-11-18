namespace DuckAndCover.Core
{
    // Rule events of the Units module (WP14). Damage, healing, shields and statuses on units use the
    // Combat and Abilities events with the unit's id as the target.

    /// <summary>A unit that is not a player came into the match.</summary>
    public sealed class UnitSpawnedEvent : GameEvent
    {
        public int unitId;
        public string typeId;
        public TeamId team;
        public Vec3 position;
        public float yaw;
        public float maxHealth;
    }

    /// <summary>A unit left the match: destroyed by damage, or removed by the rules.</summary>
    public sealed class UnitDestroyedEvent : GameEvent
    {
        public int unitId;
        public string typeId;
        public TeamId team;
        public Vec3 position;
        /// <summary>Who dealt the last damage (a player or a unit id), or -1.</summary>
        public int killerId;
        /// <summary>"killed" or "removed".</summary>
        public string reason;
    }
}
