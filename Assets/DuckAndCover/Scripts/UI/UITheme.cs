using DuckAndCover.Core;
using UnityEngine;

namespace DuckAndCover.UI
{
    /// <summary>
    /// Shared colours for the code-built UI (HUD, scoreboard, pick and prep screens, Backline view).
    /// Use these instead of literal colours so the look can change in one place.
    /// </summary>
    public static class UITheme
    {
        public static readonly Color Background = new Color(0.07f, 0.08f, 0.10f, 0.92f);
        public static readonly Color Panel = new Color(0.12f, 0.13f, 0.16f, 0.95f);
        public static readonly Color Text = new Color(0.92f, 0.93f, 0.95f);
        public static readonly Color TextDim = new Color(0.62f, 0.65f, 0.70f);
        public static readonly Color Warning = new Color(0.96f, 0.72f, 0.20f);
        public static readonly Color Danger = new Color(0.90f, 0.28f, 0.24f);

        /// <summary>Team colours follow the team, not the side, so a team keeps its colour when sides swap.</summary>
        public static readonly Color TeamA = new Color(0.24f, 0.60f, 0.95f);
        public static readonly Color TeamB = new Color(0.95f, 0.55f, 0.20f);

        public static Color ForTeam(TeamId team) =>
            team == TeamId.A ? TeamA : team == TeamId.B ? TeamB : TextDim;
    }
}
