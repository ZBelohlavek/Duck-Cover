using System.Collections.Generic;

namespace DuckAndCover.Core
{
    public partial class MapDef
    {
        /// <summary>Minion lanes (WP15), each from team A's base end to team B's. Empty on maps without minions.</summary>
        public List<LaneDef> lanes = new List<LaneDef>();
        /// <summary>Neutral camps (WP15). Empty on maps without them.</summary>
        public List<CampDef> camps = new List<CampDef>();
    }
}
