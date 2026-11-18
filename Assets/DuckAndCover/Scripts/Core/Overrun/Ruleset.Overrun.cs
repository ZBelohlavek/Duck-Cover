using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    public partial class Ruleset
    {
        /// <summary>Bastions, the late push and Overrun experience (WP16). Cloned by the Overrun rules at the start of a match.</summary>
        public OverrunRuleset overrun = new OverrunRuleset();

        /// <summary>
        /// The unit kind with this id among the lane kinds (minions, camp guards) and the Overrun kinds
        /// (Bastions), or null. Clients use it to size and name what they draw.
        /// </summary>
        public UnitDef UnitKind(string typeId)
        {
            if (lanes != null && lanes.units != null) foreach (var u in lanes.units) if (u != null && u.id == typeId) return u;
            if (overrun != null && overrun.units != null) foreach (var u in overrun.units) if (u != null && u.id == typeId) return u;
            return null;
        }
    }

    /// <summary>
    /// The numbers for Overrun (WP16): the two Bastion kinds, their shields, who may hurt them, how they
    /// shoot, the late push, and Overrun's experience. Starting values from the approved plan where it
    /// gives them (12 m, 10 s, 25 m, minute 11, 20 s waves, +50%, 2 s, 3 a second, 150), the rest set
    /// from the bot match report (dotnet run --project Tests/CoreSim -- report overrun), to tune in the
    /// balance pass.
    /// </summary>
    [Serializable]
    public class OverrunRuleset
    {
        public const string ForwardBastion = "bastion_forward";
        public const string CoreBastion = "bastion_core";
        /// <summary>The ability id Bastion shots carry in damage events.</summary>
        public const string AttackId = "bastion_attack";
        /// <summary>The ability id of a Bastion's own shield layer.</summary>
        public const string ShieldId = "bastion_shield";
        /// <summary>The stat source of the late push's extra damage on Bastions.</summary>
        public const string LatePushSource = "overrun_late_push";

        /// <summary>The Bastion kinds, given to the unit library when the Overrun rules start. Anchored: pushes don't move them.</summary>
        public UnitDef[] units =
        {
            new UnitDef { id = ForwardBastion, name = "Forward Bastion", maxHealth = 2000f, size = 2.2f, moveSpeed = 0f, anchored = true, attackDamage = 35f, attackSeconds = 1f, attackRange = 25f, sightRange = 25f },
            new UnitDef { id = CoreBastion, name = "Core Bastion", maxHealth = 3000f, size = 2.5f, moveSpeed = 0f, anchored = true, attackDamage = 45f, attackSeconds = 1f, attackRange = 25f, sightRange = 25f },
        };

        // ---- Shields ----
        public float forwardShield = 600f;
        public float coreShield = 900f;
        /// <summary>The shield comes back to full after this long with no enemy minion within <see cref="minionRadius"/>.</summary>
        public float shieldRefillSeconds = 10f;

        // ---- Who may hurt a Bastion ----
        /// <summary>
        /// Heroes (and their objects) hurt a Bastion only while one of their own team's minions is within
        /// this distance of it (metres, feet to feet). Enemy minions this close also hold its shield off.
        /// Minions hurt Bastions anywhere.
        /// </summary>
        public float minionRadius = 12f;
        /// <summary>Unit kinds that count as minions for both rules (they always damage Bastions).</summary>
        public string[] minionKinds = { LaneRuleset.Scrapper, LaneRuleset.Warden, LaneRuleset.Juggernaut };

        // ---- Bastion fire ----
        /// <summary>A hero who damages an enemy hero within a Bastion's range is its first target for this long.</summary>
        public float aggroSeconds = 3f;

        // ---- Late push ----
        /// <summary>From this match minute, waves leave every <see cref="latePushWaveSeconds"/> and Bastions take more damage.</summary>
        public float latePushMinute = 11f;
        public float latePushWaveSeconds = 20f;
        /// <summary>Extra damage Bastions take in the late push (0.5 = +50%).</summary>
        public float latePushDamageTaken = 0.5f;

        // ---- The fall of a Forward Bastion ----
        /// <summary>Seconds off the respawn wait of a team whose Forward Bastion has fallen.</summary>
        public float defenderRespawnBonus = 2f;

        // ---- The time cap ----
        /// <summary>
        /// Level at the cap on Bastions destroyed, health taken and all damage dealt to Bastions, the
        /// match plays on; after this long the team that killed more enemy minions wins.
        /// </summary>
        public float suddenDeathSeconds = 120f;

        // ---- Experience (Progression pays it; the scale matches xpPerCapture = 100) ----
        /// <summary>Per second for a living hero within Ruleset.escortRadius of one of their own minions (Overrun's "on the point").</summary>
        public float xpNearWavePerSecond = 3f;
        /// <summary>To whoever lands the killing blow on an enemy minion.</summary>
        public float xpMinionKill = 15f;
        /// <summary>Per point of damage a hero deals to an enemy Bastion (health and shield).</summary>
        public float xpPerBastionDamage = 0.05f;
        /// <summary>To every hero of the team that destroys a Bastion.</summary>
        public float xpBastionDestroyed = 150f;

        public bool IsMinion(string typeId) => Array.IndexOf(minionKinds ?? new string[0], typeId) >= 0;

        public UnitDef Kind(BastionTier tier)
        {
            string id = tier == BastionTier.Core ? CoreBastion : ForwardBastion;
            foreach (var u in units ?? new UnitDef[0]) if (u != null && u.id == id) return u;
            return null;
        }

        public float Shield(BastionTier tier) => tier == BastionTier.Core ? coreShield : forwardShield;

        public OverrunRuleset Clone()
        {
            var c = (OverrunRuleset)MemberwiseClone();
            c.units = Array.ConvertAll(units ?? new UnitDef[0], u => u.Clone());
            c.minionKinds = (string[])(minionKinds ?? new string[0]).Clone();
            return c;
        }

        /// <summary>Problems with the numbers, empty when usable.</summary>
        public List<string> Validate()
        {
            var e = new List<string>();
            var lib = UnitLibrary.From(new UnitDataFile { units = units ?? new UnitDef[0] });
            e.AddRange(lib.Validate());
            foreach (var tier in new[] { BastionTier.Forward, BastionTier.Core })
            {
                var k = Kind(tier);
                if (k == null) { e.Add("units needs a '" + (tier == BastionTier.Core ? CoreBastion : ForwardBastion) + "' kind"); continue; }
                if (k.moveSpeed != 0f || !k.anchored) e.Add(k.id + ": a Bastion stands still (moveSpeed 0, anchored)");
                if (k.attackDamage <= 0f) e.Add(k.id + ": a Bastion needs attackDamage");
            }
            if (forwardShield < 0f || coreShield < 0f) e.Add("Bastion shields can't be negative");
            if (shieldRefillSeconds < 0f) e.Add("shieldRefillSeconds can't be negative");
            if (minionRadius <= 0f) e.Add("minionRadius must be positive");
            if (minionKinds == null || minionKinds.Length == 0) e.Add("minionKinds needs at least one kind");
            if (aggroSeconds < 0f) e.Add("aggroSeconds can't be negative");
            if (latePushMinute < 0f || latePushWaveSeconds <= 0f || latePushDamageTaken < 0f) e.Add("late push numbers are wrong");
            if (suddenDeathSeconds < 0f) e.Add("suddenDeathSeconds can't be negative");
            if (defenderRespawnBonus < 0f) e.Add("defenderRespawnBonus can't be negative");
            if (xpNearWavePerSecond < 0f || xpMinionKill < 0f || xpPerBastionDamage < 0f || xpBastionDestroyed < 0f) e.Add("Overrun experience can't be negative");
            return e;
        }
    }
}
