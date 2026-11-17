using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// A game mode as data: which of the map's zones it plays on, how many are live at once and on
    /// what schedule, and how scoring works. Capture and hold and king of the hill are both just
    /// ModeDefs (Data/Modes/*.json), so a new mode is a new data file. Loaded with JsonUtility in
    /// Unity, so only plain fields: no enums, dictionaries or properties.
    /// </summary>
    [Serializable]
    public class ModeDef
    {
        public string id = "";
        public string displayName = "";
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
            scorePerSecond = 1f,
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
            if (map != null)
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
    }
}
