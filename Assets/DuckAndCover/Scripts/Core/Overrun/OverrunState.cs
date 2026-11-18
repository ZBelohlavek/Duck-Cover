using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>One Bastion as the Overrun rules track it. Its body, health and shield layer are a unit (<see cref="unitId"/>).</summary>
    [Serializable]
    public class BastionState
    {
        public int unitId = -1;
        public TeamId team;
        public BastionTier tier;
        public Vec3 position;
        public bool alive = true;
        public float health;
        public float maxHealth;
        public float shield;
        public float maxShield;
        /// <summary>Takes no damage at all (a Core Bastion while its Forward Bastion stands).</summary>
        public bool sealedOff;
        /// <summary>Enemy heroes can hurt it now: one of their minions is near it (the HUD's minions-nearby marker).</summary>
        public bool openToHeroes;
        /// <summary>Seconds with no enemy minion near it, toward the shield refill.</summary>
        public float quietFor;
        /// <summary>Who it is shooting (a player or unit id), or -1.</summary>
        public int targetId = -1;
        /// <summary>Seconds until it may shoot again.</summary>
        public float cooldown;
        /// <summary>A hero who hurt one of its team's heroes within its range, shot first until <see cref="aggroUntil"/>; -1 if none.</summary>
        public int aggroId = -1;
        public float aggroUntil;

        public BastionState Clone() => (BastionState)MemberwiseClone();
    }

    /// <summary>
    /// Overrun's state (WP16): the four Bastions, the side gates and the late push. Plain data:
    /// <see cref="OverrunServer"/> changes it, snapshots copy it (<see cref="MatchSnapshot.overrun"/>),
    /// Game and UI draw it.
    /// </summary>
    [Serializable]
    public class OverrunState
    {
        public List<BastionState> bastions = new List<BastionState>();
        /// <summary>The side gate into team A's (B's) core plaza is open: A's (B's) Forward Bastion fell.</summary>
        public bool gateOpenA, gateOpenB;
        public bool latePush;
        /// <summary>Damage (health and shield) dealt to team A's (B's) Bastions, for the time cap's second tie-break.</summary>
        public float damageToA, damageToB;
        /// <summary>Team A's (B's) minions killed by the other team, for the last tie-break.</summary>
        public int minionsLostA, minionsLostB;

        public BastionState Bastion(TeamId team, BastionTier tier)
        {
            foreach (var b in bastions) if (b.team == team && b.tier == tier) return b;
            return null;
        }

        public BastionState ByUnit(int unitId)
        {
            foreach (var b in bastions) if (b.unitId == unitId) return b;
            return null;
        }

        public bool GateOpen(TeamId team) => team == TeamId.A ? gateOpenA : team == TeamId.B && gateOpenB;

        /// <summary>Bastions of <paramref name="team"/> destroyed so far.</summary>
        public int Lost(TeamId team)
        {
            int n = 0;
            foreach (var b in bastions) if (b.team == team && !b.alive) n++;
            return n;
        }

        /// <summary>Share (0..1) of <paramref name="team"/>'s Bastion health taken by the other team, both Bastions together.</summary>
        public float HealthTaken(TeamId team)
        {
            float lost = 0f, max = 0f;
            foreach (var b in bastions)
            {
                if (b.team != team) continue;
                max += b.maxHealth;
                lost += b.alive ? Math.Max(0f, b.maxHealth - b.health) : b.maxHealth;
            }
            return max <= 0f ? 0f : lost / max;
        }

        public OverrunState Clone()
        {
            var c = (OverrunState)MemberwiseClone();
            c.bastions = new List<BastionState>(bastions.Count);
            foreach (var b in bastions) c.bastions.Add(b.Clone());
            return c;
        }
    }
}
