namespace DuckAndCover.Core
{
    public partial class MatchSnapshot
    {
        /// <summary>Overrun's Bastions, side gates and late push (WP16), or null in other modes. Filled by <see cref="OverrunServer.FillSnapshot"/>.</summary>
        public OverrunState overrun;
    }
}
