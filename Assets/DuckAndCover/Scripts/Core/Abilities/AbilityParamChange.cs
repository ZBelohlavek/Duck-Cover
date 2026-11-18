using System;

namespace DuckAndCover.Core
{
    /// <summary>
    /// One change a talent makes to one of the hero's abilities, for this match only. Plain data so
    /// upgrade files can list them. The ability rules (AbilityRunner) rebuild the hero's ability from
    /// its definition plus every change it has taken, so the order of picks doesn't matter for
    /// add, percent and multiply (sets apply last, in pick order).
    ///
    /// Params (see AbilityParams for the full list):
    ///   cooldown, charges, castTime, range, radius, count, spreadDegrees, maxTargets, ultimateCost
    ///   targeting.shape, targeting.origin, targeting.affects   (text, op "set")
    ///   effect.&lt;kind&gt;.amount | duration | delay | interval   (every effect of that kind, e.g. effect.heal.amount)
    ///   object.lifetime | health | radius | interval | armTime | maxPerOwner | width | height   (objects this ability spawns)
    ///   enable   (text: turns on the ability's effects tagged with that name, e.g. a healing trail)
    /// </summary>
    [Serializable]
    public class AbilityParamChange
    {
        /// <summary>Id of the ability definition this changes.</summary>
        public string ability = "";
        /// <summary>Which parameter, as listed above.</summary>
        public string param = "";
        /// <summary>One of <see cref="ModOp"/>: "add", "percent", "multiply" or "set".</summary>
        public string op = ModOp.Add;
        /// <summary>The number for add, percent, multiply, or set on a numeric parameter.</summary>
        public float value;
        /// <summary>The text for set on a text parameter (targeting.*) and for enable.</summary>
        public string text = "";

        public AbilityParamChange() { }

        public AbilityParamChange(string ability, string param, string op, float value, string text = "")
        {
            this.ability = ability;
            this.param = param;
            this.op = op;
            this.value = value;
            this.text = text ?? "";
        }

        public AbilityParamChange Clone() => (AbilityParamChange)MemberwiseClone();

        public override string ToString() =>
            ability + "." + param + " " + op + " " + (string.IsNullOrEmpty(text) ? value.ToString() : text);
    }
}
