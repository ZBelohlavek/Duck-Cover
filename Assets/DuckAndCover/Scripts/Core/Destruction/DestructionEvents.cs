using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>Cells changed on the server. Broadcast to everyone; WP5 rebuilds the surfaces it names,
    /// clients mirror it with <see cref="DestructionRules.ApplyChanges"/>.</summary>
    public class CellsChangedEvent : GameEvent
    {
        public List<CellChange> changes = new List<CellChange>();
        /// <summary>What caused it, for effects (sparks, blast, hammering boards).</summary>
        public CellChangeCause cause;
        /// <summary>The gadget or tool, when there is one (from <see cref="DamageRequest.sourceTag"/>).</summary>
        public string sourceTag;
        public TeamId team = TeamId.None;
        public int playerId = -1;
    }

    public enum CellChangeCause : byte
    {
        Bullet = 0,
        Melee = 1,
        Grenade = 2,
        StandardCharge = 3,
        HeavyCharge = 4,
        Patch = 10,
        Reinforce = 11,
        ZoneLock = 12,
        MapEvolution = 13,
    }

    /// <summary>A zone's integrity changed. <see cref="warning"/> turns the wall meter on near the floor.</summary>
    public class ZoneIntegrityEvent : GameEvent
    {
        public int zoneId;
        public float integrity;
        public float floor;
        public bool warning;
    }

    /// <summary>A zone hit its floor: its remaining cells are locked for the rest of the match.</summary>
    public class ZoneLockedEvent : GameEvent
    {
        public int zoneId;
    }
}
