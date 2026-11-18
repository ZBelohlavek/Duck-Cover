namespace DuckAndCover.Core
{
    // Server checks for launch pad flights (WP18). The arcs themselves are map data (LaunchPad).
    public partial class Ruleset
    {
        /// <summary>A move during a launch may stray this far (metres) from the pad's arc.</summary>
        public float launchTolerance = 2.5f;
        /// <summary>A move may be this far ahead of the server's flight clock (seconds): packets bunch up.</summary>
        public float launchLeadSeconds = 0.5f;
        /// <summary>The flight ends this long after the arc does if no move has landed it (seconds).</summary>
        public float launchGraceSeconds = 1f;
    }
}
