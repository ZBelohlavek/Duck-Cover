using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    public partial class Ruleset
    {
        /// <summary>
        /// Leveling numbers (WP8). <see cref="Ruleset.Clone"/> copies this by reference, so
        /// <see cref="ProgressionRules"/> takes its own copy when a match starts.
        /// </summary>
        public ProgressionRuleset progression = new ProgressionRuleset();
    }

    /// <summary>
    /// Every number that drives in-match leveling: experience sources, level thresholds, catch-up and
    /// which kind of upgrade each level offers. Plain fields, loadable with JsonUtility.
    /// </summary>
    [Serializable]
    public class ProgressionRuleset
    {
        public int maxLevel = 10;

        /// <summary>
        /// Total experience needed to reach each level: entry 0 is level 1 (always 0), entry 9 is
        /// level 10. Tuned with `report progression` so level 5 lands near minute 5 and level 10 near
        /// minute 12 of a typical match.
        /// </summary>
        public int[] xpToReach = { 0, 440, 920, 1440, 2000, 2560, 3130, 3710, 4300, 4900 };

        /// <summary>
        /// What each level offers, by level (entry 0 is level 1): "" for nothing, otherwise trait,
        /// talent, ultimate or capstone. The plan: traits at 2, 4, 6, 8; talents at 3, 7, 9; the
        /// ultimate's form at 5; a capstone at 10.
        /// </summary>
        public string[] levelChoices = { "", "trait", "talent", "trait", "ultimate", "trait", "talent", "trait", "talent", "capstone" };

        /// <summary>Options offered at each choice level.</summary>
        public int optionsPerLevel = 2;

        // ---- Experience sources ----
        /// <summary>Everyone connected, alive or waiting to respawn, while the match clock runs.</summary>
        public float xpPerSecondInMatch = 2f;
        /// <summary>Living players standing on a live zone (one their team holds or is fighting for).</summary>
        public float xpPerSecondOnZone = 5f;
        /// <summary>Shared by the players on the zone when it is captured.</summary>
        public float xpPerCapture = 100f;
        /// <summary>Shared by the players on the zone when it is neutralised.</summary>
        public float xpPerNeutralise = 100f;
        /// <summary>Shared by the attacker, everyone who damaged the target and everyone who helped the attacker.</summary>
        public float xpPerElimination = 100f;
        /// <summary>Per point of healing or shielding given to a teammate.</summary>
        public float xpPerSupportPoint = 0.25f;
        /// <summary>Damage to the target or help to the attacker counts toward an elimination for this long.</summary>
        public float assistWindowSeconds = 10f;

        // ---- Keeping it from snowballing ----
        /// <summary>Elimination experience changes by this share per level the target is above (or below) the receiver.</summary>
        public float bountyPerLevel = 0.1f;
        public float bountyMin = 0.5f;
        public float bountyMax = 1.5f;
        /// <summary>A player this many levels or more below the match average earns <see cref="catchUpBonus"/> more.</summary>
        public int catchUpLevelGap = 2;
        public float catchUpBonus = 0.25f;

        /// <summary>The kind offered on reaching a level, or None.</summary>
        /// <summary>The level where the ultimate's form is chosen (5 in the plan), or 0 if none is.</summary>
        public int UltimateFormLevel()
        {
            for (int level = 1; levelChoices != null && level <= levelChoices.Length; level++)
                if (ChoiceAt(level) == UpgradeKind.Ultimate) return level;
            return 0;
        }

        public UpgradeKind ChoiceAt(int level)
        {
            if (levelChoices == null || level < 1 || level > levelChoices.Length) return UpgradeKind.None;
            return UpgradeKinds.Parse(levelChoices[level - 1]);
        }

        /// <summary>Levels that offer a choice, in order.</summary>
        public List<int> ChoiceLevels()
        {
            var list = new List<int>();
            for (int level = 1; level <= maxLevel; level++) if (ChoiceAt(level) != UpgradeKind.None) list.Add(level);
            return list;
        }

        public ProgressionRuleset Clone()
        {
            var c = (ProgressionRuleset)MemberwiseClone();
            c.xpToReach = (int[])xpToReach?.Clone();
            c.levelChoices = (string[])levelChoices?.Clone();
            return c;
        }

        public List<string> Validate()
        {
            var errors = new List<string>();
            if (maxLevel < 1) errors.Add("maxLevel must be at least 1");
            if (xpToReach == null || xpToReach.Length != maxLevel) errors.Add("xpToReach needs one entry per level (" + maxLevel + ")");
            else
            {
                if (xpToReach[0] != 0) errors.Add("xpToReach starts at 0 (level 1)");
                for (int i = 1; i < xpToReach.Length; i++)
                    if (xpToReach[i] <= xpToReach[i - 1]) { errors.Add("xpToReach must rise with every level"); break; }
            }
            if (levelChoices == null || levelChoices.Length != maxLevel) errors.Add("levelChoices needs one entry per level (" + maxLevel + ")");
            else
            {
                if (!string.IsNullOrEmpty(levelChoices[0])) errors.Add("level 1 is the base kit and offers no choice");
                for (int i = 0; i < levelChoices.Length; i++)
                    if (!string.IsNullOrEmpty(levelChoices[i]) && UpgradeKinds.Parse(levelChoices[i]) == UpgradeKind.None)
                        errors.Add("levelChoices[" + i + "] '" + levelChoices[i] + "' is not trait, talent, ultimate or capstone");
            }
            if (optionsPerLevel < 1) errors.Add("optionsPerLevel must be at least 1");
            if (xpPerSecondInMatch < 0f) errors.Add("xpPerSecondInMatch can't be negative");
            if (xpPerSecondOnZone < 0f) errors.Add("xpPerSecondOnZone can't be negative");
            if (xpPerCapture < 0f) errors.Add("xpPerCapture can't be negative");
            if (xpPerNeutralise < 0f) errors.Add("xpPerNeutralise can't be negative");
            if (xpPerElimination < 0f) errors.Add("xpPerElimination can't be negative");
            if (xpPerSupportPoint < 0f) errors.Add("xpPerSupportPoint can't be negative");
            if (assistWindowSeconds < 0f) errors.Add("assistWindowSeconds can't be negative");
            if (bountyPerLevel < 0f) errors.Add("bountyPerLevel can't be negative");
            if (bountyMin < 0f || bountyMax < bountyMin) errors.Add("bounty range must be 0 <= bountyMin <= bountyMax");
            if (catchUpLevelGap < 1) errors.Add("catchUpLevelGap must be at least 1");
            if (catchUpBonus < 0f) errors.Add("catchUpBonus can't be negative");
            return errors;
        }
    }
}
