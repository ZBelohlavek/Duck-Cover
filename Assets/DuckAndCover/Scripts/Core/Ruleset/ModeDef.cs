using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// A game mode as data: which of the map's zones it plays on, how many are live at once and on
    /// what schedule, and how scoring works. Capture and hold and king of the hill are both just
    /// ModeDefs (Data/Modes/*.json), so a new mode is a new data file. Loaded with JsonUtility in
    /// Unity, so only plain fields: no enums, dictionaries or properties.
    /// Flag Rush (WP21) is a ModeDef too, with <see cref="objective"/> "flag": its zone set is the two
    /// base capture zones, which never go live as points, and score is counted in captures.
    /// </summary>
    [Serializable]
    public class ModeDef
    {
        /// <summary>Score comes from holding live zones (capture and hold, king of the hill).</summary>
        public const string ZonesObjective = "zones";
        /// <summary>Score comes from carrying the flag into the enemy base (Flag Rush, Core/Flag).</summary>
        public const string FlagObjective = "flag";

        public string id = "";
        public string displayName = "";
        /// <summary>
        /// How the mode scores: <see cref="ZonesObjective"/> (empty means the same) or <see cref="FlagObjective"/>.
        /// A flag mode's zone set holds one capture zone in each base: the zone whose startOwner is a
        /// team is that team's base, where the other team scores.
        /// </summary>
        public string objective = ZonesObjective;
        /// <summary>Name of the map zone set the mode plays on (MatchMap.zoneSets), e.g. "points" or "hills".</summary>
        public string zoneSet = "";
        /// <summary>Zones live at once. 0 = every zone in the set, all the time.</summary>
        public int activeZoneCount;
        /// <summary>Seconds before the live zones move on to the next ones in the set. 0 = they never move.</summary>
        public float rotationSeconds;
        /// <summary>How long before a move the next zone is announced.</summary>
        public float rotationWarningSeconds;
        public int scoreToWin = 1000;
        /// <summary>Points per second for each live zone a team holds.</summary>
        public float scorePerSecond = 1f;
        /// <summary>Score rate multiplier while one team holds every live zone (only when more than one is live).</summary>
        public float allHeldMultiplier = 1f;
        /// <summary>Match length cap in seconds; at the cap the higher score wins.</summary>
        public float timeCapSeconds = 900f;
        /// <summary>The map shifts once when the first team reaches this score. 0 = never.</summary>
        public int mapShiftScore;

        /// <summary>Capture and hold on three points, first to 1000 (the plan's values).</summary>
        public static ModeDef CaptureAndHold() => new ModeDef
        {
            id = "capture_hold",
            displayName = "Capture and Hold",
            zoneSet = "points",
            activeZoneCount = 0,
            scoreToWin = 1000,
            // 0.8 rather than 1: bot matches then run about 14 minutes and four players in five
            // reach level 10 (dotnet run --project Tests/CoreSim -- report scorerate).
            scorePerSecond = 0.8f,
            allHeldMultiplier = 2f,
            timeCapSeconds = 900f,
            mapShiftScore = 500,
        };

        /// <summary>King of the hill: one hill moving every 2 minutes, first to 500.</summary>
        public static ModeDef KingOfTheHill() => new ModeDef
        {
            id = "king_of_the_hill",
            displayName = "King of the Hill",
            zoneSet = "hills",
            activeZoneCount = 1,
            rotationSeconds = 120f,
            rotationWarningSeconds = 20f,
            scoreToWin = 500,
            scorePerSecond = 2f,
            allHeldMultiplier = 1f,
            timeCapSeconds = 900f,
            mapShiftScore = 250,
        };

        /// <summary>
        /// Flag Rush: one flag at a random fair spot, carried into the enemy base; first to 5 captures
        /// or the most at 15 minutes, the map shifts at 3 captures by either team. Flag timings and
        /// experience live in the ruleset (Core/Flag/Ruleset.Flag.cs).
        /// </summary>
        public static ModeDef FlagRush() => new ModeDef
        {
            id = "flag_rush",
            displayName = "Flag Rush",
            objective = FlagObjective,
            zoneSet = "bases",
            activeZoneCount = 0,
            scoreToWin = 5,
            scorePerSecond = 1f, // unused: bases never go live as points
            allHeldMultiplier = 1f,
            timeCapSeconds = 900f,
            mapShiftScore = 3,
        };

        public bool IsFlag() => objective == FlagObjective;

        public ModeDef Clone() => (ModeDef)MemberwiseClone();

        /// <summary>Problems in the mode itself and against the map it would run on.</summary>
        public List<string> Validate(MatchMap map)
        {
            var errors = new List<string>();
            if (string.IsNullOrEmpty(id)) errors.Add("mode needs an id");
            if (scoreToWin < 1) errors.Add("scoreToWin must be at least 1");
            if (scorePerSecond <= 0f) errors.Add("scorePerSecond must be positive");
            if (allHeldMultiplier <= 0f) errors.Add("allHeldMultiplier must be positive");
            if (timeCapSeconds <= 0f) errors.Add("timeCapSeconds must be positive");
            if (mapShiftScore < 0) errors.Add("mapShiftScore can't be negative");
            if (activeZoneCount < 0) errors.Add("activeZoneCount can't be negative");
            if (rotationSeconds < 0f) errors.Add("rotationSeconds can't be negative");
            if (rotationWarningSeconds < 0f || (rotationSeconds > 0f && rotationWarningSeconds >= rotationSeconds))
                errors.Add("rotationWarningSeconds must be between 0 and rotationSeconds");
            if (!string.IsNullOrEmpty(objective) && objective != ZonesObjective && objective != FlagObjective)
                errors.Add("objective must be '" + ZonesObjective + "' or '" + FlagObjective + "'");
            if (IsFlag() && (rotationSeconds > 0f || activeZoneCount != 0)) errors.Add("a flag mode's base zones don't rotate");
            if (map != null && IsFlag())
            {
                var bases = map.ZoneSet(zoneSet);
                if (bases == null) errors.Add("map " + map.mapId + " has no zone set '" + zoneSet + "'");
                else if (bases.zones.Count != 2 || BaseZone(map, TeamId.A) == null || BaseZone(map, TeamId.B) == null)
                    errors.Add("a flag mode needs '" + zoneSet + "' to hold one base zone per team (startOwner A and B)");
                if (map.flagSpots == null || map.flagSpots.Count < 2) errors.Add("a flag mode needs at least two flag spots on map " + map.mapId);
            }
            else if (map != null)
            {
                var set = map.ZoneSet(zoneSet);
                if (set == null) errors.Add("map " + map.mapId + " has no zone set '" + zoneSet + "'");
                else
                {
                    if (activeZoneCount > set.zones.Count) errors.Add("activeZoneCount " + activeZoneCount + " is more than the " + set.zones.Count + " zones in '" + zoneSet + "'");
                    if (rotationSeconds > 0f && (activeZoneCount == 0 || activeZoneCount >= set.zones.Count))
                        errors.Add("a rotating mode needs fewer live zones than the set has");
                }
            }
            return errors;
        }

        /// <summary>A flag mode's capture zone inside <paramref name="team"/>'s base (where the other team scores), or null.</summary>
        public ZoneDef BaseZone(MatchMap map, TeamId team)
        {
            var set = map == null ? null : map.ZoneSet(zoneSet);
            if (set == null || team == TeamId.None) return null;
            ZoneDef found = null;
            foreach (var z in set.zones)
                if (z != null && z.startOwner == team)
                {
                    if (found != null) return null; // two bases for one team
                    found = z;
                }
            return found;
        }
    }
}
