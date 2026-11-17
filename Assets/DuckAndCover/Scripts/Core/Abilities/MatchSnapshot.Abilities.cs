namespace DuckAndCover.Core
{
    public partial class MatchSnapshot
    {
        /// <summary>A copy of every hero's health, shields, statuses and cooldowns, and the spawned objects (WP7). Filled by AbilityRunner.FillSnapshot.</summary>
        public AbilityState abilities;
    }
}
