using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>A temporary layer of extra health. Damage eats shields before health.</summary>
    [Serializable]
    public class ShieldState
    {
        public int sourceId = -1;
        public string abilityId = "";
        public float amount;
        public float timeLeft;

        public ShieldState Clone() => (ShieldState)MemberwiseClone();
    }

    /// <summary>A status effect on a hero right now.</summary>
    [Serializable]
    public class StatusInstance
    {
        public string statusId = "";
        /// <summary>Who applied it (credited for its damage or healing), or -1.</summary>
        public int sourceId = -1;
        /// <summary>The applying effect's amount: the share of a slow, for example. 1 when unused.</summary>
        public float strength = 1f;
        public int stacks = 1;
        public float timeLeft;
        /// <summary>The full duration it was last applied with (for HUD bars).</summary>
        public float duration;
        public float interval;
        public float tickIn;

        public StatusInstance Clone() => (StatusInstance)MemberwiseClone();
    }

    /// <summary>One ability slot of a hero: what is in it, its charges and its cast in progress.</summary>
    [Serializable]
    public class AbilitySlotState
    {
        public string abilityId = "";
        public int charges;
        /// <summary>Cooldown seconds until the next charge comes back. 0 when full.</summary>
        public float rechargeLeft;
        /// <summary>Winding up: the effects land when <see cref="castLeft"/> reaches 0.</summary>
        public bool casting;
        public float castLeft;
        /// <summary>Seconds a scripted ability stays active after landing.</summary>
        public float activeLeft;
        public Vec3 aimOrigin;
        public Vec3 aimDirection;
        public int castTick;

        public AbilitySlotState Clone() => (AbilitySlotState)MemberwiseClone();
    }

    /// <summary>
    /// A hero's fighting state for one match: health, shields, stats, statuses, ability slots and the
    /// talent changes taken. Plain data; DamageRules and AbilityRunner change it. Units that are not
    /// players (WP14) have one too, with no slots: health, shields, stats and statuses work the same.
    /// </summary>
    [Serializable]
    public class HeroUnit
    {
        /// <summary>The unit id: a player's id, or a unit's from <see cref="UnitIds.First"/> up.</summary>
        public int playerId;
        public float health;
        /// <summary>Seconds since this hero last took damage (health regen waits on it).</summary>
        public float sinceDamaged;
        public float ultimateCharge;
        /// <summary>Hidden from the other team right now: a stealth status is on and no reveal status (AbilityRunner keeps it).</summary>
        public bool stealthed;
        /// <summary>Seconds left in the ambush window that opens when the hero attacks out of stealth (StatConditions.Ambush).</summary>
        public float ambushLeft;
        /// <summary>The hero's passive (an ability definition marked passive), or empty.</summary>
        public string passiveId = "";
        public StatBlock stats = new StatBlock();
        public List<ShieldState> shields = new List<ShieldState>();
        public List<StatusInstance> statuses = new List<StatusInstance>();
        public List<AbilitySlotState> slots = new List<AbilitySlotState>();
        /// <summary>Every ability parameter change taken this match, in pick order.</summary>
        public List<AbilityParamChange> changes = new List<AbilityParamChange>();

        public float Shield
        {
            get { float s = 0f; foreach (var sh in shields) s += sh.amount; return s; }
        }

        public StatusInstance Status(string statusId)
        {
            foreach (var s in statuses) if (s.statusId == statusId) return s;
            return null;
        }

        public bool HasStatus(string statusId) => Status(statusId) != null;

        public HeroUnit Clone()
        {
            var c = (HeroUnit)MemberwiseClone();
            c.stats = stats.Clone();
            c.shields = shields.ConvertAll(s => s.Clone());
            c.statuses = statuses.ConvertAll(s => s.Clone());
            c.slots = slots.ConvertAll(s => s.Clone());
            c.changes = changes.ConvertAll(s => s.Clone());
            return c;
        }
    }
}
