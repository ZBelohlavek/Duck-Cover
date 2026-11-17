using System.Linq;
using DuckAndCover.Core;
using static MatchHarness;

/// <summary>Capture, neutralise, contest and decay on control points (WP1).</summary>
static class ZoneTests
{
    [Test]
    static void ZonesLockedUntilPlay()
    {
        var h = new MatchHarness(toPlay: false);
        Check.True(h.S.zones.All(z => !z.active), "zones start locked");
        h.Match.StartMatch();
        h.RunTo(MatchPhase.Deploy);
        h.World.Put(h.A[0], Mid);
        h.Run(5f);
        Check.Near(0f, h.Zone(Mid).progress, "no capture progress while deploying");
        h.RunTo(MatchPhase.Play);
        Check.True(h.S.zones.All(z => z.active), "capture and hold: all three points unlock at Play");
        Check.Equal(3, h.Of<ZoneActivatedEvent>().Count, "one activation event per point");
    }

    [Test]
    static void CaptureTimeScalesWithPlayersUpToThree()
    {
        foreach (var (players, expected) in new[] { (1, 6f), (2, 4f), (3, 3f), (5, 3f) })
        {
            var h = new MatchHarness();
            h.World.Put(h.A.Take(players), Mid);
            float t = h.RunUntil(() => h.Zone(Mid).owner == TeamId.A, 20f);
            Check.Near(expected, t, players + " player(s) capture time", 0.06f);
            var cap = h.Last<ZoneCapturedEvent>();
            Check.Equal(players, cap.players.Length, "capture event lists everyone on the point");
            Check.Equal(TeamId.A, cap.team, "capturing team");
        }
    }

    [Test]
    static void EnemyPointMustBeNeutralisedFirst()
    {
        var h = new MatchHarness();
        h.Take(Mid, TeamId.A);
        h.World.Put(h.B[0], Mid);
        float neutralise = h.RunUntil(() => h.Zone(Mid).owner == TeamId.None, 20f);
        Check.Near(6f, neutralise, "neutralise takes 6 s alone", 0.06f);
        var n = h.Last<ZoneNeutralisedEvent>();
        Check.Equal(TeamId.B, n.team, "neutralised by B");
        Check.Equal(TeamId.A, n.previousOwner, "taken from A");
        Check.Equal(0, h.Of<ZoneCapturedEvent>().Count(e => e.team == TeamId.B), "not captured yet");
        float capture = h.RunUntil(() => h.Zone(Mid).owner == TeamId.B, 20f);
        Check.Near(6f, capture, "then a full capture", 0.06f);
    }

    [Test]
    static void AnyEnemyFreezesProgress()
    {
        var h = new MatchHarness();
        h.World.Put(h.A.Take(3), Mid);
        h.Run(1f);
        float before = h.Zone(Mid).progress;
        h.World.Put(h.B[0], Mid);
        h.Run(10f);
        Check.True(h.Zone(Mid).contested, "contested");
        Check.Near(before, h.Zone(Mid).progress, "progress frozen by a single enemy");
        Check.Equal(TeamId.None, h.Zone(Mid).owner, "still neutral");
        Check.Equal(1, h.Of<ZoneContestChangedEvent>().Count(e => e.contested), "contest started once");

        h.Match.EliminatePlayer(h.B[0], h.A[0]);
        h.RunUntil(() => h.Zone(Mid).owner == TeamId.A, 10f);
        Check.False(h.Zone(Mid).contested, "an eliminated enemy no longer contests");
        Check.Equal(1, h.Of<ZoneContestChangedEvent>().Count(e => !e.contested), "contest ended once");
    }

    [Test]
    static void OwnerWindsBackNeutraliseProgress()
    {
        var h = new MatchHarness();
        h.Take(Mid, TeamId.A);
        h.World.Put(h.B[0], Mid);
        h.Run(3f);
        Check.Near(0.5f, h.Zone(Mid).progress, "half neutralised", 0.01f);
        h.World.Clear(h.B[0]);
        h.World.Put(h.A[0], Mid);
        h.Run(3f);
        Check.Near(0f, h.Zone(Mid).progress, "owner restores it", 0.01f);
        Check.Equal(TeamId.A, h.Zone(Mid).owner, "still A's");
    }

    [Test]
    static void OtherTeamsProgressIsUndoneFirst()
    {
        var h = new MatchHarness();
        h.World.Put(h.A[0], Mid);
        h.Run(3f); // A half way
        h.World.Clear(h.A[0]);
        h.World.Put(h.B[0], Mid);
        h.Run(1.5f);
        Check.Equal(TeamId.A, h.Zone(Mid).progressTeam, "still A's progress while B undoes it");
        Check.Near(0.25f, h.Zone(Mid).progress, "B undoes at capture speed", 0.01f);
        float t = h.RunUntil(() => h.Zone(Mid).owner == TeamId.B, 20f);
        Check.Near(1.5f + 6f, t, "then B captures from zero", 0.1f);
    }

    [Test]
    static void EmptyPointDecays()
    {
        var h = new MatchHarness();
        h.World.Put(h.A[0], Mid);
        h.Run(3f);
        h.World.Clear(h.A[0]);
        h.Run(2f);
        Check.Near(0.5f - 2f * Ruleset.Default.progressDecayPerSecond, h.Zone(Mid).progress, "decays while empty", 0.01f);
        h.Run(10f);
        Check.Near(0f, h.Zone(Mid).progress, "decays to nothing");
        Check.Equal(TeamId.None, h.Zone(Mid).progressTeam, "no progress team once empty");
    }

    [Test]
    static void DeadAndDisconnectedPlayersDontCount()
    {
        var h = new MatchHarness();
        h.World.Put(h.A[0], Mid);
        h.World.Put(h.A[1], Mid);
        h.Match.EliminatePlayer(h.A[0]);
        h.Match.RemovePlayer(h.A[1]);
        h.Run(3f);
        Check.Near(0f, h.Zone(Mid).progress, "nobody living on the point");
        Check.Equal(0, h.Zone(Mid).countA, "count ignores them");
    }

    [Test]
    static void StartOwnerFromMap()
    {
        var map = TestMap();
        map.zoneSets[0].zones[0].startOwner = TeamId.A;
        var h = new GameEventBus();
        var m = new MatchRules(Ruleset.Default, ModeDef.CaptureAndHold(), map, new FakeWorld(), h);
        m.AddPlayer(1, TeamId.A); m.AddPlayer(101, TeamId.B);
        m.StartMatch();
        for (int i = 0; i < 60 * 20; i++) m.Tick(Dt);
        Check.Equal(TeamId.A, m.State.Zone(NearA).owner, "near point starts with its team");
        Check.True(m.State.scoreA >= 15, "and scores for them from the start");
    }
}
