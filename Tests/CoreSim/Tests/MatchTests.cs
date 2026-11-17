using System;
using System.Linq;
using DuckAndCover.Core;
using static MatchHarness;

/// <summary>Phases, hero pick, scoring, caps, overtime and full matches in both modes (WP1).</summary>
static class MatchTests
{
    [Test]
    static void RulesetDefaultsMatchThePlan()
    {
        var r = Ruleset.Default;
        Check.Equal(0, r.Validate().Count, "default ruleset is valid");
        Check.Near(30f, r.heroPickSeconds, "hero pick");
        Check.Near(10f, r.deploySeconds, "deploy");
        Check.Near(10f, r.matchEndSeconds, "match end");
        Check.Near(6f, r.captureSeconds, "capture");
        Check.Near(8f, r.respawnBaseSeconds, "respawn at level 1");
        Check.Near(17f, r.respawnBaseSeconds + 9 * r.respawnPerLevelSeconds, "respawn at level 10");
        Check.Equal(3, r.captureSpeedByPlayers.Length, "capture speed counts up to three players");

        r.captureSeconds = 0f;
        r.captureSpeedByPlayers = new float[0];
        Check.Equal(2, r.Validate().Count, "bad values are reported");
        var copy = Ruleset.Default.Clone();
        copy.captureSpeedByPlayers[0] = 9f;
        Check.Near(1f, Ruleset.Default.captureSpeedByPlayers[0], "clone copies arrays");
    }

    [Test]
    static void ModeValidation()
    {
        var map = TestMap();
        Check.Equal(0, ModeDef.CaptureAndHold().Validate(map).Count, "capture and hold fits the test map");
        Check.Equal(0, ModeDef.KingOfTheHill().Validate(map).Count, "king of the hill fits the test map");
        var bad = ModeDef.KingOfTheHill();
        bad.zoneSet = "nowhere";
        Check.True(bad.Validate(map).Count > 0, "missing zone set");
        bad = ModeDef.KingOfTheHill();
        bad.activeZoneCount = 0;
        Check.True(bad.Validate(map).Any(e => e.Contains("rotating")), "rotation needs fewer live zones than the set");
        bad = ModeDef.KingOfTheHill();
        bad.rotationWarningSeconds = 200f;
        Check.True(bad.Validate(map).Count > 0, "warning longer than the rotation");
        bool threw = false;
        try { new MatchRules(Ruleset.Default, bad, map, new FakeWorld(), new GameEventBus()); }
        catch (ArgumentException) { threw = true; }
        Check.True(threw, "a match refuses an invalid mode");
    }

    [Test]
    static void PhasesRunInOrder()
    {
        var h = new MatchHarness(toPlay: false);
        Check.Equal(MatchPhase.Lobby, h.S.phase, "lobby first");
        h.Match.StartMatch();
        Check.Equal(MatchPhase.HeroPick, h.S.phase, "hero pick");
        Check.True(h.S.players.All(p => !p.alive), "nobody in the world during hero pick");
        float pick = h.RunUntil(() => h.S.phase == MatchPhase.Deploy);
        Check.Near(30f, pick, "hero pick lasts 30 s", 0.06f);
        Check.True(h.S.players.All(p => p.alive), "everyone spawns for deploy");
        Check.Equal(10, h.Of<PlayerSpawnedEvent>().Count(e => e.initial), "ten initial spawns");
        float deploy = h.RunUntil(() => h.S.phase == MatchPhase.Play);
        Check.Near(10f, deploy, "deploy lasts 10 s", 0.06f);
        var phases = h.Of<PhaseChangedEvent>().Select(e => e.to).ToArray();
        Check.Equal("HeroPick,Deploy,Play", string.Join(",", phases), "phase events");
        Check.False(h.Match.StartMatch().IsAccepted, "can't start twice");
    }

    [Test]
    static void StartNeedsAPlayerPerTeam()
    {
        var m = new MatchRules(Ruleset.Default, ModeDef.CaptureAndHold(), TestMap(), new FakeWorld(), new GameEventBus());
        m.AddPlayer(1, TeamId.A);
        Check.False(m.StartMatch().IsAccepted, "team B is empty");
        m.AddPlayer(101, TeamId.B);
        Check.True(m.StartMatch().IsAccepted, "1v1 works");
        for (int i = 2; i <= 6; i++) m.AddPlayer(i, TeamId.A);
        Check.False(m.AddPlayer(7, TeamId.A).IsAccepted, "team of five is full");
        Check.False(m.AddPlayer(1, TeamId.B).IsAccepted, "can't join twice");
    }

    [Test]
    static void HeroPickIsUniquePerTeam()
    {
        var h = new MatchHarness(toPlay: false);
        h.Match.StartMatch();
        Check.True(h.Match.Apply(new PickHeroCommand { playerId = h.A[0], heroId = "vanguard" }).IsAccepted, "first pick");
        Check.False(h.Match.Apply(new PickHeroCommand { playerId = h.A[1], heroId = "vanguard" }).IsAccepted, "teammate can't take the same hero");
        Check.True(h.Match.Apply(new PickHeroCommand { playerId = h.B[0], heroId = "vanguard" }).IsAccepted, "the other team can");
        Check.True(h.Match.Apply(new PickHeroCommand { playerId = h.A[0], heroId = "striker" }).IsAccepted, "change your mind during pick");
        Check.True(h.Match.Apply(new PickHeroCommand { playerId = h.A[1], heroId = "vanguard" }).IsAccepted, "freed hero can be taken");
        Check.False(h.Match.Apply(new PickHeroCommand { playerId = h.A[2], heroId = "" }).IsAccepted, "empty pick");
        Check.False(h.Match.Apply(new PickHeroCommand { playerId = 999, heroId = "support" }).IsAccepted, "unknown player");
        Check.Equal(4, h.Of<HeroPickedEvent>().Count, "an event per accepted pick");
        h.RunTo(MatchPhase.Play);
        Check.False(h.Match.Apply(new PickHeroCommand { playerId = h.A[3], heroId = "controller" }).IsAccepted, "no picks once playing");
        Check.Equal(CommandStatus.NotHandled, h.Match.Apply(new ChooseUpgradeCommand { playerId = h.A[0], level = 2 }).status, "upgrades belong to Progression");
        Check.Equal(CommandStatus.NotHandled, h.Match.Apply(new FireCommand { playerId = h.A[0] }).status, "firing belongs to Combat");
    }

    [Test]
    static void HoldingPointsScores()
    {
        var h = new MatchHarness();
        h.Take(Mid, TeamId.A);
        int before = h.S.scoreA;
        h.Run(10f);
        Check.Equal(before + 10, h.S.scoreA, "one point a second for one point");
        Check.Equal(0, h.S.scoreB, "B holds nothing");

        h.Take(NearB, TeamId.B);
        int a = h.S.scoreA, b = h.S.scoreB;
        h.Run(10f);
        Check.Equal(a + 10, h.S.scoreA, "A still 1/s");
        Check.Equal(b + 10, h.S.scoreB, "B now 1/s");

        // A takes all three: neutralise B's point, capture it, and capture A's own.
        h.Take(NearA, TeamId.A);
        h.World.Put(h.A.Take(3), NearB);
        h.RunUntil(() => h.Zone(NearB).owner == TeamId.A, 30f);
        h.World.ClearAll();
        Check.Equal(3, h.S.ZonesHeld(TeamId.A), "A holds all three");
        Check.Near(6f, h.Match.Zones.ScoreRate(TeamId.A), "holding all three doubles the rate");
        a = h.S.scoreA;
        h.Run(10f);
        Check.Equal(a + 60, h.S.scoreA, "6 points a second");
    }

    [Test]
    static void FirstTo1000Wins()
    {
        var h = new MatchHarness();
        h.Take(NearA, TeamId.A); h.Take(Mid, TeamId.A); h.Take(NearB, TeamId.A);
        h.RunTo(MatchPhase.MatchEnd);
        Check.Equal(TeamId.A, h.S.winner, "A wins");
        Check.Equal(MatchEndReason.ScoreReached, h.S.endReason, "by score");
        Check.Equal(1000, h.S.scoreA, "score stops at the cap");
        var end = h.Last<MatchEndedEvent>();
        Check.Equal(1000, end.scoreA, "end event score");
        int a = h.S.scoreA;
        h.Run(5f);
        Check.Equal(a, h.S.scoreA, "no scoring after the end");
        Check.Near(5f, h.S.phaseTimeLeft, "scoreboard counts down", 0.06f);
    }

    [Test]
    static void MapShiftsOnceAtHalfScore()
    {
        var h = new MatchHarness();
        h.Take(NearA, TeamId.A); h.Take(Mid, TeamId.A); h.Take(NearB, TeamId.A);
        h.RunUntil(() => h.S.mapShifted, 600f);
        var shift = h.Last<MapShiftEvent>();
        Check.Equal(TeamId.A, shift.team, "the leading team triggered it");
        Check.True(shift.score >= 500 && shift.score <= 506, "at 500 points");
        h.RunTo(MatchPhase.MatchEnd);
        Check.Equal(1, h.Of<MapShiftEvent>().Count, "only once");
    }

    [Test]
    static void TimeCapHigherScoreWins()
    {
        var mode = ModeDef.CaptureAndHold();
        mode.timeCapSeconds = 60f;
        var h = new MatchHarness(mode);
        h.Take(Mid, TeamId.B);
        h.RunTo(MatchPhase.MatchEnd);
        Check.Equal(TeamId.B, h.S.winner, "B led at the cap");
        Check.Equal(MatchEndReason.TimeCap, h.S.endReason, "time cap");
        Check.Near(60f, h.S.elapsed, "ended at 60 s", 0.06f);
    }

    [Test]
    static void TimeCapTiePlaysOn()
    {
        var mode = ModeDef.CaptureAndHold();
        mode.timeCapSeconds = 20f;
        var h = new MatchHarness(mode);
        h.Run(25f);
        Check.Equal(MatchPhase.Play, h.S.phase, "level at the cap: keep playing");
        h.Take(Mid, TeamId.A);
        h.RunTo(MatchPhase.MatchEnd, 30f);
        Check.Equal(TeamId.A, h.S.winner, "first to break the tie wins");
    }

    /// <summary>A holds all three and is about to cap; B is on A's middle point.</summary>
    static MatchHarness OvertimeSetup()
    {
        var mode = ModeDef.CaptureAndHold();
        mode.scoreToWin = 100;
        mode.mapShiftScore = 0;
        var h = new MatchHarness(mode);
        h.Take(NearA, TeamId.A); h.Take(Mid, TeamId.A); h.Take(NearB, TeamId.A);
        h.RunUntil(() => h.S.scoreA >= 94, 60f);
        h.World.Put(h.A[1], Mid); // a defender keeps it contested, so B can't take it outright
        h.World.Put(h.B[0], Mid);
        h.RunTo(MatchPhase.Overtime, 10f);
        return h;
    }

    [Test]
    static void OvertimeStartsWhileTrailingTeamFights()
    {
        var h = OvertimeSetup();
        Check.Equal(100, h.S.scoreA, "A reached the cap");
        Check.Equal(TeamId.A, h.Last<OvertimeStartedEvent>().leader, "A leads into overtime");
        h.Run(20f);
        Check.Equal(MatchPhase.Overtime, h.S.phase, "overtime lasts while B stays on the point");

        h.Match.EliminatePlayer(h.B[0], h.A[0]);
        float t = h.RunUntil(() => h.S.phase == MatchPhase.MatchEnd, 10f);
        Check.Near(Ruleset.Default.overtimeGraceSeconds, t, "ends after the grace once B is cleared", 0.11f);
        Check.Equal(TeamId.A, h.S.winner, "A wins");
        Check.Equal(MatchEndReason.OvertimeHeld, h.S.endReason, "held in overtime");
    }

    [Test]
    static void OvertimeGraceForgivesStepping()
    {
        var h = OvertimeSetup();
        h.World.Clear(h.B[0]);
        h.Run(0.5f);
        h.World.Put(h.B[0], Mid);
        h.Run(5f);
        Check.Equal(MatchPhase.Overtime, h.S.phase, "stepping off for less than the grace doesn't end it");
    }

    [Test]
    static void NoOvertimeWithoutAFight()
    {
        var mode = ModeDef.CaptureAndHold();
        mode.scoreToWin = 50;
        mode.mapShiftScore = 0;
        var h = new MatchHarness(mode);
        h.Take(Mid, TeamId.A);
        h.Take(NearB, TeamId.B);
        h.World.Put(h.B[0], NearB); // on their own quiet point: not fighting
        h.RunTo(MatchPhase.MatchEnd, 120f);
        Check.Equal(0, h.Of<OvertimeStartedEvent>().Count, "no overtime");
        Check.Equal(MatchEndReason.ScoreReached, h.S.endReason, "plain win");

        var off = Ruleset.Default;
        off.overtimeEnabled = false;
        var h2 = new MatchHarness(mode, rules: off);
        h2.Take(Mid, TeamId.A);
        h2.World.Put(h2.B[0], Mid);
        h2.RunTo(MatchPhase.MatchEnd, 120f);
        Check.Equal(0, h2.Of<OvertimeStartedEvent>().Count, "overtime can be switched off");
    }

    [Test]
    static void OvertimeComeback()
    {
        var mode = ModeDef.CaptureAndHold();
        mode.scoreToWin = 100;
        mode.mapShiftScore = 0;
        var h = new MatchHarness(mode);
        h.Take(NearB, TeamId.B);
        h.Take(Mid, TeamId.A);
        h.RunUntil(() => h.S.scoreA >= 60, 120f);
        h.Take(NearA, TeamId.A); // A holds two, B one
        h.RunUntil(() => h.S.scoreA >= 99, 60f);
        h.World.Put(h.B.Take(3), Mid); // B fights for the middle as A caps
        h.RunTo(MatchPhase.Overtime, 10f);
        // B neutralises and takes the middle, then keeps scoring from two points until it caps too.
        h.RunUntil(() => h.Zone(Mid).owner == TeamId.B, 20f);
        h.World.Put(h.B.Skip(3).Take(2), NearA);
        h.RunTo(MatchPhase.MatchEnd, 300f);
        Check.Equal(TeamId.B, h.S.winner, "the trailing team reached the cap in overtime");
        Check.Equal(MatchEndReason.OvertimeComeback, h.S.endReason, "comeback");
    }

    [Test]
    static void OvertimeCanBeCapped()
    {
        var rules = Ruleset.Default;
        rules.overtimeMaxSeconds = 15f;
        var mode = ModeDef.CaptureAndHold();
        mode.scoreToWin = 100;
        mode.mapShiftScore = 0;
        var h = new MatchHarness(mode, rules: rules);
        h.Take(Mid, TeamId.A);
        h.RunUntil(() => h.S.scoreA >= 98, 200f);
        h.World.Put(h.A[1], Mid);
        h.World.Put(h.B[0], Mid);
        h.RunTo(MatchPhase.Overtime, 10f);
        float t = h.RunUntil(() => h.S.phase == MatchPhase.MatchEnd, 30f);
        Check.Near(15f, t, "overtime stops at its limit", 0.11f);
        Check.Equal(MatchEndReason.OvertimeTimeout, h.S.endReason, "timeout");
        Check.Equal(TeamId.A, h.S.winner, "higher score wins");
    }

    [Test]
    static void KingOfTheHillRotates()
    {
        var h = new MatchHarness(ModeDef.KingOfTheHill());
        Check.Equal("11", string.Join(",", h.S.zones.Where(z => z.active).Select(z => z.id.value)), "first hill live");
        h.Take(Hills[0], TeamId.A);
        int before = h.S.scoreA;
        h.Run(10f);
        Check.Equal(before + 20, h.S.scoreA, "the holder scores 2 per second");

        h.RunUntil(() => h.Of<ZoneUpcomingEvent>().Count > 0, 120f);
        Check.Near(100f, h.S.elapsed, "next hill announced 20 s ahead", 0.06f);
        Check.Equal(Hills[1], h.Last<ZoneUpcomingEvent>().zone, "announces the next hill in order");

        h.RunUntil(() => h.Zone(Hills[1]).active, 30f);
        Check.Near(120f, h.S.elapsed, "moves after 2 minutes", 0.06f);
        Check.False(h.Zone(Hills[0]).active, "old hill switched off");
        Check.Equal(TeamId.None, h.Zone(Hills[0]).owner, "old hill neutral again");
        Check.Equal(TeamId.None, h.Zone(Hills[1]).owner, "new hill starts neutral");
        Check.Near(0f, h.Match.Zones.ScoreRate(TeamId.A), "nobody scores until it is taken");

        h.Run(3 * 120f + 1f);
        Check.True(h.Zone(Hills[0]).active, "wraps around after the last hill");
    }

    [Test]
    static void ScriptedCaptureAndHoldMatch()
    {
        string first = PlayScriptedMatch(out var h);
        Check.Equal(MatchPhase.MatchEnd, h.S.phase, "the match ends");
        Check.Equal(TeamId.B, h.S.winner, "B's late push wins it");
        Check.Equal(1000, h.S.scoreB, "B reached 1000");
        Check.True(h.S.scoreA > 300, "A scored too");
        Check.True(h.S.elapsed > 300f && h.S.elapsed < 900f, "ends before the time cap: " + h.S.elapsed);
        Check.Equal(1, h.Of<MapShiftEvent>().Count, "one map shift");
        Check.True(h.Of<PlayerEliminatedEvent>().Count >= 10, "fights happened");
        Check.True(h.Of<PlayerSpawnedEvent>().Count(e => !e.initial) >= 10, "players respawned");
        Check.Equal(first, PlayScriptedMatch(out _), "same inputs give the same match, event for event");
    }

    /// <summary>
    /// Ten minutes of scripted play: A takes its point and the middle early, B fights back and
    /// eventually holds all three. Returns the event log.
    /// </summary>
    static string PlayScriptedMatch(out MatchHarness h)
    {
        h = new MatchHarness(toPlay: false);
        h.Match.StartMatch();
        string[] heroes = { "vanguard", "striker", "support", "controller", "vanguard2" };
        for (int i = 0; i < 5; i++)
        {
            h.Match.Apply(new PickHeroCommand { playerId = h.A[i], heroId = heroes[i] });
            h.Match.Apply(new PickHeroCommand { playerId = h.B[i], heroId = heroes[i] });
        }
        h.RunTo(MatchPhase.Play);
        var m = h;

        // Minutes 0-1: each team takes its own point, A also takes the middle.
        m.World.Put(m.A.Take(2), NearA);
        m.World.Put(m.A.Skip(2).Take(3), Mid);
        m.World.Put(m.B.Take(2), NearB);
        m.RunUntil(() => m.Zone(Mid).owner == TeamId.A && m.Zone(NearB).owner == TeamId.B, 60f);
        m.World.ClearAll();

        // Minutes 1-6: B fights for the middle every 30 s and loses each fight.
        for (int round = 0; round < 10; round++)
        {
            m.World.Put(m.B.Take(2), Mid);
            m.Run(2f);
            foreach (var id in m.B.Take(2)) { m.Match.EliminatePlayer(id, m.A[round % 5]); m.World.Clear(id); }
            m.Run(28f);
        }

        // Minutes 6+: B wipes the middle, takes it, then takes A's point and holds all three.
        foreach (var id in m.A.Take(4)) m.Match.EliminatePlayer(id, m.B[0]);
        m.World.Put(m.B.Take(3), Mid);
        m.RunUntil(() => m.Zone(Mid).owner == TeamId.B, 30f);
        m.World.ClearAll();
        m.Match.Apply(new PickSpawnCommand { playerId = m.B[3], zone = NearB });
        m.World.Put(m.B.Take(3), NearA);
        m.RunUntil(() => m.Zone(NearA).owner == TeamId.B, 60f);
        m.World.ClearAll();
        m.RunTo(MatchPhase.MatchEnd, 900f);
        return m.Log();
    }

    [Test]
    static void ScriptedKingOfTheHillMatch()
    {
        var h = new MatchHarness(ModeDef.KingOfTheHill());
        // A wins every hill: the first three of each team chase the live hill.
        h.RunUntil(() =>
        {
            h.World.ClearAll();
            var live = h.S.zones.First(z => z.active).id;
            h.World.Put(h.A.Take(3), live);
            return h.S.phase == MatchPhase.MatchEnd;
        }, 900f, "match end");
        Check.Equal(TeamId.A, h.S.winner, "A wins");
        Check.Equal(500, h.S.scoreA, "first to 500");
        Check.True(h.Of<ZoneDeactivatedEvent>().Count >= 2, "the hill moved during the match");
        Check.Equal(1, h.Of<MapShiftEvent>().Count, "map shift at 250");
    }

    [Test]
    static void SnapshotIsADeepCopy()
    {
        var h = new MatchHarness();
        h.Take(Mid, TeamId.A);
        var snap = h.Match.TakeSnapshot();
        Check.Equal(h.S.tick, snap.tick, "snapshot tick");
        Check.Equal(TeamId.A, snap.match.Zone(Mid).owner, "zone in the snapshot");
        h.World.Put(h.B[0], Mid);
        h.Match.EliminatePlayer(h.A[0]);
        h.Run(7f);
        Check.Equal(TeamId.A, snap.match.Zone(Mid).owner, "snapshot zone doesn't change with the match");
        Check.True(snap.match.Player(h.A[0]).alive, "snapshot players don't change with the match");
    }
}
