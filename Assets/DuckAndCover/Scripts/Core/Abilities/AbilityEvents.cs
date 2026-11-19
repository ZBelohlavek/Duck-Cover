namespace DuckAndCover.Core
{
    // Rule events published by the Abilities module. The caster's client shows the wind-up at once;
    // these confirm or correct it, and tell everyone else what to draw.

    /// <summary>The loadout of a hero changed (hero pick, a talent that swaps an ability).</summary>
    public sealed class LoadoutSetEvent : GameEvent
    {
        public int playerId;
        public string[] abilityIds;
        public string passiveId;
    }

    /// <summary>An ability with a wind-up started. It lands <see cref="castTime"/> seconds later unless interrupted.</summary>
    public sealed class AbilityCastStartedEvent : GameEvent
    {
        public int playerId;
        public int slot;
        public string abilityId;
        public float castTime;
        public Vec3 aimOrigin;
        public Vec3 aimDirection;
    }

    /// <summary>A wind-up was interrupted (a stun, or the caster went down).</summary>
    public sealed class AbilityCancelledEvent : GameEvent
    {
        public int playerId;
        public int slot;
        public string abilityId;
        public string reason;
    }

    /// <summary>An ability landed: its effects were applied on this tick (delayed effects follow).</summary>
    public sealed class AbilityUsedEvent : GameEvent
    {
        public int playerId;
        public int slot;
        public string abilityId;
        public Vec3 aimOrigin;
        public Vec3 aimDirection;
        /// <summary>Where each ray or area landed.</summary>
        public Vec3[] points;
        /// <summary>Everyone caught, in area order (a player can appear more than once).</summary>
        public int[] targets;
        /// <summary>Charges left and seconds until the next one, so clients needn't wait for a snapshot.</summary>
        public int chargesLeft;
        public float rechargeLeft;
    }

    /// <summary>A hero went into stealth or came out of it (a stealth status began or ended, or a reveal covered it).</summary>
    public sealed class StealthChangedEvent : GameEvent
    {
        public int playerId;
        public bool stealthed;
    }

    /// <summary>A hero attacked out of stealth: for <see cref="seconds"/> their "ambush" upgrades count.</summary>
    public sealed class AmbushEvent : GameEvent
    {
        public int playerId;
        public float seconds;
    }

    /// <summary>The server refused an ability request. Sent so the caster's client can undo its prediction.</summary>
    public sealed class AbilityRejectedEvent : GameEvent
    {
        public int playerId;
        public int slot;
        public string abilityId;
        public string reason;
    }

    public sealed class StatusAppliedEvent : GameEvent
    {
        public int targetId;
        public int sourceId;
        public string statusId;
        public float strength;
        public int stacks;
        public float seconds;
        public string abilityId;
    }

    public sealed class StatusEndedEvent : GameEvent
    {
        public int targetId;
        public string statusId;
        /// <summary>"expired", "cleansed", "eliminated", "respawned" or "used" (a ward that blocked something).</summary>
        public string reason;
    }

    /// <summary>A ward on the target stopped a harmful status (and was used up).</summary>
    public sealed class StatusBlockedEvent : GameEvent
    {
        public int targetId;
        public int sourceId;
        /// <summary>The harmful status that was stopped.</summary>
        public string statusId;
        /// <summary>The ward that stopped it.</summary>
        public string wardId;
        public string abilityId;
    }

    /// <summary>
    /// A hero is shoved (or dashes: a push on the caster along their aim). Movement belongs to the
    /// Game layer, so it carries this out. <see cref="direction"/> is filled in when the rules know
    /// where the hero stands; otherwise it is zero and the Game layer works it out from
    /// <see cref="mode"/> and <see cref="origin"/>.
    /// </summary>
    public sealed class PushedEvent : GameEvent
    {
        public int targetId;
        public int sourceId;
        /// <summary>One of PushDirections: "aim", "away" or "toward".</summary>
        public string mode;
        public Vec3 origin;
        public Vec3 direction;
        public float distance;
        public float seconds;
        public string abilityId;
    }

    public sealed class ObjectSpawnedEvent : GameEvent
    {
        public int objectId;
        public string defId;
        public int ownerId;
        public TeamId team;
        public string abilityId;
        public Vec3 position;
        /// <summary>Level direction it faces; a wall stands across it.</summary>
        public Vec3 facing;
        public float radius;
        public float lifetime;
        public float health;
        public bool blocksMovement;
        public bool blocksShots;
        /// <summary>Wall size for barriers and gates (ObjectDef.width and height), 0 for the rest.</summary>
        public float width;
        public float height;
    }

    /// <summary>A mine went off or a field pulsed and reached someone.</summary>
    public sealed class ObjectTriggeredEvent : GameEvent
    {
        public int objectId;
        public string defId;
        public Vec3 position;
        public int[] targets;
    }

    public sealed class ObjectDamagedEvent : GameEvent
    {
        public int objectId;
        public int attackerId;
        public float amount;
        public float healthLeft;
    }

    public sealed class ObjectRemovedEvent : GameEvent
    {
        public int objectId;
        public string defId;
        public int ownerId;
        /// <summary>"expired", "destroyed", "triggered", "replaced" or "ownerLeft".</summary>
        public string reason;
        /// <summary>Who destroyed it, or -1.</summary>
        public int byPlayerId = -1;
    }

    /// <summary>A hero's ultimate became ready.</summary>
    public sealed class UltimateReadyEvent : GameEvent
    {
        public int playerId;
        public string abilityId;
    }

    /// <summary>A killing blow paid a kill reward (<see cref="KillRewardDef"/>) on one of the hero's abilities or their passive.</summary>
    public sealed class KillRewardEvent : GameEvent
    {
        public int playerId;
        /// <summary>The ability or passive whose reward it was.</summary>
        public string abilityId;
        public int victimId;
        /// <summary>One of <see cref="KillVictims"/>.</summary>
        public string victim;
        /// <summary>Notches the ability holds now (0 when the reward gives none).</summary>
        public int notches;
    }
}
