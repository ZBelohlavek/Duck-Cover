using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// A unit that is not a player, as the server has it: who it is and where. Its health, shields,
    /// stats and statuses live with the heroes' in <see cref="AbilityState.units"/> under the same
    /// id, so the combat rules treat both alike.
    /// </summary>
    [Serializable]
    public class UnitState
    {
        public int id;
        public string typeId = "";
        /// <summary>A team, or None for neutral units (camp guards): everyone's enemy.</summary>
        public TeamId team = TeamId.None;
        /// <summary>Feet position.</summary>
        public Vec3 position;
        /// <summary>Facing, degrees around the up axis (0 = +z), as player yaw.</summary>
        public float yaw;
        public bool alive = true;
        /// <summary>From the kind, kept here so the rules don't need the library.</summary>
        public float maxHealth;
        public float size = 1f;
        public float moveSpeed;
        /// <summary>Seconds since it was destroyed: kept for the lag compensation window, then removed.</summary>
        public float deadFor;
        /// <summary>A push in progress: metres a second, and seconds left.</summary>
        public Vec3 pushVelocity;
        public float pushLeft;

        public BodyPose Pose() => new BodyPose(position, false, alive, size);

        public UnitState Clone() => (UnitState)MemberwiseClone();
    }

    /// <summary>
    /// Every unit that is not a player, with the queries the combat rules need (alive, team). Made by
    /// <see cref="CombatServer"/> and shared by the damage rules, the ability runner and
    /// <see cref="UnitRules"/>.
    /// </summary>
    public sealed class UnitRoster
    {
        public readonly List<UnitState> units = new List<UnitState>();
        public int nextId = UnitIds.First;

        public UnitState Get(int id)
        {
            if (!UnitIds.IsUnit(id)) return null;
            foreach (var u in units) if (u.id == id) return u;
            return null;
        }

        public bool IsAlive(int id)
        {
            var u = Get(id);
            return u != null && u.alive;
        }

        /// <summary>Living units, in id order.</summary>
        public List<UnitState> Living()
        {
            var list = units.FindAll(u => u.alive);
            list.Sort((a, b) => a.id.CompareTo(b.id));
            return list;
        }
    }
}
