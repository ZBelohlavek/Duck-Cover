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
        public static readonly Color Text = new Color(0.92f, 0.96f, 0.98f);
        /// <summary>Secondary text. Bright enough to read over a sunlit map (the old grey faded into the sky).</summary>
        public static readonly Color TextDim = new Color(0.68f, 0.80f, 0.86f);
        public static readonly Color Warning = new Color(0.98f, 0.78f, 0.25f);
        public static readonly Color Danger = new Color(1.00f, 0.32f, 0.28f);

        /// <summary>The helmet display's own light: cool line work, as if projected onto the visor.</summary>
        public static readonly Color Visor = new Color(0.62f, 0.93f, 1.00f);
        /// <summary>Faint visor lines: empty bar cells, rules, the visor's edge.</summary>
        public static readonly Color VisorFaint = new Color(0.62f, 0.93f, 1.00f, 0.32f);
        /// <summary>The tinted glass behind numbers and slots, so they read against a bright sky without a box.</summary>
        public static readonly Color Glass = new Color(0.01f, 0.04f, 0.07f, 0.45f);
        /// <summary>The darkening at the visor's edges.</summary>
        public static readonly Color Vignette = new Color(0.00f, 0.02f, 0.04f, 0.42f);

        public static readonly Color Health = new Color(0.40f, 0.95f, 0.55f);
        public static readonly Color Shield = new Color(0.45f, 0.85f, 1.00f);

        /// <summary>Team colours follow the team, not the side, so a team keeps its colour when sides swap.</summary>
        public static readonly Color TeamA = new Color(0.24f, 0.60f, 0.95f);
        public static readonly Color TeamB = new Color(0.95f, 0.55f, 0.20f);

        public static Color ForTeam(TeamId team) =>
            team == TeamId.A ? TeamA : team == TeamId.B ? TeamB : TextDim;

        /// <summary>
        /// Each hero's identity colour, brightened to read as light on the visor: Lancer cobalt (navy camo),
        /// Bulwark red, Mender jade, Rigger violet, the war chief saffron, Skulk lime (grass camo), Bolete toadstool pink
        /// (his cap is red, but red is Bulwark's), the vampire deep crimson. The visor's edge and the hero's own slots take it.
        /// </summary>
        public static Color ForHero(string heroId)
        {
            switch (heroId)
            {
                case "lancer": return new Color(0.45f, 0.62f, 1.00f);
                case "bulwark": return new Color(1.00f, 0.36f, 0.30f);
                case "mender": return new Color(0.30f, 0.95f, 0.70f);
                case "rigger": return new Color(0.74f, 0.50f, 1.00f);
                case "skulk": return new Color(0.66f, 0.94f, 0.26f);
                case "warchief": return new Color(1.00f, 0.78f, 0.20f);
                case "bolete": return new Color(1.00f, 0.52f, 0.72f);
                case "vampire": return new Color(0.86f, 0.10f, 0.22f);
                default: return Visor;
            }
        }

        public static Color WithAlpha(Color c, float alpha) => new Color(c.r, c.g, c.b, alpha);
    }
}
