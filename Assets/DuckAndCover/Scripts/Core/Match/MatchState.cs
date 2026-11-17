using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// Everything the Match, Zone and Spawn rules know about a match. Plain serializable data:
    /// MatchRules changes it, snapshots copy it, and Net, Game and UI read it.
    /// </summary>
    [Serializable]
    public class MatchState
    {
        public string modeId = "";
        public MatchPhase phase = MatchPhase.Lobby;
        /// <summary>Server ticks since the match was created (one per MatchRules.Tick).</summary>
        public int tick;
        /// <summary>Seconds left in a timed phase (HeroPick, Deploy, MatchEnd).</summary>
        public float phaseTimeLeft;
        /// <summary>Seconds of Play and Overtime so far: the match clock that the time cap, rotations and respawns use.</summary>
        public float elapsed;

        public int scoreA;
        public int scoreB;
        /// <summary>Fractions of a point earned but not yet counted, so scoring is exact at any tick rate.</summary>
        public float scoreCarryA;
        public float scoreCarryB;

        public List<ZoneState> zones = new List<ZoneState>();
        /// <summary>King of the hill: index in the zone set of the first live zone.</summary>
        public int rotationIndex;
        /// <summary>Match time of the next rotation, if the mode rotates.</summary>
        public float nextRotationAt;
        /// <summary>The next rotation has been announced.</summary>
        public bool rotationAnnounced;

        public float nextWaveAt;

        public bool mapShifted;

        /// <summary>The team that reached the cap or led at the time cap, while in overtime.</summary>
        public TeamId overtimeLeader = TeamId.None;
        public float overtimeElapsed;
        /// <summary>How long the trailing team has been off every contested point.</summary>
        public float overtimeClearFor;

        public TeamId winner = TeamId.None;
        public MatchEndReason endReason = MatchEndReason.None;

        public List<PlayerState> players = new List<PlayerState>();

        // ---- Queries ----

        public bool IsPlaying => phase == MatchPhase.Play || phase == MatchPhase.Overtime;

        public int Score(TeamId team) => team == TeamId.A ? scoreA : team == TeamId.B ? scoreB : 0;

        public TeamId Leader => scoreA > scoreB ? TeamId.A : scoreB > scoreA ? TeamId.B : TeamId.None;

        public PlayerState Player(int id)
        {
            foreach (var p in players) if (p.id == id) return p;
            return null;
        }

        public ZoneState Zone(ZoneId id)
        {
            foreach (var z in zones) if (z.id == id) return z;
            return null;
        }

        public bool IsAlive(int id) { var p = Player(id); return p != null && p.connected && p.alive; }

        public int ConnectedCount(TeamId team)
        {
            int n = 0;
            foreach (var p in players) if (p.team == team && p.connected) n++;
            return n;
        }

        /// <summary>Live zones the team holds.</summary>
        public int ZonesHeld(TeamId team)
        {
            int n = 0;
            foreach (var z in zones) if (z.active && z.owner == team) n++;
            return n;
        }

        public int ActiveZoneCount
        {
            get { int n = 0; foreach (var z in zones) if (z.active) n++; return n; }
        }

        /// <summary>A deep copy, for snapshots and tests.</summary>
        public MatchState Clone()
        {
            var c = (MatchState)MemberwiseClone();
            c.players = new List<PlayerState>(players.Count);
            foreach (var p in players) c.players.Add(p.Clone());
            c.zones = new List<ZoneState>(zones.Count);
            foreach (var z in zones) c.zones.Add(z.Clone());
            return c;
        }
    }
}
