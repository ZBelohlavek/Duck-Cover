using System;
using System.Collections.Generic;
using System.Linq;
using DuckAndCover.Core;

/// <summary>
/// A Flag Rush match in Play on the test map (bases 21 and 22, flag spots at x = -10, 0, 10) with
/// combat, heroes (A1 plays Lancer for the dash) and the flag. Team A is 1..n, team B 101..100+n.
/// Bodies stand where tests put them; base zones are answered by <see cref="BoxWorld.OnZone"/>.
/// </summary>
sealed class FlagHarness
{
    public const float Dt = 0.05f;
    public const int A1 = 1, A2 = 2, B1 = 101, B2 = 102;
    public static readonly ZoneId BaseA = new ZoneId(21), BaseB = new ZoneId(22);

    public readonly BoxWorld Map = new BoxWorld();
    public readonly GameEventBus Events = new GameEventBus { Record = true };
    public readonly MatchRules Match;
    public readonly CombatServer Combat;
    public readonly HeroServer Heroes;
    public readonly FlagServer Flag;

    public MatchState S => Match.State;
    public FlagState F => Flag.State;
    public Ruleset R => Match.Rules;

    public FlagHarness(int seed = 7, Ruleset rules = null, ModeDef mode = null, int perTeam = 2)
    {
        rules = rules ?? CombatHarness.QuickRules();
        Match = new MatchRules(rules, mode ?? ModeDef.FlagRush(), MatchHarness.TestMap(), Map, Events);
        Combat = new CombatServer(Match, Map, Events, Dt, ArenaLayout.Placeholder(), StarterWeapons.Library(), HeroHarness.DataLibrary());
        Heroes = new HeroServer(Combat, ProgressionHarness.DataCatalog(), Map, Events);
        Flag = new FlagServer(Combat, Map, Events, seed, Heroes.Progression);
        for (int i = 1; i <= perTeam; i++)
        {
            Match.AddPlayer(i, TeamId.A);
            Match.AddPlayer(100 + i, TeamId.B);
        }
        Check.True(Match.Apply(new PickHeroCommand { playerId = A1, heroId = "lancer" }).IsAccepted, "A1 picks Lancer");
        Check.True(Match.StartMatch().IsAccepted, "match starts");
        while (S.phase != MatchPhase.Play) Tick();
        Park();
    }

    /// <summary>Everyone well away from the flag spots and the bases.</summary>
    public void Park()
    {
        foreach (var p in S.players) Place(p.id, p.id * 3f, p.team == TeamId.A ? -40f : 40f);
        Map.OnZone.Clear();
    }

    public void Tick()
    {
        Match.Tick(Dt);
        Combat.Tick(Dt);
        Heroes.Tick(Dt);
        Flag.Tick(Dt);
    }

    public void Run(float seconds)
    {
        int n = (int)Math.Round(seconds / Dt);
        for (int i = 0; i < n; i++) Tick();
    }

    public float RunUntil(Func<bool> done, float limit = 120f, string what = "condition")
    {
        int n = (int)(limit / Dt);
        for (int i = 0; i < n; i++)
        {
            if (done()) return i * Dt;
            Tick();
        }
        Check.True(done(), what + " within " + limit + " s");
        return limit;
    }

    public void Place(int id, float x, float z, float y = 0f)
    {
        var m = Combat.Movement.Motion(id);
        if (m != null) m.position = new Vec3(x, y, z);
    }

    public void PlaceAt(int id, Vec3 p) => Place(id, p.x, p.z, p.y);

    /// <summary>Wait for the flag to lie somewhere, walk <paramref name="id"/> onto it, and tick once.</summary>
    public void Take(int id)
    {
        RunUntil(() => F.status == FlagStatus.AtSpot || F.status == FlagStatus.Dropped, 120f, "a flag to take");
        PlaceAt(id, F.position);
        Tick();
        Check.True(F.IsCarrier(id), id + " carries the flag");
    }

    /// <summary>Carry the flag into the enemy base and score.</summary>
    public void Score(int id)
    {
        Take(id);
        var team = S.Player(id).team;
        Map.OnZone[id] = Flag.BaseOf(team.Other());
        Tick();
        Map.OnZone.Remove(id);
        Park();
    }

    public List<T> Of<T>() where T : GameEvent => Events.History.OfType<T>().ToList();
    public T Last<T>() where T : GameEvent => Events.History.OfType<T>().LastOrDefault();
}

/// <summary>Flag Rush (WP21): the flag's life, the carrier, scoring, the match result and the map.</summary>
static class FlagTests
{
    static readonly Vec3[] Spots = MatchHarness.TestMap().flagSpots.ToArray();

    [Test]
    static void FlagAppearsAfterTheFirstDelayAtAnAnnouncedSpot()
    {
        var h = new FlagHarness();
        h.Tick();
        var upcoming = h.Last<FlagUpcomingEvent>();
        Check.True(upcoming != null, "the first spot is announced as Play starts");
        Check.Equal(FlagStatus.Waiting, h.F.status, "not on the map yet");
        Check.True(Spots.Contains(upcoming.position), "announced at a map spot");
        Check.Near(h.R.flagFirstSpawnSeconds, upcoming.seconds, "appears after the first delay", 0.1f);
        float took = h.RunUntil(() => h.F.status == FlagStatus.AtSpot, 60f, "the flag appears");
        Check.Near(h.R.flagFirstSpawnSeconds, took + 2 * FlagHarness.Dt, "about 20 s into Play", 0.15f);
        Check.Equal(upcoming.position, h.F.position, "where it was announced");
        Check.Equal(upcoming.spot, h.Last<FlagAppearedEvent>().spot, "appeared event names the spot");
    }

    [Test]
    static void SpotsComeFromTheSeedAndNeverRepeat()
    {
        string Sequence(int seed)
        {
            var h = new FlagHarness(seed);
            for (int i = 0; i < 4; i++) h.Score(i % 2 == 0 ? FlagHarness.A1 : FlagHarness.B1);
            var spots = h.Of<FlagAppearedEvent>().Select(e => e.spot).ToList();
            for (int i = 1; i < spots.Count; i++) Check.True(spots[i] != spots[i - 1], "seed " + seed + ": never the same spot twice running");
            return string.Join(",", spots);
        }
        Check.Equal(Sequence(3), Sequence(3), "the same seed picks the same spots");
        var all = new HashSet<string>();
        for (int seed = 1; seed <= 8; seed++) all.Add(Sequence(seed));
        Check.True(all.Count > 1, "different seeds pick different spots");
    }

    [Test]
    static void TouchPicksUpTheNearestLivingPlayer()
    {
        var h = new FlagHarness();
        h.RunUntil(() => h.F.status == FlagStatus.AtSpot);
        var at = h.F.position;
        h.Place(FlagHarness.A1, at.x + 1.4f, at.z);
        h.Place(FlagHarness.B1, at.x - 0.5f, at.z);
        h.Place(FlagHarness.A2, at.x, at.z, 3f); // on something above it: too high
        h.Match.EliminatePlayer(FlagHarness.B1, FlagHarness.A2);
        h.Tick();
        Check.True(h.F.IsCarrier(FlagHarness.A1), "the downed player can't take it; the one in reach does");
        var taken = h.Last<FlagTakenEvent>();
        Check.Equal(TeamId.A, taken.team, "taken by team A");
        Check.False(taken.fromDrop, "from its spot");

        var h2 = new FlagHarness();
        h2.RunUntil(() => h2.F.status == FlagStatus.AtSpot);
        at = h2.F.position;
        h2.Place(FlagHarness.A1, at.x + 1.6f, at.z);
        h2.Tick();
        Check.Equal(FlagStatus.AtSpot, h2.F.status, "1.6 m away is out of reach");
        h2.Place(FlagHarness.B1, at.x + 1f, at.z);
        h2.Place(FlagHarness.A1, at.x - 0.4f, at.z);
        h2.Tick();
        Check.True(h2.F.IsCarrier(FlagHarness.A1), "the nearest of two takes it");
    }

    [Test]
    static void CarrierCantSprintOrDashButCanFight()
    {
        var h = new FlagHarness();
        var r = h.R;
        Check.Near(r.TopSpeed, h.Combat.Movement.TopSpeed(FlagHarness.A1), "sprint speed before");
        h.Take(FlagHarness.A1);
        Check.Near(r.walkSpeed, h.Combat.Movement.TopSpeed(FlagHarness.A1), "walk speed while carrying");
        Check.Near(r.TopSpeed, h.Combat.Movement.TopSpeed(FlagHarness.B1), "others still sprint");

        var m = h.Combat.Movement.Motion(FlagHarness.A1);
        var moved = h.Combat.Apply(new MoveCommand { playerId = FlagHarness.A1, position = m.position + new Vec3(0.3f, 0f, 0f), yaw = m.yaw, placement = m.placement, sprinting = true });
        Check.True(moved.IsAccepted, "a walking step is fine");
        Check.False(m.sprinting, "never marked sprinting");

        Check.True(h.Flag.MovesCaster("lancer_slip"), "Slip moves its caster");
        Check.False(h.Flag.MovesCaster("lancer_mark"), "Tracer Mark doesn't");
        var eye = m.position + Vec3.Up * 1.6f;
        var dash = h.Combat.Apply(new UseAbilityCommand { playerId = FlagHarness.A1, slot = 0, aimOrigin = eye, aimDirection = Vec3.Forward, tick = h.S.tick });
        Check.False(dash.IsAccepted, "no dash with the flag");
        Check.True(dash.reason.Contains("carrying the flag"), "says why: " + dash.reason);
        var mark = h.Combat.Apply(new UseAbilityCommand { playerId = FlagHarness.A1, slot = 1, aimOrigin = eye, aimDirection = Vec3.Forward, tick = h.S.tick });
        Check.True(mark.IsAccepted, "other abilities still work: " + mark.reason);

        h.Map.OnZone[FlagHarness.A1] = FlagHarness.BaseB;
        h.Tick();
        Check.Near(r.TopSpeed, h.Combat.Movement.TopSpeed(FlagHarness.A1), "sprint is back after scoring");
    }

    [Test]
    static void CarriedFlagFollowsAndScoresOnlyInTheEnemyBase()
    {
        var h = new FlagHarness();
        h.Take(FlagHarness.A1);
        h.Place(FlagHarness.A1, 4f, -7f);
        h.Tick();
        Check.Equal(new Vec3(4f, 0f, -7f), h.F.position, "the flag rides with its carrier");

        h.Map.OnZone[FlagHarness.A1] = FlagHarness.BaseA;
        h.Run(1f);
        Check.Equal(0, h.S.scoreA, "own base: nothing");
        Check.True(h.F.IsCarrier(FlagHarness.A1), "still carrying");

        h.Map.OnZone[FlagHarness.A1] = FlagHarness.BaseB;
        h.Tick();
        Check.Equal(1, h.S.scoreA, "enemy base: one capture");
        var cap = h.Last<FlagCapturedEvent>();
        Check.Equal(FlagHarness.A1, cap.playerId, "the carrier scored");
        Check.Equal(1, cap.scoreA, "event carries the new score");
        Check.Equal(FlagStatus.Waiting, h.F.status, "the flag is gone");
        Check.Near(h.R.flagAfterCaptureSeconds, h.F.appearAt - h.S.elapsed, "next one in 10 s", 0.06f);
        Check.Equal(1, h.F.captures, "counted");
        Check.True(h.S.zones.All(z => !z.active), "base zones never go live as points");
    }

    [Test]
    static void DroppedOnDeathThenTakenOrReset()
    {
        var h = new FlagHarness();
        h.Take(FlagHarness.A1);
        h.Place(FlagHarness.A1, 6f, -3f);
        h.Tick();
        h.Match.EliminatePlayer(FlagHarness.A1, FlagHarness.B1);
        var drop = h.Last<FlagDroppedEvent>();
        Check.True(drop != null, "the flag drops the moment its carrier goes down");
        Check.Equal(FlagStatus.Dropped, h.F.status, "on the ground");
        Check.Equal(new Vec3(6f, 0f, -3f), h.F.position, "where they fell");
        Check.Equal(FlagHarness.B1, drop.killerId, "who stopped them");
        Check.True(h.Of<ExperienceAwardedEvent>().Any(e => e.playerId == FlagHarness.B1 && e.source == ExperienceSource.Flag), "stopping the carrier pays");

        h.Run(5f);
        h.Place(FlagHarness.B2, 6.5f, -3f);
        h.Tick();
        Check.True(h.F.IsCarrier(FlagHarness.B2), "anyone may pick a dropped flag up, even the other team");
        Check.True(h.Last<FlagTakenEvent>().fromDrop, "from the drop");

        // Dropped again and left alone: it vanishes after 15 s and appears at another spot at once.
        h.Match.EliminatePlayer(FlagHarness.B2, FlagHarness.A2);
        int lastSpot = h.F.spot;
        h.Park();
        float waited = h.RunUntil(() => h.Of<FlagResetEvent>().Any(), 30f, "the dropped flag resets");
        Check.Near(h.R.flagDropResetSeconds, waited, "after 15 s untouched", 0.1f);
        Check.Equal(FlagStatus.AtSpot, h.F.status, "a new flag is up straight away");
        Check.True(h.F.spot != lastSpot, "at a different spot");
    }

    [Test]
    static void LeavingDropsTheFlag()
    {
        var h = new FlagHarness();
        h.Take(FlagHarness.B1);
        h.Match.RemovePlayer(FlagHarness.B1);
        h.Tick();
        Check.Equal(FlagStatus.Dropped, h.F.status, "a carrier who leaves drops it");
        Check.Equal(-1, h.F.carrierId, "no carrier");
    }

    [Test]
    static void ShiftAtThreeAndWinAtFive()
    {
        var h = new FlagHarness();
        h.Score(FlagHarness.A1);
        h.Score(FlagHarness.B1);
        Check.False(h.S.mapShifted, "1:1, no shift");
        h.Score(FlagHarness.A1);
        h.Score(FlagHarness.A2);
        Check.True(h.S.mapShifted, "the map shifts at 3 captures");
        Check.Equal(TeamId.A, h.Last<MapShiftEvent>().team, "A got there first");
        h.Score(FlagHarness.A1);
        Check.Equal(MatchPhase.Play, h.S.phase, "4 isn't enough");
        h.Score(FlagHarness.A2);
        Check.Equal(MatchPhase.MatchEnd, h.S.phase, "five captures win");
        Check.Equal(TeamId.A, h.S.winner, "team A");
        Check.Equal(MatchEndReason.ScoreReached, h.S.endReason, "by score");
        Check.Equal(5, h.Last<MatchEndedEvent>().scoreA, "5 to 1");
        var order = h.Events.History.Where(e => e is FlagCapturedEvent || e is MatchEndedEvent).Select(e => e.GetType().Name).Last();
        Check.Equal(nameof(MatchEndedEvent), order, "the winning capture is announced before the end");
    }

    [Test]
    static void TimeCapLeaderWinsOrOvertimeUntilTheNextCapture()
    {
        var mode = ModeDef.FlagRush();
        mode.timeCapSeconds = 60f;
        var h = new FlagHarness(mode: mode);
        h.Score(FlagHarness.B1);
        h.RunUntil(() => h.S.phase == MatchPhase.MatchEnd, 120f, "the time cap");
        Check.Equal(TeamId.B, h.S.winner, "the leader at the cap wins");
        Check.Equal(MatchEndReason.TimeCap, h.S.endReason, "by the time cap");

        var tie = new FlagHarness(mode: mode);
        tie.Score(FlagHarness.A1);
        tie.Score(FlagHarness.B1);
        tie.RunUntil(() => tie.S.phase == MatchPhase.Overtime, 120f, "overtime");
        Check.Equal(TeamId.None, tie.Last<OvertimeStartedEvent>().leader, "level: nobody leads");
        tie.Run(20f);
        Check.Equal(MatchPhase.Overtime, tie.S.phase, "overtime has no clock by default");
        tie.Score(FlagHarness.B2);
        Check.Equal(MatchPhase.MatchEnd, tie.S.phase, "the next capture ends it");
        Check.Equal(TeamId.B, tie.S.winner, "won by the team that scored");
        Check.Equal(MatchEndReason.OvertimeCapture, tie.S.endReason, "in overtime");
    }

    [Test]
    static void OvertimeLimitEndsInADraw()
    {
        var mode = ModeDef.FlagRush();
        mode.timeCapSeconds = 30f;
        var rules = CombatHarness.QuickRules();
        rules.overtimeMaxSeconds = 10f;
        rules.flagFirstSpawnSeconds = 100f; // nobody scores
        var h = new FlagHarness(rules: rules, mode: mode);
        h.RunUntil(() => h.S.phase == MatchPhase.MatchEnd, 60f, "the end");
        Check.Equal(TeamId.None, h.S.winner, "a draw");
        Check.Equal(MatchEndReason.OvertimeTimeout, h.S.endReason, "overtime ran out");
    }

    [Test]
    static void FlagExperience()
    {
        var h = new FlagHarness();
        float Xp(int id) => h.Of<ExperienceAwardedEvent>().Where(e => e.playerId == id && e.source == ExperienceSource.Flag).Sum(e => e.amount);
        h.Take(FlagHarness.A1);
        float pickup = Xp(FlagHarness.A1);
        Check.True(pickup >= h.R.xpFlagPickup - 0.01f, "picking up pays " + pickup);
        h.Run(3f);
        float carrying = Xp(FlagHarness.A1) - pickup;
        Check.True(carrying >= h.R.xpFlagCarryPerSecond * 2.9f, "carrying pays by the second: " + carrying);
        var at = h.F.position;
        h.Place(FlagHarness.A2, at.x + 5f, at.z);   // near the capture
        h.Place(FlagHarness.B1, at.x + 4f, at.z);   // enemies near it get nothing
        h.Map.OnZone[FlagHarness.A1] = FlagHarness.BaseB;
        float before = Xp(FlagHarness.A1);
        h.Tick();
        Check.True(Xp(FlagHarness.A1) - before >= h.R.xpFlagCapture - 0.01f, "scoring pays the most");
        Check.True(Xp(FlagHarness.A2) >= h.R.xpFlagCaptureAssist - 0.01f, "a teammate near the capture is paid");
        Check.Equal(0f, Xp(FlagHarness.B1), "the enemy isn't");
        Check.True(h.R.xpFlagCapture > h.R.xpFlagStopCarrier && h.R.xpFlagCapture > h.R.xpFlagCaptureAssist && h.R.xpFlagCapture > h.R.xpFlagPickup, "capturing pays the most");
        Check.Equal(FlagHarness.A2, h.Last<FlagCapturedEvent>().assists.Single(), "the assist is named");
    }

    [Test]
    static void FlagModeNeedsBasesAndSpots()
    {
        var map = MatchHarness.TestMap();
        Check.Equal(0, ModeDef.FlagRush().Validate(map).Count, "fits the test map");
        map.flagSpots.Clear();
        Check.True(ModeDef.FlagRush().Validate(map).Any(e => e.Contains("flag spots")), "needs spots");
        map = MatchHarness.TestMap();
        map.ZoneSet("bases").zones[1].startOwner = TeamId.A;
        Check.True(ModeDef.FlagRush().Validate(map).Any(e => e.Contains("one base zone per team")), "one base each");
        var bad = ModeDef.FlagRush();
        bad.objective = "gold";
        Check.True(bad.Validate(null).Any(e => e.Contains("objective")), "unknown objective");
        Check.Equal(TeamId.A, ModeDef.FlagRush().BaseZone(MatchHarness.TestMap(), TeamId.A).startOwner, "base zone by team");
        Check.True(Throws(() => new FlagServer(new CombatHarness().Combat, new BoxWorld(), new GameEventBus(), 1)), "no flag in capture and hold");
        var broken = CombatHarness.QuickRules();
        broken.flagDropResetSeconds = 0f;
        Check.True(broken.ValidateFlag().Count > 0, "flag numbers are checked");
    }

    [Test]
    static void FlagRidesInSnapshotsOverTheWire()
    {
        var h = new FlagHarness();
        h.Take(FlagHarness.B1);
        var s = new MatchSnapshot();
        h.Flag.FillSnapshot(s);
        var back = (SnapshotMessage)WireCodec.Decode(WireCodec.Encode(new SnapshotMessage { snapshot = s }));
        Check.Equal(FlagStatus.Carried, back.snapshot.flag.status, "status");
        Check.Equal(FlagHarness.B1, back.snapshot.flag.carrierId, "carrier");
        Check.Equal(h.F.position, back.snapshot.flag.position, "position");
        Check.True(back.snapshot.flag.IsCarrier(FlagHarness.B1), "carrier for everyone");
        var none = (SnapshotMessage)WireCodec.Decode(WireCodec.Encode(new SnapshotMessage { snapshot = new MatchSnapshot() }));
        Check.True(none.snapshot.flag == null, "no flag in other modes");
    }

    [Test]
    static void DisposeGivesTheHooksBack()
    {
        var h = new FlagHarness();
        h.Flag.Dispose();
        Check.True(h.Combat.Movement.NoSprint == null, "sprint check removed");
        Check.True(h.Combat.Abilities.UseCheck == null, "the ability check is back to none");
    }

    [Test]
    static void SameSeedSameMatch()
    {
        string Run()
        {
            var h = new FlagHarness(11);
            h.Score(FlagHarness.A1);
            h.Take(FlagHarness.B1);
            h.Match.EliminatePlayer(FlagHarness.B1, FlagHarness.A1);
            h.Park();
            h.Run(40f);
            return string.Join("\n", h.Events.History.Where(e => e.GetType().Name.StartsWith("Flag")).Select(e => e.tick + " " + e.GetType().Name));
        }
        Check.Equal(Run(), Run(), "the flag's story repeats exactly");
    }

    // ---- The map ----

    [Test]
    static void GantryYardCarriesFlagRush()
    {
        var map = MapCatalog.Default;
        var match = map.ToMatchMap();
        Check.Equal(0, ModeDef.FlagRush().Validate(match).Count, "Flag Rush fits Gantry Yard");
        Check.Equal("21,22", string.Join(",", match.ZoneSet("bases").zones.Select(z => z.id.value)), "two base zones");
        Check.Equal(GantryYard.BaseA, ModeDef.FlagRush().BaseZone(match, TeamId.A).id.value, "Base A is team A's");
        Check.True(map.flagSpots.Count >= 4, "at least two pairs of flag spots");
        var report = MapValidator.Check(map);
        Check.True(report.Ok, "the validator passes: " + string.Join("; ", report.errors));
        Check.Equal(map.flagSpots.Count, report.flagSpots.Count, "every spot measured");
        foreach (var row in report.flagSpots) Check.True(row.Skew <= 0.02f, row.spot + " is within 2% (" + (row.Skew * 100f).ToString("0.0") + "%)");
        // The base zone is inside the spawn room, reached by both of its side doors.
        var baseA = map.Zone(new ZoneId(GantryYard.BaseA));
        Check.True(map.spawnsA.Any(sp => Vec3.Distance(sp.position, baseA.center) < 8f), "Base A is in team A's spawn room");
    }

    [Test]
    static void ValidatorCatchesUnfairFlagSpots()
    {
        void Expect(Action<MapDef> change, string expected, string what)
        {
            var map = GantryYard.Build();
            change(map);
            var report = MapValidator.Check(map);
            Check.True(report.errors.Any(e => e.Contains(expected)), what + ": says '" + expected + "' (got: " + string.Join("; ", report.errors.Take(3)) + ")");
        }
        Expect(m => { m.flagSpots.Add(new Vec3(-16f, 0f, -32f)); m.flagSpots.Add(new Vec3(16f, 0f, 32f)); }, "closer to one base", "a spot on A's point");
        Expect(m => m.flagSpots.Add(new Vec3(-26f, 0f, 1f)), "no twin", "a spot without its twin");
        Expect(m => { m.flagSpots.Add(new Vec3(3.5f, 0f, -31f)); m.flagSpots.Add(new Vec3(-3.5f, 0f, 31f)); }, "not on open floor", "a spot inside a crate");
        Expect(m => m.flagSpots.Clear(), "flag spots", "no spots");
        Expect(m => m.zones.RemoveAll(z => z.set == "bases"), "'bases'", "no base zones");
    }

    // ---- Over the network ----

    [Test]
    static void FullFlagMatchOverTheNetwork()
    {
        var h = new SessionHarness(ModeDef.FlagRush(), heroes: true);
        var scripts = new List<ScriptedPlayer>();
        for (int i = 0; i < 4; i++)
        {
            var c = h.Join();
            var s = new ScriptedPlayer(c, 4);
            scripts.Add(s);
            c.SnapshotReceived += s.Step;
        }
        int limit = (int)((ModeDef.FlagRush().timeCapSeconds + 120f) * h.Host.Settings.tickRate);
        for (int i = 0; i < limit && h.Match.State.phase != MatchPhase.MatchEnd; i++) { h.Host.Tick(); h.Net.Pump(); }
        h.Ticks(4);

        var s0 = h.Match.State;
        var events = h.Events.History;
        Check.Equal(MatchPhase.MatchEnd, s0.phase, "the flag match finished");
        Check.True(s0.winner != TeamId.None, "someone won");
        Check.True(events.OfType<FlagCapturedEvent>().Count() >= 3, "flags were captured: " + events.OfType<FlagCapturedEvent>().Count());
        Check.True(events.OfType<MapShiftEvent>().Any(), "the map shifted");
        Check.True(events.OfType<FlagTakenEvent>().Any(), "flags were taken by touch");
        Check.True(events.OfType<ExperienceAwardedEvent>().Any(e => e.source == ExperienceSource.Flag), "flag experience was paid");
        Check.False(events.OfType<ZoneCapturedEvent>().Any(), "no zone was ever captured");
        string truth = SessionDigest.Of(s0);
        for (int i = 0; i < h.Clients.Count; i++)
        {
            Check.Equal(truth, SessionDigest.Of(h.Clients[i].State), "client " + (i + 1) + " agrees with the host");
            Check.True(h.Clients[i].Snapshot.flag != null, "client " + (i + 1) + " sees the flag");
            Check.True(scripts[i].Finished, "client " + (i + 1) + " saw the end");
        }
        Check.True(events.OfType<FlagDroppedEvent>().Any(), "carriers were shot down and dropped the flag");
    }

    static bool Throws(Action a)
    {
        try { a(); return false; }
        catch (ArgumentException) { return true; }
    }
}
