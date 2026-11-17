using System.Collections.Generic;

namespace DuckAndCover.Core
{
    public partial class MatchSnapshot
    {
        /// <summary>Every player's position and stance (WP4). Filled by CombatServer.FillSnapshot.</summary>
        public List<PlayerMotion> motion = new List<PlayerMotion>();
        /// <summary>Every player's weapons, ammo and reload (WP4).</summary>
        public List<WeaponState> weapons = new List<WeaponState>();
        /// <summary>Shots in flight (WP4), so a late joiner sees them too.</summary>
        public List<ProjectileState> projectiles = new List<ProjectileState>();
        /// <summary>Players that are practice dummies (nobody controls them).</summary>
        public int[] dummies = new int[0];

        public bool IsDummy(int playerId) => dummies != null && System.Array.IndexOf(dummies, playerId) >= 0;

        public PlayerMotion Motion(int playerId)
        {
            if (motion != null) foreach (var m in motion) if (m.playerId == playerId) return m;
            return null;
        }

        public WeaponState Weapons(int playerId)
        {
            if (weapons != null) foreach (var w in weapons) if (w.playerId == playerId) return w;
            return null;
        }
    }
}
