namespace DuckAndCover.Core
{
    /// <summary>The part of the shared <see cref="Ruleset"/> that the Destruction and Breach modules own.</summary>
    public partial class Ruleset
    {
        public DestructionRuleset destruction = new DestructionRuleset();
    }
}
