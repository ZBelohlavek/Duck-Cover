using System;

namespace DuckAndCover.Core
{
    /// <summary>
    /// One change to a stat, written by a trait, talent, passive or status effect. Plain data so
    /// upgrade and status files can list them (JsonUtility: strings, not enums, so the files stay
    /// readable). Final value = (base + sum of Add) * (1 + sum of Percent) * product of Multiply.
    /// </summary>
    [Serializable]
    public class StatModifier
    {
        /// <summary>Which stat, one of <see cref="StatIds"/> (any string works; unknown stats start at 0).</summary>
        public string stat = "";
        /// <summary>How to combine, one of <see cref="ModOp"/>: "add", "percent" or "multiply".</summary>
        public string op = ModOp.Add;
        /// <summary>Add: amount. Percent: 0.1 for +10%. Multiply: 0.7 for x0.7.</summary>
        public float value;
        /// <summary>Only counts while this holds, one of <see cref="StatConditions"/>. Empty = always.</summary>
        public string when = "";

        public StatModifier() { }

        public StatModifier(string stat, string op, float value, string when = "")
        {
            this.stat = stat;
            this.op = op;
            this.value = value;
            this.when = when ?? "";
        }

        public StatModifier Clone() => (StatModifier)MemberwiseClone();

        public override string ToString() =>
            stat + " " + op + " " + value + (string.IsNullOrEmpty(when) ? "" : " when " + when);
    }

    /// <summary>
    /// How a <see cref="StatModifier"/> or <see cref="AbilityParamChange"/> combines with the value
    /// it changes. Strings so data files stay readable.
    /// </summary>
    public static class ModOp
    {
        /// <summary>Adds the value (flat).</summary>
        public const string Add = "add";
        /// <summary>Adds a share: 0.1 is +10%. Percents add together before they multiply.</summary>
        public const string Percent = "percent";
        /// <summary>Multiplies: 0.7 is x0.7. Multipliers stack by multiplying.</summary>
        public const string Multiply = "multiply";
        /// <summary>Replaces the value (ability parameters only).</summary>
        public const string Set = "set";

        public static bool IsKnown(string op) => op == Add || op == Percent || op == Multiply || op == Set;

        /// <summary>Applies one change on its own to a single value (ability parameters).</summary>
        public static float Apply(float current, string op, float value)
        {
            switch (op)
            {
                case Add: return current + value;
                case Percent: return current * (1f + value);
                case Multiply: return current * value;
                case Set: return value;
                default: return current;
            }
        }
    }

    /// <summary>
    /// The stats the rules read. Strings, so data can name them; a stat nobody sets starts at
    /// <see cref="DefaultBase"/>. Multiplier stats (rates, dealt and taken) start at 1.
    /// </summary>
    public static class StatIds
    {
        /// <summary>Health when full. The base comes from the hero (or Ruleset.defaultMaxHealth).</summary>
        public const string MaxHealth = "maxHealth";
        /// <summary>Movement speed multiplier. The Game layer multiplies the hero's run speed by it.</summary>
        public const string MoveSpeed = "moveSpeed";
        /// <summary>Multiplier on all damage this hero deals.</summary>
        public const string DamageDealt = "damageDealt";
        /// <summary>Multiplier on all damage this hero takes.</summary>
        public const string DamageTaken = "damageTaken";
        /// <summary>Multiplier on healing this hero gives.</summary>
        public const string HealingDone = "healingDone";
        /// <summary>Multiplier on healing this hero receives.</summary>
        public const string HealingTaken = "healingTaken";
        /// <summary>Multiplier on shields this hero gives.</summary>
        public const string ShieldStrength = "shieldStrength";
        /// <summary>Cooldown seconds recovered per second (1.2 = cooldowns run 20% faster).</summary>
        public const string CooldownRate = "cooldownRate";
        /// <summary>Multiplier on ultimate charge gained.</summary>
        public const string UltimateChargeRate = "ultimateChargeRate";
        /// <summary>Multiplier on ability area radii (sphere targeting and spawned objects).</summary>
        public const string AbilityRadius = "abilityRadius";
        /// <summary>Multiplier on status durations this hero applies.</summary>
        public const string StatusDuration = "statusDuration";
        /// <summary>Health regained per second, when not damaged recently (read by Combat).</summary>
        public const string HealthRegen = "healthRegen";
        /// <summary>Weapon numbers, read by the weapon rules (WP4).</summary>
        public const string FireRate = "fireRate";
        public const string ReloadSpeed = "reloadSpeed";
        public const string WeaponDamage = "weaponDamage";
        /// <summary>Multiplier on hitscan reach: the weapon's range and the distances its damage falls off over.</summary>
        public const string WeaponRange = "weaponRange";
        /// <summary>Multiplier on magazine size (1.25 = a quarter more rounds).</summary>
        public const string MagazineSize = "magazineSize";
        /// <summary>Share of weapon damage dealt to enemies that heals the shooter (0.1 = 10%).</summary>
        public const string LifeSteal = "lifeSteal";
        /// <summary>Metres each weapon hit pushes an enemy along the shot (0 = none).</summary>
        public const string WeaponPush = "weaponPush";
        /// <summary>Above 0: weaponPush only shoves enemies this many metres away or closer (a point-blank knockback). 0 = any distance.</summary>
        public const string WeaponPushRange = "weaponPushRange";
        /// <summary>Extra healing given to someone at low health (Ruleset.lowHealthShare or less), as a share (0.3 = 30% more).</summary>
        public const string HealingToLowHealth = "healingToLowHealth";
        /// <summary>Above 0: a healing weapon's hits on teammates also clear their harmful statuses.</summary>
        public const string WeaponCleanse = "weaponCleanse";
        /// <summary>How much each weapon hit slows an enemy, as a share of speed (0.15 = 15% slower; 0 = none).</summary>
        public const string WeaponSlow = "weaponSlow";
        /// <summary>
        /// How many times a healing round that heals a teammate jumps on to the next hurt teammate nearby
        /// (Ruleset.healRicochetRange, Ruleset.healRicochetShare of the heal). 0 = none.
        /// </summary>
        public const string HealRicochet = "healRicochet";
        /// <summary>Capture speed multiplier on points, for the zone rules to read.</summary>
        public const string CaptureSpeed = "captureSpeed";
        /// <summary>Seconds added to the ambush window (Ruleset.ambushSeconds) that attacking out of stealth opens.</summary>
        public const string AmbushSeconds = "ambushSeconds";

        /// <summary>The starting value of a stat that the hero doesn't set.</summary>
        public static float DefaultBase(string stat)
        {
            switch (stat)
            {
                case MaxHealth:
                case HealthRegen:
                    return 0f;
                case MoveSpeed:
                case DamageDealt:
                case DamageTaken:
                case HealingDone:
                case HealingTaken:
                case ShieldStrength:
                case CooldownRate:
                case UltimateChargeRate:
                case AbilityRadius:
                case StatusDuration:
                case FireRate:
                case ReloadSpeed:
                case WeaponDamage:
                case MagazineSize:
                case WeaponRange:
                case CaptureSpeed:
                    return 1f;
                default:
                    return 0f;
            }
        }
    }

    /// <summary>When a conditional <see cref="StatModifier"/> counts. Empty means always.</summary>
    public static class StatConditions
    {
        public const string Always = "";
        /// <summary>Standing on a live point the hero's team holds, or (in lane modes) beside their own minions (Ruleset.escortRadius).</summary>
        public const string OnHeldPoint = "onHeldPoint";
        /// <summary>Standing on any live point, or (in lane modes) beside their own minions (Ruleset.escortRadius).</summary>
        public const string OnPoint = "onPoint";
        /// <summary>Health at or below half.</summary>
        public const string LowHealth = "lowHealth";
        /// <summary>Has a shield up.</summary>
        public const string Shielded = "shielded";
        /// <summary>Hidden by a stealth status (and not revealed).</summary>
        public const string Stealthed = "stealthed";
        /// <summary>Just attacked out of stealth: Ruleset.ambushSeconds plus the ambushSeconds stat.</summary>
        public const string Ambush = "ambush";

        public static bool IsKnown(string when) =>
            string.IsNullOrEmpty(when) || when == OnHeldPoint || when == OnPoint || when == LowHealth || when == Shielded
            || when == Stealthed || when == Ambush;
    }
}
