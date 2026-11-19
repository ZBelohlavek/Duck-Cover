using System.Collections.Generic;

namespace DuckAndCover.Core
{
    // Numbers for the Abilities, Stats and Combat modules (WP7). Value fields only, so the base
    // Ruleset.Clone (MemberwiseClone) copies them. Validated by ValidateAbilities, which the
    // AbilityRunner calls; Ruleset.ValidateModules is left free for one other module.
    public partial class Ruleset
    {
        // ---- Health and shields ----
        /// <summary>Max health of a hero whose data doesn't set one.</summary>
        public float defaultMaxHealth = 200f;
        /// <summary>Most shield a hero can carry at once from all sources.</summary>
        public float maxShield = 150f;
        /// <summary>How long a shield lasts when the effect doesn't say.</summary>
        public float defaultShieldSeconds = 4f;
        /// <summary>Health regen (the healthRegen stat) starts this long after the last damage.</summary>
        public float regenDelaySeconds = 5f;
        /// <summary>At or below this share of max health a hero counts as "lowHealth" for conditional modifiers.</summary>
        public float lowHealthShare = 0.5f;
        /// <summary>
        /// The lane version of the point conditions (StatConditions.OnPoint, OnHeldPoint): a hero
        /// within this many metres of one of their own team's units (a minion or Bastion) counts as
        /// holding ground too, so upgrades that work on points also work in a lane mode (WP20).
        /// </summary>
        public float escortRadius = 8f;
        /// <summary>Whether damage effects and weapons can hurt teammates.</summary>
        public bool friendlyFire = false;

        // ---- Abilities ----
        /// <summary>Cooldowns and charges refill on respawn. Ultimate charge always carries over.</summary>
        public bool resetCooldownsOnRespawn = true;
        /// <summary>No talent can take a cooldown below this.</summary>
        public float minCooldownSeconds = 0.5f;
        /// <summary>Share of an ultimate's cost given back when a stun or death interrupts its wind-up.</summary>
        public float ultimateInterruptRefund = 0.5f;
        /// <summary>Ultimate charge gained per second while alive, from level 1 (100 = full for a cost-100 ultimate).</summary>
        public float ultimateChargePerSecond = 0.8f;
        /// <summary>Ultimate charge per point of damage dealt to enemies.</summary>
        public float ultimateChargePerDamage = 0.05f;
        /// <summary>Ultimate charge per point of healing or shield given to teammates.</summary>
        public float ultimateChargePerHealing = 0.05f;
        /// <summary>
        /// Share of the damage and healing charge above that counts when the other side is a unit (a
        /// minion, Bastion or camp guard) rather than a hero, so clearing waves doesn't bring ultimates
        /// round far faster than fighting heroes does (WP20).
        /// </summary>
        public float ultimateChargeUnitShare = 0.5f;
        /// <summary>Most charge a hero can bank.</summary>
        public float ultimateChargeMax = 100f;

        // ---- Status effects ----
        /// <summary>Slows can't take a hero below this share of their move speed.</summary>
        public float minMoveSpeed = 0.3f;
        /// <summary>Stuns longer than this are cut short.</summary>
        public float maxStunSeconds = 2f;
        /// <summary>Seconds of the ambush window that attacking out of stealth opens (StatConditions.Ambush), before the ambushSeconds stat.</summary>
        public float ambushSeconds = 2.5f;
        /// <summary>
        /// A stealthed enemy closer than this is drawn as a shimmer instead of not at all (the Game layer
        /// reads it), so a cloak can't stand on top of someone unseen.
        /// </summary>
        public float stealthShimmerRange = 5f;

        // ---- Spawned objects ----
        /// <summary>First id given to spawned objects (turrets, mines, barriers), clear of map device ids.</summary>
        public int firstObjectId = 10000;

        /// <summary>Problems with the ability numbers, empty when usable.</summary>
        public List<string> ValidateAbilities()
        {
            var e = new List<string>();
            if (defaultMaxHealth <= 0f) e.Add("defaultMaxHealth must be positive");
            if (maxShield < 0f) e.Add("maxShield can't be negative");
            if (defaultShieldSeconds <= 0f) e.Add("defaultShieldSeconds must be positive");
            if (regenDelaySeconds < 0f) e.Add("regenDelaySeconds can't be negative");
            if (lowHealthShare < 0f || lowHealthShare > 1f) e.Add("lowHealthShare must be between 0 and 1");
            if (minCooldownSeconds < 0f) e.Add("minCooldownSeconds can't be negative");
            if (ultimateInterruptRefund < 0f || ultimateInterruptRefund > 1f) e.Add("ultimateInterruptRefund must be between 0 and 1");
            if (ultimateChargePerSecond < 0f || ultimateChargePerDamage < 0f || ultimateChargePerHealing < 0f) e.Add("ultimate charge rates can't be negative");
            if (ultimateChargeUnitShare < 0f) e.Add("ultimateChargeUnitShare can't be negative");
            if (escortRadius < 0f) e.Add("escortRadius can't be negative");
            if (ultimateChargeMax <= 0f) e.Add("ultimateChargeMax must be positive");
            if (minMoveSpeed < 0f || minMoveSpeed > 1f) e.Add("minMoveSpeed must be between 0 and 1");
            if (maxStunSeconds <= 0f) e.Add("maxStunSeconds must be positive");
            if (ambushSeconds < 0f) e.Add("ambushSeconds can't be negative");
            if (stealthShimmerRange < 0f) e.Add("stealthShimmerRange can't be negative");
            if (firstObjectId < 1) e.Add("firstObjectId must be positive");
            return e;
        }
    }
}
