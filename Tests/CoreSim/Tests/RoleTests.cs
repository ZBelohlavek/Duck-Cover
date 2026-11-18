using System.Linq;
using DuckAndCover.Core;

/// <summary>The 4v4 lineup advice (Core/Heroes/Roles): tank, healer, DPS and utility or flex, never a lock.</summary>
static class RoleTests
{
    [Test]
    static void TeamsAreFourAside()
    {
        Check.Equal(4, Ruleset.Default.maxPlayersPerTeam, "4v4");
        Check.False(Ruleset.Default.uniqueHeroesPerTeam, "duplicate heroes stay allowed");
    }

    [Test]
    static void StarterHeroesCoverTheLineup()
    {
        var heroes = ProgressionHarness.DataCatalog();
        Check.Equal(Roles.Tank, heroes.Get("bulwark").role, "Bulwark is the tank");
        Check.Equal(Roles.Healer, heroes.Get("mender").role, "Mender is the healer");
        Check.Equal(Roles.Damage, heroes.Get("lancer").role, "Lancer is the DPS");
        Check.Equal(Roles.Utility, heroes.Get("rigger").role, "Rigger is the utility pick");
        Check.Equal("Tank,Healer,DPS,Utility", string.Join(",", Roles.Lineup.Select(Roles.Label)), "labels in lineup order");
    }

    [Test]
    static void HintNamesTheOpenRolesOnly()
    {
        var h = new MatchHarness(toPlay: false);
        var heroes = ProgressionHarness.DataCatalog();
        h.Match.StartMatch();
        Check.Equal("Recommended, still open: Tank, Healer, DPS, Utility", Roles.Hint(h.S, TeamId.A, heroes), "nobody has picked");

        Pick(h, h.A[0], "mender");
        Pick(h, h.A[1], "lancer");
        Check.Equal("Tank,Utility", string.Join(",", Roles.Open(h.S, TeamId.A, heroes).Select(Roles.Label)), "healer and DPS covered");
        Check.Equal(4, Roles.Open(h.S, TeamId.B, heroes).Count, "the other team's picks don't count");

        // Two DPS and no tank is allowed: the hint still points out the gap, nothing is refused.
        Pick(h, h.A[2], "lancer");
        Check.Equal("Tank", string.Join(",", Roles.Open(h.S, TeamId.A, heroes).Select(Roles.Label)),
            "the fourth slot is flex: with one player left only the tank is open");
        Pick(h, h.A[3], "lancer");
        Check.Equal("Recommended, still open: Tank", Roles.Hint(h.S, TeamId.A, heroes), "a full team without a tank still hears about it");

        Pick(h, h.A[3], "bulwark");
        Check.Equal("", Roles.Hint(h.S, TeamId.A, heroes), "tank, healer and DPS with a flex pick: nothing to say");
    }

    static void Pick(MatchHarness h, int id, string hero) =>
        Check.True(h.Match.Apply(new PickHeroCommand { playerId = id, heroId = hero }).IsAccepted, id + " picks " + hero);
}
