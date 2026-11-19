using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    // Ability data. Plain [Serializable] classes with public fields and string kinds, so they load
    // with JsonUtility in Unity and System.Text.Json in tests, and the files stay readable.
    // Note for loaders: JsonUtility never leaves a nested class or array null (it fills in empty
    // ones), so the rules treat an empty kind or id as "not set".

    /// <summary>
    /// One ability: a cooldown with charges, an optional wind-up,
    /// a targeting shape, and a list of standard effects. A test ability needs nothing but this.
    /// </summary>
    [Serializable]
    public class AbilityDef
    {
        public string id = "";
        public string displayName = "";
        public string description = "";

        /// <summary>
        /// Seconds to recover one charge. Every ability runs on a cooldown (there is no ultimate charge),
        /// and most sit between <see cref="Ruleset.abilityCooldownFloor"/> and <see cref="Ruleset.abilityCooldownCeiling"/>.
        /// </summary>
        public float cooldown;
        /// <summary>
        /// Why this ability may recharge faster than <see cref="Ruleset.abilityCooldownFloor"/> (a held shield,
        /// a reload). Empty for every other ability; the ability rules test lists the exceptions.
        /// </summary>
        public string shortCooldownReason = "";
        /// <summary>Uses banked before the cooldown blocks it.</summary>
        public int charges = 1;
        /// <summary>Wind-up: the effects land this many seconds after the cast starts. A stun cancels it.</summary>
        public float castTime;
        /// <summary>
        /// How long the ability lasts: status, slow and shield effects that give no duration of their
        /// own use it, and a scripted ability stays active (its Tick runs) this long after landing.
        /// </summary>
        public float duration;

        public TargetingDef targeting = new TargetingDef();
        public EffectDef[] effects = new EffectDef[0];

        /// <summary>Optional custom behaviour registered with AbilityRunner.RegisterScript. Empty for data-only abilities.</summary>
        public string scriptId = "";

        /// <summary>Always on: takes no slot and is never cast. It is just <see cref="stats"/>.</summary>
        public bool passive;
        /// <summary>
        /// A passive's stat changes, or a held ability's while it is up (talents change them with the
        /// param "stat.NAME").
        /// </summary>
        public StatModifier[] stats = new StatModifier[0];

        /// <summary>
        /// Raised and lowered rather than cast (a shield held up): using it puts its object out, and
        /// using it again (or <see cref="UseAbilityCommand.lower"/>) takes it back. It spends no charge.
        /// The object keeps its health between raises and regains it while down
        /// (<see cref="ObjectDef.regen"/>); if it is destroyed, the slot waits out its cooldown before it can
        /// go up again. Stuns and going down lower it. <see cref="stats"/> apply while it is up.
        /// </summary>
        public bool held;
        /// <summary>A held ability: the hero can't fire their weapon while it is up.</summary>
        public bool blocksWeapon;
        /// <summary>
        /// Usable while stunned or silenced (a rage that shakes off control). A talent turns it on with the
        /// param "breaksControl" (op set, value 1).
        /// </summary>
        public bool breaksControl;

        public AbilityDef Clone()
        {
            var c = (AbilityDef)MemberwiseClone();
            c.targeting = (targeting ?? new TargetingDef()).Clone();
            c.effects = EffectDef.CloneAll(effects);
            c.stats = stats == null ? new StatModifier[0] : Array.ConvertAll(stats, s => s?.Clone());
            return c;
        }
    }

    /// <summary>Where an ability looks for targets.</summary>
    [Serializable]
    public class TargetingDef
    {
        /// <summary>One of <see cref="Shapes"/>: "self", "ray" or "sphere".</summary>
        public string shape = Shapes.Self;
        /// <summary>For spheres, one of <see cref="Origins"/>: around the caster or where the aim lands.</summary>
        public string origin = Origins.Caster;
        /// <summary>Ray length, or how far away an aimed sphere can land (metres).</summary>
        public float range;
        /// <summary>Sphere radius (metres), scaled by the caster's abilityRadius stat.</summary>
        public float radius;
        /// <summary>
        /// Rays: how many rays in a fan. Spheres: how many areas (the extra ones sit in a ring one radius
        /// around the first). A target caught by several takes the effects several times.
        /// </summary>
        public int count = 1;
        /// <summary>Width of a ray fan in degrees (left to right).</summary>
        public float spreadDegrees;
        /// <summary>Who counts as a target, one of <see cref="Affects"/>.</summary>
        public string affects = Affects.Enemies;
        /// <summary>Most targets per ray or area. 0 = no limit. Nearest the area's centre first, ties to the lower id, so it is deterministic.</summary>
        public int maxTargets;
        /// <summary>
        /// Aimed spheres: the centre drops to the floor below where the aim lands (a leap's landing spot),
        /// so aiming at a wall or into the air still lands on the ground.
        /// </summary>
        public bool ground;

        public TargetingDef Clone() => (TargetingDef)MemberwiseClone();
    }

    /// <summary>
    /// One standard effect. Which fields matter depends on <see cref="kind"/>:
    /// damage (amount), heal (amount), shield (amount, duration), status (status, amount = strength,
    /// duration), slow (amount = share of speed taken, duration), stun (duration), cleanse,
    /// push (amount = metres, or the most metres for a leap; duration = seconds; direction; lift = metres
    /// up at the top of the arc, so a push with only a lift is a knock-up), spawn (objectId), reload,
    /// healRounds (amount = health each round gives, radius = splash around the teammate hit).
    /// </summary>
    [Serializable]
    public class EffectDef
    {
        /// <summary>One of <see cref="EffectKinds"/>.</summary>
        public string kind = "";
        public float amount;
        public float duration;
        /// <summary>Seconds after the ability lands before this effect applies (a fuse, a delayed blast).</summary>
        public float delay;
        /// <summary>Status effects: overrides the status's tick interval. 0 = the status's own.</summary>
        public float interval;
        /// <summary>Status id for kind "status".</summary>
        public string status = "";
        /// <summary>Object definition id for kind "spawn".</summary>
        public string objectId = "";
        /// <summary>Push: one of <see cref="PushDirections"/>.</summary>
        public string direction = PushDirections.Aim;
        /// <summary>An area around each target, for kinds that reach past it (healRounds: the splash). 0 = none.</summary>
        public float radius;
        /// <summary>Push: metres the target rises at the top of the arc (a leap, a knock-up). 0 = level with the ground.</summary>
        public float lift;
        /// <summary>
        /// Overrides who this effect reaches: empty = the ability's targets; "self" = the caster once,
        /// whatever was hit; otherwise filters the ability's targets (<see cref="Affects"/>).
        /// </summary>
        public string affects = "";
        /// <summary>When set, the effect is off until a talent enables this tag (AbilityParamChange param "enable").</summary>
        public string tag = "";

        public EffectDef Clone() => (EffectDef)MemberwiseClone();

        public static EffectDef[] CloneAll(EffectDef[] list)
        {
            if (list == null) return new EffectDef[0];
            var c = new EffectDef[list.Length];
            for (int i = 0; i < list.Length; i++) c[i] = list[i]?.Clone();
            return c;
        }
    }

    /// <summary>A status effect: a timed set of stat changes, control flags and optional ticking effects.</summary>
    [Serializable]
    public class StatusDef
    {
        public string id = "";
        public string displayName = "";
        /// <summary>Harmful: cleanse removes it.</summary>
        public bool negative;
        /// <summary>Duration when the effect that applies it gives none.</summary>
        public float duration = 2f;
        /// <summary>Stat changes while active. Multiplied by stacks, and by the strength when <see cref="scaleWithStrength"/>.</summary>
        public StatModifier[] stats = new StatModifier[0];
        /// <summary>Multiply stat values by the strength (the effect's amount): a 0.3 slow on "moveSpeed percent -1" is -30%.</summary>
        public bool scaleWithStrength;
        /// <summary>Can't move, shoot or use abilities; cancels wind-ups.</summary>
        public bool stun;
        /// <summary>Can't use abilities.</summary>
        public bool silence;
        /// <summary>Can't move.</summary>
        public bool root;
        /// <summary>
        /// A ward: the next harmful status an enemy puts on the bearer is blocked, and the ward is used up
        /// (<see cref="StatusBlockedEvent"/>).
        /// </summary>
        public bool ward;
        /// <summary>Visible to the other team through walls (the Game layer draws it).</summary>
        public bool reveal;
        /// <summary>
        /// Hidden from the other team (a cloak): enemy turrets, minions and Bastions don't pick the bearer,
        /// and the Game layer doesn't draw them beyond Ruleset.stealthShimmerRange. It ends early when the
        /// bearer fires or uses an ability that reaches past themselves, which opens their ambush window
        /// (StatConditions.Ambush). A reveal status on the bearer wins over it.
        /// </summary>
        public bool stealth;
        /// <summary>What a second application does, one of <see cref="Stacking"/>.</summary>
        public string stacking = Stacking.Refresh;
        public int maxStacks = 1;
        /// <summary>Seconds between ticks of <see cref="tickEffects"/>. 0 = no ticking.</summary>
        public float interval;
        /// <summary>Applied to the bearer every interval, from whoever applied the status (damage or heal over time).</summary>
        public EffectDef[] tickEffects = new EffectDef[0];

        public StatusDef Clone()
        {
            var c = (StatusDef)MemberwiseClone();
            c.stats = stats == null ? new StatModifier[0] : Array.ConvertAll(stats, s => s?.Clone());
            c.tickEffects = EffectDef.CloneAll(tickEffects);
            return c;
        }
    }

    /// <summary>Something an ability places in the world: a barrier, a mine, a turret, a healing field.</summary>
    [Serializable]
    public class ObjectDef
    {
        public string id = "";
        public string displayName = "";
        /// <summary>Seconds before it disappears. 0 = until destroyed or replaced.</summary>
        public float lifetime;
        /// <summary>Hit points. 0 = can't be destroyed.</summary>
        public float health;
        /// <summary>Reach of its pulses and triggers (metres), scaled by the owner's abilityRadius stat.</summary>
        public float radius;
        /// <summary>One of <see cref="Triggers"/>: "none" (barriers), "pulse" (fields, turrets), "proximity" (mines).</summary>
        public string trigger = Triggers.None;
        /// <summary>Seconds between pulses.</summary>
        public float interval = 1f;
        /// <summary>Seconds after placing before it pulses or triggers.</summary>
        public float armTime;
        /// <summary>Who its effects reach, one of <see cref="Affects"/> (relative to the owner).</summary>
        public string affects = Affects.Enemies;
        /// <summary>Most targets per pulse or trigger. 0 = no limit.</summary>
        public int maxTargets;
        /// <summary>A mine is used up when it triggers.</summary>
        public bool consumeOnTrigger;
        /// <summary>Pulses only reach targets it can see (needs the world to implement ILineOfSightQuery).</summary>
        public bool needsLineOfSight;
        /// <summary>Most of these one player can have out; placing another removes the oldest. 0 = no limit.</summary>
        public int maxPerOwner;
        /// <summary>Stops movement (the Game layer gives it a collider).</summary>
        public bool blocksMovement;
        /// <summary>Stops shots and rays (the Game layer gives it a collider).</summary>
        public bool blocksShots;
        /// <summary>
        /// Size of a solid object (a barrier or gate): a wall this wide and high, <see cref="SolidDepth"/>
        /// thick, standing on its spot and facing the caster's aim. Shots and ability rays stop on it
        /// when <see cref="blocksShots"/> is set. 0 = no solid shape.
        /// </summary>
        public float width;
        public float height;

        /// <summary>
        /// Moves with its owner every tick (a shield held up, a drone at their shoulder): it stands
        /// <see cref="followAhead"/> metres along the way they face (negative = behind) and
        /// <see cref="followRise"/> metres above their feet, facing where they face.
        /// </summary>
        public bool followOwner;
        public float followAhead;
        public float followRise;
        /// <summary>
        /// Latch onto the owner's teammate (or the owner) nearest where it lands, within its radius, and
        /// follow them like <see cref="followOwner"/> follows the owner. Nobody in reach: it stays put.
        /// A talent turns it on with the param "object.followAlly" (op set, value 1).
        /// </summary>
        public bool followAlly;
        /// <summary>
        /// Above 0: a ball this big around its spot that shots and ability rays can hit (a drone).
        /// Its team's own shots pass it like their barriers. Balls never block line of sight.
        /// </summary>
        public float hitRadius;
        /// <summary>
        /// A wall that opens out: it appears this share of its <see cref="width"/> wide and reaches full
        /// width over <see cref="expandTime"/> seconds. 0 = full width at once.
        /// </summary>
        public float expandFrom;
        public float expandTime;
        /// <summary>Share of the damage it takes that goes back to whoever dealt it (0.25 = a quarter).</summary>
        public float reflect;
        /// <summary>Held abilities' objects: health regained per second while lowered, after <see cref="regenDelay"/> seconds down.</summary>
        public float regen;
        public float regenDelay;

        /// <summary>Thickness of every solid object (metres).</summary>
        public const float SolidDepth = 0.3f;

        /// <summary>Has a wall shape that blocks shots.</summary>
        public bool IsSolid => blocksShots && width > 0f && height > 0f;
        /// <summary>A ball that shots can hit (see <see cref="hitRadius"/>).</summary>
        public bool IsBall => hitRadius > 0f && health > 0f;
        public EffectDef[] effects = new EffectDef[0];

        public ObjectDef Clone()
        {
            var c = (ObjectDef)MemberwiseClone();
            c.effects = EffectDef.CloneAll(effects);
            return c;
        }
    }

    /// <summary>A data file of ability definitions (Data/Resources/Abilities/*.json). Its root object for JsonUtility.</summary>
    [Serializable]
    public class AbilityDataFile
    {
        public AbilityDef[] abilities = new AbilityDef[0];
        public StatusDef[] statuses = new StatusDef[0];
        public ObjectDef[] objects = new ObjectDef[0];
    }

    public static class EffectKinds
    {
        public const string Damage = "damage";
        public const string Heal = "heal";
        public const string Shield = "shield";
        public const string Status = "status";
        public const string Slow = "slow";
        public const string Stun = "stun";
        public const string Cleanse = "cleanse";
        public const string Push = "push";
        public const string Spawn = "spawn";
        /// <summary>Refills the magazine of the weapon in the target's hand (the combat server provides it).</summary>
        public const string Reload = "reload";
        /// <summary>
        /// Refills the target's magazine with healing rounds (the combat server provides it): until that
        /// magazine is spent or reloaded, a round that hits a teammate heals them by the amount.
        /// </summary>
        public const string HealRounds = "healRounds";

        public static readonly string[] All = { Damage, Heal, Shield, Status, Slow, Stun, Cleanse, Push, Spawn, Reload, HealRounds };
        public static bool IsKnown(string kind) => Array.IndexOf(All, kind) >= 0;
    }

    public static class Shapes
    {
        public const string Self = "self";
        public const string Ray = "ray";
        public const string Sphere = "sphere";
        public static bool IsKnown(string s) => s == Self || s == Ray || s == Sphere;
    }

    public static class Origins
    {
        /// <summary>Centred on the caster (the command's aim origin).</summary>
        public const string Caster = "caster";
        /// <summary>Where the aim ray meets the world, or at full range.</summary>
        public const string AimPoint = "aimPoint";
        public static bool IsKnown(string s) => s == Caster || s == AimPoint;
    }

    public static class Affects
    {
        public const string Enemies = "enemies";
        /// <summary>Teammates, not the caster.</summary>
        public const string Allies = "allies";
        public const string AlliesAndSelf = "alliesAndSelf";
        public const string Self = "self";
        public const string All = "all";
        public static bool IsKnown(string s) => s == Enemies || s == Allies || s == AlliesAndSelf || s == Self || s == All;
    }

    public static class Triggers
    {
        public const string None = "none";
        public const string Pulse = "pulse";
        public const string Proximity = "proximity";
        public static bool IsKnown(string s) => s == None || s == Pulse || s == Proximity;
    }

    public static class Stacking
    {
        /// <summary>The new application replaces the old (strength and time).</summary>
        public const string Refresh = "refresh";
        /// <summary>Adds a stack (up to maxStacks) and refreshes the time.</summary>
        public const string Stack = "stack";
        /// <summary>Keeps the stronger; an equal or stronger one refreshes the time.</summary>
        public const string Strongest = "strongest";
        /// <summary>A second application does nothing until the first ends.</summary>
        public const string Ignore = "ignore";
        public static bool IsKnown(string s) => s == Refresh || s == Stack || s == Strongest || s == Ignore;
    }

    public static class PushDirections
    {
        /// <summary>Along the caster's aim (a dash or a charge on the caster).</summary>
        public const string Aim = "aim";
        /// <summary>Away from where the ability landed (a knockback).</summary>
        public const string Away = "away";
        /// <summary>Toward where the ability landed (a pull).</summary>
        public const string Toward = "toward";
        /// <summary>
        /// To where the ability landed (a leap, on the caster): as far as that spot, flat, up to the
        /// effect's amount. Give it a lift so it arcs.
        /// </summary>
        public const string Leap = "leap";
        public static bool IsKnown(string s) => s == Aim || s == Away || s == Toward || s == Leap;
    }

    /// <summary>
    /// Optional extra the Game layer's world can implement. Turrets and other objects with
    /// needsLineOfSight use it; without it they reach everything in range.
    /// </summary>
    public interface ILineOfSightQuery
    {
        bool HasLineOfSight(Vec3 from, int playerId, int tick);
    }

    /// <summary>
    /// Optional extra the world can implement so a caster's rays and their objects' sight lines are
    /// judged like their shots: past the caster's own body and through their own team's barriers
    /// (with friendly fire off). <see cref="CombatWorld"/> implements it.
    /// </summary>
    public interface ICasterQuery
    {
        bool RaycastFor(int casterId, Vec3 origin, Vec3 dir, float max, int tick, out HitInfo hit);
        bool HasLineOfSightFor(int viewerId, Vec3 from, int playerId, int tick);
    }
}
