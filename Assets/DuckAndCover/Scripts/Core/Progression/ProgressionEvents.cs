namespace DuckAndCover.Core
{
    // Rule events published by the Progression module. The HUD shows the level-up prompt from
    // LevelUpEvent, and the hero rules apply an upgrade's stats and ability changes on UpgradeChosenEvent.

    /// <summary>A player reached a new level. If it offers a choice, the options wait until picked.</summary>
    public sealed class LevelUpEvent : GameEvent
    {
        public int playerId;
        public TeamId team;
        public int level;
        /// <summary>What the level offers, or None (no choice at this level).</summary>
        public UpgradeKind kind;
        /// <summary>Upgrade ids on offer, left first. Empty when the level offers nothing or the hero has no data.</summary>
        public string[] options;
    }

    /// <summary>A player took an upgrade. Final for the match.</summary>
    public sealed class UpgradeChosenEvent : GameEvent
    {
        public int playerId;
        public TeamId team;
        public string heroId;
        public int level;
        public UpgradeKind kind;
        /// <summary>0 = left, 1 = right.</summary>
        public int option;
        public string upgradeId;
    }

    /// <summary>
    /// Experience from a one-off source (capture, neutralise, elimination), for the HUD's "+50" pop-ups.
    /// Experience earned every second (being on a point, being in the match) isn't announced; read it
    /// from the snapshot.
    /// </summary>
    public sealed class ExperienceAwardedEvent : GameEvent
    {
        public int playerId;
        public float amount;
        public ExperienceSource source;
    }

    public enum ExperienceSource
    {
        Match = 0,
        Zone = 1,
        Capture = 2,
        Neutralise = 3,
        Elimination = 4,
        Support = 5,
        /// <summary>Flag Rush: picking up, carrying, scoring, stopping a carrier, being near a capture.</summary>
        Flag = 6,
        /// <summary>A neutral camp taken in Overrun (WP15).</summary>
        Camp = 7,
        /// <summary>Overrun (WP16): near your own wave, minion kills, Bastion damage, Bastions destroyed.</summary>
        Overrun = 8,
    }
}
