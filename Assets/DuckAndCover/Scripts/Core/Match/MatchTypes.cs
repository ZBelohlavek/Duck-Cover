using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    public enum MatchPhase
    {
        /// <summary>Before StartMatch: players join and pick teams.</summary>
        Lobby = 0,
        /// <summary>Each player picks a hero, one of each per team.</summary>
        HeroPick = 1,
        /// <summary>Teams wait in their spawn rooms; the points are locked.</summary>
        Deploy = 2,
        /// <summary>Points are live, holding them scores, players respawn.</summary>
        Play = 3,
        /// <summary>A cap was reached while the trailing team was fighting on a point.</summary>
        Overtime = 4,
        /// <summary>Scoreboard. Nothing scores any more.</summary>
        MatchEnd = 5,
    }

    public enum MatchEndReason
    {
        None = 0,
        /// <summary>A team reached the mode's scoreToWin.</summary>
        ScoreReached = 1,
        /// <summary>The time cap ran out; the higher score won.</summary>
        TimeCap = 2,
        /// <summary>Overtime ended with the trailing team cleared off the points.</summary>
        OvertimeHeld = 3,
        /// <summary>The trailing team reached scoreToWin during overtime.</summary>
        OvertimeComeback = 4,
        /// <summary>Overtime ran out (overtimeMaxSeconds); the higher score won.</summary>
        OvertimeTimeout = 5,
    }

    /// <summary>One player. Level is owned by Progression (WP8); the rules here only read it.</summary>
    [Serializable]
    public class PlayerState
    {
        public int id;
        public TeamId team = TeamId.None;
        /// <summary>False once the player left. They stay listed so ids and scoreboards keep their meaning.</summary>
        public bool connected = true;
        /// <summary>In the world. False while waiting to respawn, before Deploy, and after leaving.</summary>
        public bool alive;
        public string heroId = "";
        /// <summary>1..10. Set by Progression; respawn waits grow with it.</summary>
        public int level = 1;
        /// <summary>Match time (MatchState.elapsed) from which the player may return, while not alive.</summary>
        public float respawnAt;
        /// <summary>Where the player wants to come back: <see cref="ZoneId.None"/> = base.</summary>
        public ZoneId spawnChoice;
        public int eliminations;
        public int deaths;

        public PlayerState Clone() => (PlayerState)MemberwiseClone();
    }

    /// <summary>One zone in a map's zone set.</summary>
    [Serializable]
    public class ZoneDef
    {
        public ZoneId id;
        /// <summary>Who holds the zone when it first goes live. None = neutral.</summary>
        public TeamId startOwner = TeamId.None;
        /// <summary>The team that may respawn here while holding it (the point next to its base), or None.</summary>
        public TeamId forwardSpawnFor = TeamId.None;

        public ZoneDef() { }
        public ZoneDef(int id, TeamId startOwner = TeamId.None, TeamId forwardSpawnFor = TeamId.None)
        {
            this.id = new ZoneId(id);
            this.startOwner = startOwner;
            this.forwardSpawnFor = forwardSpawnFor;
        }
    }

    /// <summary>A named, ordered group of zones a mode plays on. King of the hill rotates in this order.</summary>
    [Serializable]
    public class ZoneSetDef
    {
        public string name = "";
        public List<ZoneDef> zones = new List<ZoneDef>();
    }

    /// <summary>
    /// The objective layout of a map, from the map definition (WP9 authors it; tests build it in code).
    /// A map usually has a "points" set (near A, middle, near B) and a "hills" set.
    /// </summary>
    [Serializable]
    public class MatchMap
    {
        public string mapId = "";
        public List<ZoneSetDef> zoneSets = new List<ZoneSetDef>();

        public ZoneSetDef ZoneSet(string name)
        {
            if (zoneSets == null) return null;
            foreach (var s in zoneSets) if (s != null && s.name == name) return s;
            return null;
        }

        public List<string> Validate()
        {
            var errors = new List<string>();
            if (zoneSets == null || zoneSets.Count == 0) { errors.Add("a map needs at least one zone set"); return errors; }
            var names = new HashSet<string>();
            foreach (var set in zoneSets)
            {
                if (set == null) { errors.Add("a zone set is missing"); continue; }
                if (!names.Add(set.name ?? "")) errors.Add("zone set '" + set.name + "' is defined twice");
                if (set.zones == null || set.zones.Count == 0) { errors.Add("zone set '" + set.name + "' has no zones"); continue; }
                var ids = new HashSet<int>();
                var forward = new HashSet<TeamId>();
                foreach (var z in set.zones)
                {
                    if (z == null || !z.id.IsValid) { errors.Add("zone set '" + set.name + "' has a zone without an id"); continue; }
                    if (!ids.Add(z.id.value)) errors.Add("zone " + z.id + " is in '" + set.name + "' twice");
                    if (z.forwardSpawnFor != TeamId.None && !forward.Add(z.forwardSpawnFor))
                        errors.Add("team " + z.forwardSpawnFor + " has two forward spawn zones in '" + set.name + "'");
                }
            }
            return errors;
        }
    }

    /// <summary>What happened to a command. Rejections carry a reason for logs and the HUD.</summary>
    public enum CommandStatus
    {
        Accepted = 0,
        Rejected = 1,
        /// <summary>The Match module doesn't handle this command type; another module should.</summary>
        NotHandled = 2,
    }

    public readonly struct CommandResult
    {
        public readonly CommandStatus status;
        public readonly string reason;

        CommandResult(CommandStatus status, string reason) { this.status = status; this.reason = reason; }

        public static readonly CommandResult Accepted = new CommandResult(CommandStatus.Accepted, "");
        public static readonly CommandResult NotHandled = new CommandResult(CommandStatus.NotHandled, "");
        public static CommandResult Reject(string reason) => new CommandResult(CommandStatus.Rejected, reason);

        public bool IsAccepted => status == CommandStatus.Accepted;
        public override string ToString() => status == CommandStatus.Rejected ? "Rejected: " + reason : status.ToString();
    }
}
