namespace DuckAndCover.Core
{
    public partial class AbilityContext
    {
        /// <summary>The live match state (phase, zones, players). Read it; change it only through MatchRules.</summary>
        public MatchState match;
    }
}
