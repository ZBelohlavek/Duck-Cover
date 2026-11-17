namespace DuckAndCover.Core
{
    // Rule events published by the Combat module. Progression reads them for experience (who
    // damaged whom, healing and shielding given); Game and UI draw hit markers and health bars.

    /// <summary>A hero took damage. Published before the elimination it may cause.</summary>
    public sealed class DamagedEvent : GameEvent
    {
        /// <summary>The player credited, or -1 (the world).</summary>
        public int attackerId;
        public int targetId;
        /// <summary>Total taken after modifiers: health lost plus <see cref="absorbed"/>.</summary>
        public float amount;
        /// <summary>The part of <see cref="amount"/> that shields took.</summary>
        public float absorbed;
        public float healthLeft;
        /// <summary>The ability or weapon that did it, or empty.</summary>
        public string abilityId;
        /// <summary>The spawned object that did it (a turret, a mine), or 0.</summary>
        public int objectId;
        /// <summary>This damage took the hero down.</summary>
        public bool eliminated;
    }

    /// <summary>A hero got health back from an ability (not from passive regen).</summary>
    public sealed class HealedEvent : GameEvent
    {
        public int healerId;
        public int targetId;
        /// <summary>Health actually restored (overheal is not counted).</summary>
        public float amount;
        public float healthNow;
        public string abilityId;
        public int objectId;
    }

    /// <summary>A hero received a shield.</summary>
    public sealed class ShieldedEvent : GameEvent
    {
        public int sourceId;
        public int targetId;
        /// <summary>Shield actually added, after the cap.</summary>
        public float amount;
        public float seconds;
        public float shieldNow;
        public string abilityId;
        public int objectId;
    }
}
