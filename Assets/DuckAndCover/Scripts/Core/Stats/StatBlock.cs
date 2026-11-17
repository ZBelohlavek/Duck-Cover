using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>A stat's starting value, set by the hero (WP8) or the ability rules.</summary>
    [Serializable]
    public class StatBase
    {
        public string stat = "";
        public float value;
    }

    /// <summary>A modifier on the stack and who put it there, so it can be taken off again.</summary>
    [Serializable]
    public class AppliedModifier
    {
        /// <summary>Who added it: an upgrade id, "status:slow", "passive"... Removing a source removes all its modifiers.</summary>
        public string source = "";
        public StatModifier mod = new StatModifier();
    }

    /// <summary>
    /// One hero's stats: starting values plus the modifier stack that traits, talents, passives and
    /// status effects write to. Value = (base + sum Add) * (1 + sum Percent) * product Multiply, over
    /// the modifiers whose condition holds right now. Plain lists so snapshots copy it.
    /// </summary>
    [Serializable]
    public class StatBlock
    {
        public List<StatBase> bases = new List<StatBase>();
        public List<AppliedModifier> modifiers = new List<AppliedModifier>();

        public float Base(string stat)
        {
            foreach (var b in bases) if (b.stat == stat) return b.value;
            return StatIds.DefaultBase(stat);
        }

        public void SetBase(string stat, float value)
        {
            foreach (var b in bases) if (b.stat == stat) { b.value = value; return; }
            bases.Add(new StatBase { stat = stat, value = value });
        }

        public void Add(string source, StatModifier mod)
        {
            if (mod == null || string.IsNullOrEmpty(mod.stat)) return;
            modifiers.Add(new AppliedModifier { source = source ?? "", mod = mod.Clone() });
        }

        public void Add(string source, IEnumerable<StatModifier> mods)
        {
            if (mods == null) return;
            foreach (var m in mods) Add(source, m);
        }

        /// <summary>Takes off every modifier from <paramref name="source"/>; returns how many.</summary>
        public int Remove(string source) => modifiers.RemoveAll(m => m.source == source);

        public bool HasSource(string source)
        {
            foreach (var m in modifiers) if (m.source == source) return true;
            return false;
        }

        /// <summary>
        /// The stat's value now. <paramref name="holds"/> answers conditional modifiers ("onHeldPoint"...);
        /// without it only unconditional modifiers count.
        /// </summary>
        public float Get(string stat, Func<string, bool> holds = null)
        {
            float add = 0f, pct = 0f, mul = 1f;
            foreach (var a in modifiers)
            {
                var m = a.mod;
                if (m.stat != stat) continue;
                if (!string.IsNullOrEmpty(m.when) && (holds == null || !holds(m.when))) continue;
                switch (m.op)
                {
                    case ModOp.Add: add += m.value; break;
                    case ModOp.Percent: pct += m.value; break;
                    case ModOp.Multiply: mul *= m.value; break;
                }
            }
            return (Base(stat) + add) * Math.Max(0f, 1f + pct) * mul;
        }

        public StatBlock Clone()
        {
            var c = new StatBlock();
            foreach (var b in bases) c.bases.Add(new StatBase { stat = b.stat, value = b.value });
            foreach (var m in modifiers) c.modifiers.Add(new AppliedModifier { source = m.source, mod = m.mod.Clone() });
            return c;
        }
    }
}
