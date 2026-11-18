using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>A camp as clients draw it (HUD timers, pad progress).</summary>
    [Serializable]
    public class CampSnapshot
    {
        public string id = "";
        public CampKind kind;
        public CampPhase phase;
        public float progress;
        public TeamId progressTeam = TeamId.None;
        public bool contested;
        public float respawnIn;
        public TeamId takenBy = TeamId.None;
    }

    /// <summary>Waves and camps as clients see them (WP15). Minion bodies are in <see cref="MatchSnapshot.units"/>.</summary>
    [Serializable]
    public class LaneSnapshot
    {
        public float nextWaveIn;
        public int wavesA, wavesB;
        public bool juggernautA, juggernautB;
        public List<CampSnapshot> camps = new List<CampSnapshot>();
        public List<CampBoost> boosts = new List<CampBoost>();
    }

    public partial class MatchSnapshot
    {
        /// <summary>Waves and camps, on maps that have lanes. Filled by LaneServer.FillSnapshot.</summary>
        public LaneSnapshot lanes = new LaneSnapshot();
    }
}
