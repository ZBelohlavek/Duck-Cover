using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>An ability as one hero has it right now: the definition with their talent changes applied.</summary>
    public sealed class ResolvedAbility
    {
        public AbilityDef def;
        /// <summary>The objects this ability spawns, with the changes applied, by object id.</summary>
        public readonly Dictionary<string, ObjectDef> objects = new Dictionary<string, ObjectDef>();
        /// <summary>Effect tags that talents turned on.</summary>
        public readonly HashSet<string> enabled = new HashSet<string>();

        public bool IsOn(EffectDef e) => e != null && (string.IsNullOrEmpty(e.tag) || enabled.Contains(e.tag));
    }

    /// <summary>
    /// Applies <see cref="AbilityParamChange"/>s to an ability definition. Per parameter:
    /// (base + adds) * (1 + percents) * multipliers, unless a "set" is present, which wins (the last
    /// one taken). The rules never special-case a named talent; they only read the result.
    ///
    /// Numeric params:
    ///   cooldown, charges, castTime, duration, ultimateCost, range, radius, count, spreadDegrees, maxTargets
    ///   effect.KEY.amount | duration | delay | interval
    ///   object.lifetime | health | radius | interval | armTime | maxPerOwner | maxTargets | width | height
    ///          | reflect | regen | regenDelay | hitRadius
    ///   object.effect.KEY.amount | duration | delay | interval
    ///   stat.NAME   (a passive's stat change, e.g. stat.healthRegen)
    /// where KEY is an effect's kind ("damage", "heal", "slow"...), its status id or its tag; every
    /// matching effect changes.
    /// Text params (op "set", value in text): targeting.shape, targeting.origin, targeting.affects, object.affects
    /// Switch: enable (text = an effect tag on the ability or its objects).
    /// </summary>
    public static class AbilityParams
    {
        public const string Enable = "enable";

        sealed class Slot
        {
            public Func<float> get;
            public Action<float> set;
            public bool whole;
            public float min;
        }

        /// <summary>What is wrong with a change, or null when it can be applied. For hero data validation (WP8).</summary>
        public static string Check(AbilityParamChange c, AbilityLibrary lib)
        {
            if (c == null) return "a change is missing";
            var def = lib.Ability(c.ability);
            if (def == null) return "unknown ability '" + c.ability + "'";
            string w = c.ability + "." + c.param + ": ";
            var r = Build(def, lib);
            if (c.param == Enable)
            {
                if (string.IsNullOrEmpty(c.text)) return w + "needs the tag to enable in text";
                return HasTag(r, c.text) ? null : w + "no effect is tagged '" + c.text + "'";
            }
            if (IsTextParam(c.param))
            {
                if (c.op != ModOp.Set) return w + "text parameters only take op 'set'";
                if (c.param.StartsWith("object.") && r.objects.Count == 0) return w + "the ability spawns no object";
                return TextIsValid(c.param, c.text) ? null : w + "'" + c.text + "' is not a valid value";
            }
            if (!ModOp.IsKnown(c.op)) return w + "unknown op '" + c.op + "'";
            if (float.IsNaN(c.value) || float.IsInfinity(c.value)) return w + "value is not a number";
            return Find(r, c.param).Count > 0 ? null : w + "the ability has no such parameter";
        }

        /// <summary>The definition with the changes for this ability applied. Changes that don't fit are skipped.</summary>
        public static ResolvedAbility Resolve(AbilityDef baseDef, IEnumerable<AbilityParamChange> changes, AbilityLibrary lib, float minCooldown = 0f)
        {
            var r = Build(baseDef, lib);
            if (changes == null) return r;

            var byParam = new Dictionary<string, List<AbilityParamChange>>();
            var order = new List<string>();
            foreach (var c in changes)
            {
                if (c == null || c.ability != baseDef.id) continue;
                if (c.param == Enable) { if (!string.IsNullOrEmpty(c.text)) r.enabled.Add(c.text); continue; }
                if (IsTextParam(c.param))
                {
                    if (c.op == ModOp.Set && TextIsValid(c.param, c.text)) SetText(r, c.param, c.text);
                    continue;
                }
                if (!byParam.TryGetValue(c.param, out var list)) { byParam[c.param] = list = new List<AbilityParamChange>(); order.Add(c.param); }
                list.Add(c);
            }

            foreach (var param in order)
            {
                float add = 0f, pct = 0f, mul = 1f, setTo = 0f;
                bool hasSet = false;
                foreach (var c in byParam[param])
                {
                    switch (c.op)
                    {
                        case ModOp.Add: add += c.value; break;
                        case ModOp.Percent: pct += c.value; break;
                        case ModOp.Multiply: mul *= c.value; break;
                        case ModOp.Set: setTo = c.value; hasSet = true; break;
                    }
                }
                foreach (var slot in Find(r, param))
                {
                    float v = hasSet ? setTo : (slot.get() + add) * Math.Max(0f, 1f + pct) * mul;
                    if (slot.whole) v = (float)Math.Round(v);
                    slot.set(Math.Max(slot.min, v));
                }
            }
            // Talents can't push a cooldown under the floor (an ability that starts under it keeps its own).
            float floor = Math.Min(baseDef.cooldown, minCooldown);
            if (r.def.cooldown < floor) r.def.cooldown = floor;
            return r;
        }

        static ResolvedAbility Build(AbilityDef baseDef, AbilityLibrary lib)
        {
            var r = new ResolvedAbility { def = baseDef.Clone() };
            foreach (var e in r.def.effects)
            {
                if (e == null || e.kind != EffectKinds.Spawn || r.objects.ContainsKey(e.objectId)) continue;
                var o = lib.Object(e.objectId);
                if (o != null) r.objects[e.objectId] = o.Clone();
            }
            return r;
        }

        static bool IsTextParam(string param) =>
            param == "targeting.shape" || param == "targeting.origin" || param == "targeting.affects" || param == "object.affects";

        static bool TextIsValid(string param, string text)
        {
            switch (param)
            {
                case "targeting.shape": return Shapes.IsKnown(text);
                case "targeting.origin": return Origins.IsKnown(text);
                default: return Affects.IsKnown(text);
            }
        }

        static void SetText(ResolvedAbility r, string param, string text)
        {
            switch (param)
            {
                case "targeting.shape": r.def.targeting.shape = text; break;
                case "targeting.origin": r.def.targeting.origin = text; break;
                case "targeting.affects": r.def.targeting.affects = text; break;
                case "object.affects": foreach (var o in r.objects.Values) o.affects = text; break;
            }
        }

        static bool HasTag(ResolvedAbility r, string tag)
        {
            foreach (var e in r.def.effects) if (e != null && e.tag == tag) return true;
            foreach (var o in r.objects.Values) foreach (var e in o.effects) if (e != null && e.tag == tag) return true;
            return false;
        }

        static List<Slot> Find(ResolvedAbility r, string param)
        {
            var found = new List<Slot>();
            if (string.IsNullOrEmpty(param)) return found;
            var d = r.def;
            var t = d.targeting;
            switch (param)
            {
                case "cooldown": found.Add(new Slot { get = () => d.cooldown, set = v => d.cooldown = v }); return found;
                case "charges": found.Add(new Slot { get = () => d.charges, set = v => d.charges = (int)v, whole = true, min = 1f }); return found;
                case "castTime": found.Add(new Slot { get = () => d.castTime, set = v => d.castTime = v }); return found;
                case "duration": found.Add(new Slot { get = () => d.duration, set = v => d.duration = v }); return found;
                case "ultimateCost": if (d.ultimate) found.Add(new Slot { get = () => d.ultimateCost, set = v => d.ultimateCost = v, min = 1f }); return found;
                case "range": found.Add(new Slot { get = () => t.range, set = v => t.range = v }); return found;
                case "radius": found.Add(new Slot { get = () => t.radius, set = v => t.radius = v }); return found;
                case "count": found.Add(new Slot { get = () => t.count, set = v => t.count = (int)v, whole = true, min = 1f }); return found;
                case "spreadDegrees": found.Add(new Slot { get = () => t.spreadDegrees, set = v => t.spreadDegrees = v }); return found;
                case "maxTargets": found.Add(new Slot { get = () => t.maxTargets, set = v => t.maxTargets = (int)v, whole = true }); return found;
            }

            var parts = param.Split('.');
            if (parts[0] == "stat" && parts.Length == 2)
            {
                foreach (var mod in d.stats)
                {
                    var m = mod;
                    if (m != null && m.stat == parts[1]) found.Add(new Slot { get = () => m.value, set = v => m.value = v, min = float.MinValue });
                }
            }
            else if (parts[0] == "effect" && parts.Length == 3)
            {
                AddEffectSlots(found, d.effects, parts[1], parts[2]);
            }
            else if (parts[0] == "object" && parts.Length == 4 && parts[1] == "effect")
            {
                foreach (var o in r.objects.Values) AddEffectSlots(found, o.effects, parts[2], parts[3]);
            }
            else if (parts[0] == "object" && parts.Length == 2)
            {
                foreach (var obj in r.objects.Values)
                {
                    var o = obj;
                    switch (parts[1])
                    {
                        case "lifetime": found.Add(new Slot { get = () => o.lifetime, set = v => o.lifetime = v }); break;
                        case "health": found.Add(new Slot { get = () => o.health, set = v => o.health = v }); break;
                        case "radius": found.Add(new Slot { get = () => o.radius, set = v => o.radius = v }); break;
                        case "interval": found.Add(new Slot { get = () => o.interval, set = v => o.interval = v, min = 0.05f }); break;
                        case "armTime": found.Add(new Slot { get = () => o.armTime, set = v => o.armTime = v }); break;
                        case "maxPerOwner": found.Add(new Slot { get = () => o.maxPerOwner, set = v => o.maxPerOwner = (int)v, whole = true }); break;
                        case "maxTargets": found.Add(new Slot { get = () => o.maxTargets, set = v => o.maxTargets = (int)v, whole = true }); break;
                        case "width": found.Add(new Slot { get = () => o.width, set = v => o.width = v }); break;
                        case "height": found.Add(new Slot { get = () => o.height, set = v => o.height = v }); break;
                        case "reflect": found.Add(new Slot { get = () => o.reflect, set = v => o.reflect = v }); break;
                        case "regen": found.Add(new Slot { get = () => o.regen, set = v => o.regen = v }); break;
                        case "regenDelay": found.Add(new Slot { get = () => o.regenDelay, set = v => o.regenDelay = v }); break;
                        case "hitRadius": found.Add(new Slot { get = () => o.hitRadius, set = v => o.hitRadius = v }); break;
                    }
                }
            }
            return found;
        }

        static void AddEffectSlots(List<Slot> found, EffectDef[] effects, string key, string field)
        {
            if (effects == null) return;
            foreach (var effect in effects)
            {
                var e = effect;
                if (e == null) continue;
                if (e.kind != key && !(e.status == key && key.Length > 0) && !(e.tag == key && key.Length > 0)) continue;
                switch (field)
                {
                    case "amount": found.Add(new Slot { get = () => e.amount, set = v => e.amount = v }); break;
                    case "duration": found.Add(new Slot { get = () => e.duration, set = v => e.duration = v }); break;
                    case "delay": found.Add(new Slot { get = () => e.delay, set = v => e.delay = v }); break;
                    case "interval": found.Add(new Slot { get = () => e.interval, set = v => e.interval = v }); break;
                }
            }
        }
    }
}
