namespace DuckAndCover.Core
{
    // Rule events of the Overrun module (WP16). Bastion shots show up as DamagedEvent with abilityId
    // OverrunRuleset.AttackId; Bastions appearing as UnitSpawnedEvent and falling as UnitDestroyedEvent
    // too, with BastionDestroyedEvent saying which one it was.

    /// <summary>A Bastion's shield is gone: from here its health takes the damage.</summary>
    public sealed class BastionShieldBrokenEvent : GameEvent
    {
        public TeamId team;
        public BastionTier tier;
        public int unitId;
    }

    /// <summary>A Bastion's shield came back to full (no enemy minions near it for a while).</summary>
    public sealed class BastionShieldRestoredEvent : GameEvent
    {
        public TeamId team;
        public BastionTier tier;
        public int unitId;
        public float shield;
    }

    /// <summary>
    /// A Bastion fell. <see cref="team"/> is the side that lost it; <see cref="byTeam"/> scores. When
    /// a Forward Bastion falls, the side gate into its team's core plaza opens and its Core Bastion can be hurt.
    /// </summary>
    public sealed class BastionDestroyedEvent : GameEvent
    {
        public TeamId team;
        public BastionTier tier;
        public int unitId;
        public TeamId byTeam;
        /// <summary>Who landed the last hit (a player or unit id), or -1.</summary>
        public int killerId;
    }

    /// <summary>The side gate into <see cref="team"/>'s core plaza opened (its Forward Bastion fell).</summary>
    public sealed class SideGateOpenedEvent : GameEvent
    {
        public TeamId team;
    }

    /// <summary>The late push began: waves every <see cref="waveSeconds"/> and Bastions take <see cref="extraDamage"/> more damage.</summary>
    public sealed class LatePushStartedEvent : GameEvent
    {
        public float waveSeconds;
        public float extraDamage;
    }
}
