using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// Every tunable number in the rules that doesn't depend on the mode. Plain public fields with the
    /// plan's starting values, so the Game layer can load overrides from data
    /// (JsonUtility.FromJsonOverwrite) and Core never sees Unity. What changes between capture and
    /// hold and king of the hill lives in <see cref="ModeDef"/>.
    /// Partial on purpose: other modules add their numbers in their own folder (for example
    /// Core/Progression/Ruleset.Progression.cs) rather than editing this file.
    /// </summary>
    [Serializable]
    public partial class Ruleset
    {
        // ---- Teams ----
        /// <summary>Smallest team that can start a match. 1 so two people can test.</summary>
        public int minPlayersPerTeam = 1;
        public int maxPlayersPerTeam = 5;
        /// <summary>
        /// When true a hero can be picked by only one player on each team. Off by default: teammates
        /// may play the same hero.
        /// </summary>
        public bool uniqueHeroesPerTeam = false;

        // ---- Phases (seconds) ----
        public float heroPickSeconds = 30f;
        /// <summary>Teams are held in their spawn rooms and the points stay locked.</summary>
        public float deploySeconds = 10f;
        /// <summary>Scoreboard and builds after the result.</summary>
        public float matchEndSeconds = 10f;

        // ---- Overtime ----
        public bool overtimeEnabled = true;
        /// <summary>The trailing team must be off every contested point this long before overtime ends.</summary>
        public float overtimeGraceSeconds = 1f;
        /// <summary>Overtime can't last longer than this. 0 = no limit.</summary>
        public float overtimeMaxSeconds = 0f;

        // ---- Zones ----
        /// <summary>Seconds for one player to capture a neutral point.</summary>
        public float captureSeconds = 6f;
        /// <summary>Seconds for one player to neutralise an enemy point.</summary>
        public float neutraliseSeconds = 6f;
        /// <summary>
        /// Capture speed by the number of capturing players: entry 0 is one player, entry 1 two
        /// players, and so on. Players beyond the last entry add nothing ("up to three").
        /// </summary>
        public float[] captureSpeedByPlayers = { 1f, 1.5f, 2f };
        /// <summary>Share of a full capture lost per second while nobody stands on a half-taken point.</summary>
        public float progressDecayPerSecond = 0.1f;

        // ---- Respawn ----
        /// <summary>Respawn wait at level 1.</summary>
        public float respawnBaseSeconds = 8f;
        /// <summary>Added per level above 1: 8 s at level 1, 17 s at level 10.</summary>
        public float respawnPerLevelSeconds = 1f;
        /// <summary>Players return in waves this many seconds apart (0 = as soon as their wait ends).</summary>
        public float respawnWaveSeconds = 2f;

        /// <summary>The plan's starting values.</summary>
        public static Ruleset Default => new Ruleset();

        /// <summary>A copy, so a match can't change the ruleset another match uses.</summary>
        public Ruleset Clone()
        {
            var c = (Ruleset)MemberwiseClone();
            c.captureSpeedByPlayers = (float[])captureSpeedByPlayers?.Clone();
            CloneModules(c);
            return c;
        }

        /// <summary>Problems that would break a match, empty when the ruleset is usable.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            if (minPlayersPerTeam < 1) errors.Add("minPlayersPerTeam must be at least 1");
            if (maxPlayersPerTeam < minPlayersPerTeam) errors.Add("maxPlayersPerTeam must be at least minPlayersPerTeam");
            if (heroPickSeconds < 0f) errors.Add("heroPickSeconds can't be negative");
            if (deploySeconds < 0f) errors.Add("deploySeconds can't be negative");
            if (matchEndSeconds < 0f) errors.Add("matchEndSeconds can't be negative");
            if (overtimeGraceSeconds < 0f) errors.Add("overtimeGraceSeconds can't be negative");
            if (overtimeMaxSeconds < 0f) errors.Add("overtimeMaxSeconds can't be negative");
            if (captureSeconds <= 0f) errors.Add("captureSeconds must be positive");
            if (neutraliseSeconds <= 0f) errors.Add("neutraliseSeconds must be positive");
            if (captureSpeedByPlayers == null || captureSpeedByPlayers.Length == 0) errors.Add("captureSpeedByPlayers needs at least one entry");
            else foreach (var f in captureSpeedByPlayers) if (f <= 0f) { errors.Add("captureSpeedByPlayers entries must be positive"); break; }
            if (progressDecayPerSecond < 0f) errors.Add("progressDecayPerSecond can't be negative");
            if (respawnBaseSeconds < 0f) errors.Add("respawnBaseSeconds can't be negative");
            if (respawnPerLevelSeconds < 0f) errors.Add("respawnPerLevelSeconds can't be negative");
            if (respawnWaveSeconds < 0f) errors.Add("respawnWaveSeconds can't be negative");
            ValidateModules(errors);
            return errors;
        }

        /// <summary>Another module's ruleset file may implement these for its own fields.</summary>
        partial void ValidateModules(List<string> errors);
        partial void CloneModules(Ruleset copy);
    }
}
