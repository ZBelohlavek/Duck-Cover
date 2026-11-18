namespace DuckAndCover.Core
{
    public partial class MatchSnapshot
    {
        /// <summary>The Flag Rush flag (WP21), or null in other modes. Filled by <see cref="FlagServer.FillSnapshot"/>.</summary>
        public FlagState flag;
    }
}
