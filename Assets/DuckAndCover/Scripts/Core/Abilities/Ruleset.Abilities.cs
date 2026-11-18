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
        /// <summary>Whether damage effects and weapons can hurt teammates.</summary>
        public bool friendlyFire = false;

        // ---- Abilities ----
        /// <summary>Cooldowns and charges refill on respawn. Ultimate charge always carries over.</summary>
        public bool resetCooldownsOnRespawn = true;
        /// <summary>No talent can take a cooldown below this.</summary>
        public float minCooldownSeconds = 0.5f;
        /// <summary>Share of an ultimate's cost given back when a stun or death interrupts its wind-up.</summary>
        public float ultimateInterruptRefund = 0.5f;
        /// <summary>Player level at which ultimates unlock (the plan: level 5).</summary>
        public int ultimateUnlockLevel = 8;
        /// <summary>Ultimate charge gained per second while alive, once unlocked (100 = full for a cost-100 ultimate).</summary>
        public float ultimateChargePerSecond = 0.8f;
        /// <summary>Ultimate charge per point of damage dealt to enemies.</summary>
        public float ultimateChargePerDamage = 0.05f;
        /// <summary>Ultimate charge per point of healing or shield given to teammates.</summary>
        public float ultimateChargePerHealing = 0.05f;
        /// <summary>Most charge a hero can bank.</summary>
        public float ultimateChargeMax = 100f;

        // ---- Status effects ----
        /// <summary>Slows can't take a hero below this share of their move speed.</summary>
        public float minMoveSpeed = 0.3f;
        /// <summary>Stuns longer than this are cut short.</summary>
        public float maxStunSeconds = 2f;

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
            if (ultimateUnlockLevel < 1) e.Add("ultimateUnlockLevel must be at least 1");
            if (ultimateChargePerSecond < 0f || ultimateChargePerDamage < 0f || ultimateChargePerHealing < 0f) e.Add("ultimate charge rates can't be negative");
            if (ultimateChargeMax <= 0f) e.Add("ultimateChargeMax must be positive");
            if (minMoveSpeed < 0f || minMoveSpeed > 1f) e.Add("minMoveSpeed must be between 0 and 1");
            if (maxStunSeconds <= 0f) e.Add("maxStunSeconds must be positive");
            if (firstObjectId < 1) e.Add("firstObjectId must be positive");
            return e;
        }
    }
}
