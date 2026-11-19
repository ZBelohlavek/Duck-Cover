using System;

namespace DuckAndCover.Core
{
    /// <summary>
    /// A hero as data (Data/Resources/Heroes/*.json): role, weapon, two abilities, ultimate, passive, and the two
    /// upgrades offered at each choice level. A fifth hero costs a data file and art, not engine code.
    /// Loaded with JsonUtility in Unity, so only plain public fields and arrays.
    /// </summary>
    [Serializable]
    public class HeroDef
    {
        public string id = "";
        public string displayName = "";
        /// <summary>vanguard, striker, support or controller.</summary>
        public string role = "";
        public string weaponId = "";
        /// <summary>Health when full, before upgrades. 0 = Ruleset.defaultMaxHealth.</summary>
        public float maxHealth;
        /// <summary>The two ability ids (AbilityDef ids from the Abilities module).</summary>
        public string[] abilities = new string[0];
        /// <summary>The third ability (key X), usable from level 1 like the other two. It may run on ultimate charge or on a cooldown. The level-8 "ultimate" pick upgrades it.</summary>
        public string ultimateId = "";
        /// <summary>
        /// An always-on passive ability in the base kit, or empty. The starter heroes have none: their
        /// passive is an upgrade choice (kind "passive") partway through the match.
        /// </summary>
        public string passiveId = "";
        /// <summary>One entry per choice level (2 to 10 with the plan's numbers).</summary>
        public HeroLevelChoice[] levels = new HeroLevelChoice[0];
        /// <summary>Every trait, talent, ultimate form and capstone the levels offer.</summary>
        public UpgradeDef[] upgrades = new UpgradeDef[0];

        public UpgradeDef Upgrade(string upgradeId)
        {
            if (upgrades == null || string.IsNullOrEmpty(upgradeId)) return null;
            foreach (var u in upgrades) if (u != null && u.id == upgradeId) return u;
            return null;
        }

        public HeroLevelChoice Level(int level)
        {
            if (levels == null) return null;
            foreach (var l in levels) if (l != null && l.level == level) return l;
            return null;
        }

        /// <summary>True when the id is one of the hero's own abilities, ultimate or passive.</summary>
        public bool OwnsAbility(string abilityId)
        {
            if (string.IsNullOrEmpty(abilityId)) return false;
            if (abilityId == ultimateId || abilityId == passiveId) return true;
            if (abilities != null) foreach (var a in abilities) if (a == abilityId) return true;
            return false;
        }
    }

    /// <summary>The upgrades offered at one level. options[0] is the left choice, options[1] the right.</summary>
    [Serializable]
    public class HeroLevelChoice
    {
        public int level;
        public string[] options = new string[0];
    }

    /// <summary>
    /// What a trait, talent, ultimate form or capstone does. Pure data in most cases: stat changes for
    /// the modifier stack and parameter changes for the hero's abilities (both owned by WP7), plus an
    /// optional script id for anything unusual. The rules never special-case a named upgrade.
    /// </summary>
    [Serializable]
    public class UpgradeDef
    {
        public string id = "";
        public string displayName = "";
        public string description = "";
        /// <summary>trait, talent, ultimate, passive or capstone (see <see cref="UpgradeKinds"/>).</summary>
        public string kind = "";
        public StatModifier[] stats = new StatModifier[0];
        public AbilityParamChange[] abilityChanges = new AbilityParamChange[0];
        /// <summary>Custom behaviour for the unusual cases, or empty.</summary>
        public string scriptId = "";

        public UpgradeKind Kind => UpgradeKinds.Parse(kind);
    }

    public enum UpgradeKind
    {
        None = 0,
        /// <summary>A general passive any hero of the role could take.</summary>
        Trait = 1,
        /// <summary>Changes how one of the hero's own abilities works.</summary>
        Talent = 2,
        /// <summary>One of two forms of the hero's ultimate.</summary>
        Ultimate = 3,
        /// <summary>The level 10 choice that ties the build together.</summary>
        Capstone = 4,
        /// <summary>The hero's passive, picked partway through the match (stats only, like a trait).</summary>
        Passive = 5,
    }

    /// <summary>Data files spell kinds as lower-case words; this maps them to <see cref="UpgradeKind"/>.</summary>
    public static class UpgradeKinds
    {
        public const string Trait = "trait";
        public const string Talent = "talent";
        public const string Ultimate = "ultimate";
        public const string Capstone = "capstone";
        public const string Passive = "passive";

        public static UpgradeKind Parse(string kind)
        {
            switch (kind)
            {
                case Trait: return UpgradeKind.Trait;
                case Talent: return UpgradeKind.Talent;
                case Ultimate: return UpgradeKind.Ultimate;
                case Capstone: return UpgradeKind.Capstone;
                case Passive: return UpgradeKind.Passive;
                default: return UpgradeKind.None;
            }
        }
    }
}
