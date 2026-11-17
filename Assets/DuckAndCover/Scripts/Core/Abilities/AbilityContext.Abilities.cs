namespace DuckAndCover.Core
{
    public partial class AbilityContext
    {
        /// <summary>The ability rules: damage, heal, statuses, objects. Scripts act through this.</summary>
        public AbilityRunner runner;
        /// <summary>The ability being used, with the caster's talents applied.</summary>
        public AbilityDef ability;
        /// <summary>Where the ability landed and who it caught (set from Activate on; null in CanActivate).</summary>
        public EffectArea[] areas;
    }
}
