using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// Every ability, status and object definition a match can use, by id. Starts with the built-in
    /// statuses (slow, stun, root, silence); data files add to it and may replace a built-in by id.
    /// </summary>
    public sealed class AbilityLibrary
    {
        public const string SlowStatus = "slow";
        public const string StunStatus = "stun";
        public const string RootStatus = "root";
        public const string SilenceStatus = "silence";

        readonly Dictionary<string, AbilityDef> abilities = new Dictionary<string, AbilityDef>();
        readonly Dictionary<string, StatusDef> statuses = new Dictionary<string, StatusDef>();
        readonly Dictionary<string, ObjectDef> objects = new Dictionary<string, ObjectDef>();
        readonly List<string> loadProblems = new List<string>();

        public AbilityLibrary()
        {
            foreach (var s in BuiltinStatuses()) statuses[s.id] = s;
        }

        public static AbilityLibrary From(params AbilityDataFile[] files)
        {
            var lib = new AbilityLibrary();
            foreach (var f in files) lib.Add(f);
            return lib;
        }

        public IEnumerable<AbilityDef> Abilities => abilities.Values;
        public IEnumerable<StatusDef> Statuses => statuses.Values;
        public IEnumerable<ObjectDef> Objects => objects.Values;

        public void Add(AbilityDataFile file)
        {
            if (file == null) return;
            if (file.abilities != null) foreach (var a in file.abilities) Add(a);
            if (file.statuses != null) foreach (var s in file.statuses) Add(s);
            if (file.objects != null) foreach (var o in file.objects) Add(o);
        }

        public void Add(AbilityDef a)
        {
            if (a == null || string.IsNullOrEmpty(a.id)) { loadProblems.Add("an ability has no id"); return; }
            if (abilities.ContainsKey(a.id)) loadProblems.Add("ability '" + a.id + "' is defined twice");
            abilities[a.id] = a;
        }

        public void Add(StatusDef s)
        {
            if (s == null || string.IsNullOrEmpty(s.id)) { loadProblems.Add("a status has no id"); return; }
            statuses[s.id] = s; // data may replace a built-in
        }

        public void Add(ObjectDef o)
        {
            if (o == null || string.IsNullOrEmpty(o.id)) { loadProblems.Add("an object has no id"); return; }
            if (objects.ContainsKey(o.id)) loadProblems.Add("object '" + o.id + "' is defined twice");
            objects[o.id] = o;
        }

        public AbilityDef Ability(string id) => id != null && abilities.TryGetValue(id, out var a) ? a : null;
        public StatusDef Status(string id) => id != null && statuses.TryGetValue(id, out var s) ? s : null;
        public ObjectDef Object(string id) => id != null && objects.TryGetValue(id, out var o) ? o : null;

        /// <summary>The status an effect applies: its own for kind "status", the built-ins for slow and stun.</summary>
        public static string StatusIdFor(EffectDef e)
        {
            if (e == null) return null;
            if (e.kind == EffectKinds.Slow) return SlowStatus;
            if (e.kind == EffectKinds.Stun) return StunStatus;
            if (e.kind == EffectKinds.Status) return e.status;
            return null;
        }

        /// <summary>Everything wrong with the definitions. Empty when every ability can run.</summary>
        public List<string> Validate(ICollection<string> knownScripts = null)
        {
            var errors = new List<string>(loadProblems);
            foreach (var a in abilities.Values)
            {
                string w = "ability '" + a.id + "': ";
                if (a.passive)
                {
                    if (a.stats == null || a.stats.Length == 0) errors.Add(w + "a passive needs stats");
                    if (a.effects != null && a.effects.Length > 0) errors.Add(w + "a passive is never cast, so it can't have effects");
                    ValidateStats(a.stats, w, errors);
                    ValidateKills(a, w, errors);
                    continue;
                }
                ValidateKills(a, w, errors);
                if (!a.held && a.stats != null && a.stats.Length > 0) errors.Add(w + "stats only work on a passive or a held ability");
                if (a.cooldown < 0f) errors.Add(w + "cooldown can't be negative");
                if (!a.ultimate && a.cooldown <= 0f) errors.Add(w + "needs a cooldown (or ultimate: true)");
                if (a.charges < 1) errors.Add(w + "charges must be at least 1");
                if (a.ultimate && a.ultimateCost <= 0f) errors.Add(w + "ultimateCost must be positive");
                if (a.castTime < 0f || a.duration < 0f) errors.Add(w + "castTime and duration can't be negative");
                var t = a.targeting ?? new TargetingDef();
                if (!Shapes.IsKnown(t.shape)) errors.Add(w + "unknown targeting shape '" + t.shape + "'");
                if (!Origins.IsKnown(t.origin)) errors.Add(w + "unknown targeting origin '" + t.origin + "'");
                if (!Affects.IsKnown(t.affects)) errors.Add(w + "unknown targeting affects '" + t.affects + "'");
                if (t.shape == Shapes.Ray && t.range <= 0f) errors.Add(w + "a ray needs a range");
                if (t.shape == Shapes.Sphere && t.radius <= 0f) errors.Add(w + "a sphere needs a radius");
                if (t.shape == Shapes.Arc && (t.range <= 0f || t.spreadDegrees <= 0f)) errors.Add(w + "an arc needs a range and a spreadDegrees");
                if (t.shape == Shapes.Sphere && t.origin == Origins.AimPoint && t.range <= 0f) errors.Add(w + "an aimed sphere needs a range");
                if (t.ground && (t.shape != Shapes.Sphere || t.origin != Origins.AimPoint)) errors.Add(w + "ground only works on an aimed sphere");
                if (t.count < 1) errors.Add(w + "count must be at least 1");
                if (t.maxTargets < 0 || t.spreadDegrees < 0f) errors.Add(w + "maxTargets and spreadDegrees can't be negative");
                bool hasScript = !string.IsNullOrEmpty(a.scriptId);
                if (hasScript && knownScripts != null && !knownScripts.Contains(a.scriptId)) errors.Add(w + "no script registered as '" + a.scriptId + "'");
                if (!hasScript && (a.effects == null || a.effects.Length == 0)) errors.Add(w + "has no effects and no script");
                ValidateEffects(a.effects, w, errors, 0);
                if (a.held)
                {
                    if (a.ultimate || a.passive) errors.Add(w + "a held ability can't be an ultimate or a passive");
                    var held = HeldObject(a);
                    if (held == null) errors.Add(w + "a held ability needs to spawn an object");
                    else if (held.health <= 0f) errors.Add(w + "a held ability's object needs health");
                    if (a.stats != null && a.stats.Length > 0) ValidateStats(a.stats, w, errors);
                }
                else if (a.blocksWeapon) errors.Add(w + "blocksWeapon only works on a held ability");
            }
            foreach (var s in statuses.Values)
            {
                string w = "status '" + s.id + "': ";
                if (s.duration <= 0f) errors.Add(w + "duration must be positive");
                if (!Stacking.IsKnown(s.stacking)) errors.Add(w + "unknown stacking '" + s.stacking + "'");
                if (s.maxStacks < 1) errors.Add(w + "maxStacks must be at least 1");
                if (s.interval < 0f) errors.Add(w + "interval can't be negative");
                if (s.tickEffects != null && s.tickEffects.Length > 0 && s.interval <= 0f) errors.Add(w + "tick effects need an interval");
                ValidateStats(s.stats, w, errors);
                ValidateEffects(s.tickEffects, w, errors, 1);
            }
            foreach (var o in objects.Values)
            {
                string w = "object '" + o.id + "': ";
                if (o.lifetime < 0f || o.health < 0f || o.radius < 0f || o.armTime < 0f) errors.Add(w + "lifetime, health, radius and armTime can't be negative");
                if (!Triggers.IsKnown(o.trigger)) errors.Add(w + "unknown trigger '" + o.trigger + "'");
                if (!Affects.IsKnown(o.affects)) errors.Add(w + "unknown affects '" + o.affects + "'");
                if (o.followAlly && o.radius <= 0f) errors.Add(w + "following an ally needs a radius to find one in");
                if (o.trigger == Triggers.Pulse && o.interval <= 0f) errors.Add(w + "a pulse needs an interval");
                if (o.trigger != Triggers.None && o.radius <= 0f) errors.Add(w + "a pulse or trigger needs a radius");
                if (o.trigger != Triggers.None && (o.effects == null || o.effects.Length == 0)) errors.Add(w + "a pulse or trigger needs effects");
                if (o.maxPerOwner < 0 || o.maxTargets < 0) errors.Add(w + "maxPerOwner and maxTargets can't be negative");
                if (o.lifetime == 0f && o.health == 0f && o.maxPerOwner == 0) errors.Add(w + "would last forever: give it a lifetime, health or maxPerOwner");
                if (o.hitRadius < 0f || o.expandFrom < 0f || o.expandFrom > 1f || o.expandTime < 0f || o.reflect < 0f || o.regen < 0f || o.regenDelay < 0f)
                    errors.Add(w + "hitRadius, expandTime, reflect, regen and regenDelay can't be negative, and expandFrom is a share (0 to 1)");
                if (o.hitRadius > 0f && o.health <= 0f) errors.Add(w + "a ball shots can hit needs health");
                ValidateEffects(o.effects, w, errors, 1);
            }
            return errors;
        }

        void ValidateKills(AbilityDef a, string w, List<string> errors)
        {
            if (a.notchDamage < 0f || a.maxNotches < 0) errors.Add(w + "notchDamage and maxNotches can't be negative");
            if (a.onKill == null) return;
            foreach (var k in a.onKill)
            {
                if (k == null) { errors.Add(w + "a kill reward is missing"); continue; }
                string x = w + "kill reward '" + k.name + "': ";
                if (string.IsNullOrEmpty(k.victims)) errors.Add(x + "needs victims");
                else foreach (var v in k.victims.Split(',')) if (!KillVictims.IsKnown(v.Trim())) errors.Add(x + "unknown victim '" + v.Trim() + "'");
                if (!KillScopes.IsKnown(k.scope)) errors.Add(x + "unknown scope '" + k.scope + "'");
                if (k.notches < 0) errors.Add(x + "notches can't be negative");
                if (k.notches > 0 && (a.passive || a.maxNotches <= 0)) errors.Add(x + "gives notches, but the ability holds none (maxNotches)");
                if (a.passive && k.scope != KillScopes.Any) errors.Add(x + "a passive deals no damage of its own, so its scope must be 'any'");
                if (k.notches == 0 && (k.effects == null || k.effects.Length == 0)) errors.Add(x + "does nothing (no notches or effects)");
                ValidateEffects(k.effects, x, errors, 1);
            }
        }

        void ValidateEffects(EffectDef[] effects, string w, List<string> errors, int depth)
        {
            if (effects == null) return;
            foreach (var e in effects)
            {
                if (e == null || string.IsNullOrEmpty(e.kind)) { errors.Add(w + "an effect has no kind"); continue; }
                string x = w + e.kind + ": ";
                if (!EffectKinds.IsKnown(e.kind)) { errors.Add(w + "unknown effect kind '" + e.kind + "'"); continue; }
                if (e.amount < 0f || e.duration < 0f || e.delay < 0f || e.interval < 0f || e.radius < 0f) errors.Add(x + "amount, duration, delay, interval and radius can't be negative");
                if (!string.IsNullOrEmpty(e.affects) && !Affects.IsKnown(e.affects)) errors.Add(x + "unknown affects '" + e.affects + "'");
                switch (e.kind)
                {
                    case EffectKinds.Damage:
                    case EffectKinds.Heal:
                    case EffectKinds.Shield:
                    case EffectKinds.HealRounds:
                        if (e.amount <= 0f) errors.Add(x + "needs an amount");
                        break;
                    case EffectKinds.Slow:
                        if (e.amount <= 0f || e.amount > 1f) errors.Add(x + "amount is the share of speed taken, between 0 and 1");
                        break;
                    case EffectKinds.Stun:
                        if (e.duration <= 0f) errors.Add(x + "needs a duration");
                        break;
                    case EffectKinds.Status:
                        if (Status(e.status) == null) errors.Add(x + "unknown status '" + e.status + "'");
                        break;
                    case EffectKinds.Push:
                        if (e.amount <= 0f && e.lift <= 0f) errors.Add(x + "needs a distance (amount) or a lift");
                        if (e.lift < 0f) errors.Add(x + "lift can't be negative");
                        if (!PushDirections.IsKnown(e.direction)) errors.Add(x + "unknown direction '" + e.direction + "'");
                        break;
                    case EffectKinds.Spawn:
                        if (depth > 0) errors.Add(x + "objects and statuses can't spawn objects");
                        else if (Object(e.objectId) == null) errors.Add(x + "unknown object '" + e.objectId + "'");
                        break;
                }
            }
        }

        static void ValidateStats(StatModifier[] stats, string w, List<string> errors)
        {
            if (stats == null) return;
            foreach (var m in stats)
            {
                if (m == null || string.IsNullOrEmpty(m.stat)) { errors.Add(w + "a stat change has no stat"); continue; }
                if (m.op != ModOp.Add && m.op != ModOp.Percent && m.op != ModOp.Multiply) errors.Add(w + "stat op '" + m.op + "' must be add, percent or multiply");
                if (!StatConditions.IsKnown(m.when)) errors.Add(w + "unknown condition '" + m.when + "'");
            }
        }

        /// <summary>slow, stun, root and silence: the statuses the standard effects use.</summary>
        public static StatusDef[] BuiltinStatuses() => new[]
        {
            new StatusDef
            {
                id = SlowStatus, displayName = "Slowed", negative = true, duration = 2f,
                stats = new[] { new StatModifier(StatIds.MoveSpeed, ModOp.Percent, -1f) },
                scaleWithStrength = true, stacking = Stacking.Strongest,
            },
            new StatusDef { id = StunStatus, displayName = "Stunned", negative = true, duration = 1f, stun = true, stacking = Stacking.Strongest },
            new StatusDef { id = RootStatus, displayName = "Rooted", negative = true, duration = 1.5f, root = true, stacking = Stacking.Strongest },
            new StatusDef { id = SilenceStatus, displayName = "Silenced", negative = true, duration = 2f, silence = true, stacking = Stacking.Strongest },
        };

        /// <summary>The object a held ability puts out: the first object it spawns. Null if none.</summary>
        public ObjectDef HeldObject(AbilityDef a)
        {
            if (a == null || a.effects == null) return null;
            foreach (var e in a.effects)
                if (e != null && e.kind == EffectKinds.Spawn) return Object(e.objectId);
            return null;
        }
    }
}
