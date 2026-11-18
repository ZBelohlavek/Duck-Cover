using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>One minion walking a lane, as the wave rules track it. Its body is a unit in <see cref="UnitRoster"/>.</summary>
    [Serializable]
    public class MinionState
    {
        public int unitId;
        public TeamId team;
        public string typeId = "";
        public int lane;
        /// <summary>Metres along the lane from team A's end where it last was.</summary>
        public float progress;
        /// <summary>Its place in the wave: metres to the right of the lane line, in its own walking direction.</summary>
        public float offset;
        /// <summary>Who it is fighting (a player or unit id), or -1.</summary>
        public int targetId = -1;
        /// <summary>Seconds until it may shoot again.</summary>
        public float cooldown;
        /// <summary>Wardens: seconds until the next shield pulse.</summary>
        public float pulseIn;
        /// <summary>Getting round cover: 1 keeps it on the right, -1 on the left, 0 when the way ahead is clear.</summary>
        public int steer;
    }

    /// <summary>A neutral camp's guards, pad and timers.</summary>
    [Serializable]
    public class CampState
    {
        public string id = "";
        public CampKind kind;
        /// <summary>Guarded (guards alive), Open (pad can be taken) or Resting (taken, guards coming back).</summary>
        public CampPhase phase = CampPhase.Guarded;
        public int[] guardIds = new int[0];
        /// <summary>0..1 toward taking the pad, for <see cref="progressTeam"/>.</summary>
        public float progress;
        public TeamId progressTeam = TeamId.None;
        public bool contested;
        /// <summary>Seconds until the guards are back (Resting).</summary>
        public float respawnIn;
        /// <summary>Who took it last, None before anyone has.</summary>
        public TeamId takenBy = TeamId.None;
        /// <summary>Heroes who hurt its guards or stood on its pad since it last came back.</summary>
        public List<int> helpers = new List<int>();

        public CampState Clone()
        {
            var c = (CampState)MemberwiseClone();
            c.guardIds = (int[])guardIds.Clone();
            c.helpers = new List<int>(helpers);
            return c;
        }
    }

    public enum CampPhase
    {
        Guarded = 0,
        Open = 1,
        Resting = 2,
    }

    /// <summary>A timed boost a camp gave a hero.</summary>
    [Serializable]
    public class CampBoost
    {
        public int playerId;
        public CampKind kind;
        public float timeLeft;
    }

    /// <summary>Waves, minions and camps for one match: the part clients see is in <see cref="LaneSnapshot"/>.</summary>
    public sealed class LaneState
    {
        public float nextWaveIn;
        public int wavesA, wavesB;
        public bool juggernautA, juggernautB;
        public readonly List<MinionState> minions = new List<MinionState>();
        public readonly List<CampState> camps = new List<CampState>();
        public readonly List<CampBoost> boosts = new List<CampBoost>();
        /// <summary>Camp guard unit id to its camp, so damage on a guard is credited to the camp.</summary>
        public readonly Dictionary<int, CampState> guardCamp = new Dictionary<int, CampState>();

        public bool JuggernautPending(TeamId team) => team == TeamId.A ? juggernautA : team == TeamId.B && juggernautB;

        public void SetJuggernaut(TeamId team, bool on)
        {
            if (team == TeamId.A) juggernautA = on;
            else if (team == TeamId.B) juggernautB = on;
        }

        public MinionState Minion(int unitId)
        {
            foreach (var m in minions) if (m.unitId == unitId) return m;
            return null;
        }

        public CampState Camp(string id)
        {
            foreach (var c in camps) if (c.id == id) return c;
            return null;
        }

        public int Alive(TeamId team)
        {
            int n = 0;
            foreach (var m in minions) if (m.team == team) n++;
            return n;
        }
    }
}
