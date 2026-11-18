using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>One living unit as clients see it: compact, since a lane match has up to 40 of them.</summary>
    [Serializable]
    public class UnitSnapshot
    {
        public int id;
        public string typeId = "";
        public TeamId team = TeamId.None;
        public Vec3 position;
        public float yaw;
        public float health;
        public float maxHealth;
        public float shield;
        /// <summary>Ids of the statuses on it now (slowed, burning, marked).</summary>
        public string[] statuses = new string[0];
    }

    public partial class MatchSnapshot
    {
        /// <summary>Living units that are not players (WP14). Filled by CombatServer.FillSnapshot.</summary>
        public List<UnitSnapshot> units = new List<UnitSnapshot>();

        public UnitSnapshot Unit(int unitId)
        {
            if (units != null) foreach (var u in units) if (u.id == unitId) return u;
            return null;
        }
    }
}
