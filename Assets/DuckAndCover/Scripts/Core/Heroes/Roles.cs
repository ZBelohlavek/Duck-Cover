using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// The recommended 4v4 lineup: one tank, one healer, one damage dealer and one utility or flex pick.
    /// A recommendation only: nothing stops a team from picking any mix, duplicates included
    /// (<see cref="Ruleset.uniqueHeroesPerTeam"/> stays off). The pick screen uses this to say which
    /// roles a team hasn't covered yet. Role ids are the ones in hero data (<see cref="HeroDef.role"/>).
    /// </summary>
    public static class Roles
    {
        public const string Tank = "vanguard";
        public const string Healer = "support";
        public const string Damage = "striker";
        public const string Utility = "controller";

        /// <summary>The lineup in the order the pick screen lists it. The last slot is flex.</summary>
        public static readonly string[] Lineup = { Tank, Healer, Damage, Utility };

        /// <summary>The name players see for a role id: Tank, Healer, DPS or Utility.</summary>
        public static string Label(string role)
        {
            switch (role)
            {
                case Tank: return "Tank";
                case Healer: return "Healer";
                case Damage: return "DPS";
                case Utility: return "Utility";
                default: return string.IsNullOrEmpty(role) ? "" : role;
            }
        }

        /// <summary>
        /// Roles from the lineup that none of <paramref name="team"/>'s picks cover yet, in lineup order.
        /// Utility is the flex slot: it only counts as open while the team has more unpicked players than
        /// tank, healer and DPS gaps, so a team that flexes its fourth pick isn't nagged about it.
        /// </summary>
        public static List<string> Open(MatchState state, TeamId team, HeroCatalog heroes)
        {
            var open = new List<string>();
            if (state == null || heroes == null) return open;
            var covered = new HashSet<string>();
            int unpicked = 0;
            foreach (var p in state.players)
            {
                if (p.team != team || !p.connected) continue;
                var hero = string.IsNullOrEmpty(p.heroId) ? null : heroes.Get(p.heroId);
                if (hero == null) unpicked++;
                else covered.Add(hero.role);
            }
            foreach (var role in Lineup)
                if (role != Utility && !covered.Contains(role)) open.Add(role);
            // The flex slot: suggest utility only while there are more free players than core roles to fill.
            if (!covered.Contains(Utility) && unpicked > open.Count) open.Add(Utility);
            return open;
        }

        /// <summary>One line for the pick screen, e.g. "Recommended, still open: Tank, Healer". Empty when the lineup is covered.</summary>
        public static string Hint(MatchState state, TeamId team, HeroCatalog heroes)
        {
            var open = Open(state, team, heroes);
            if (open.Count == 0) return "";
            var names = new List<string>();
            foreach (var role in open) names.Add(Label(role));
            return "Recommended, still open: " + string.Join(", ", names);
        }
    }
}
