using System;
using System.Collections.Generic;
using System.Linq;
using DuckAndCover.Core;

/// <summary>
/// A combat match with a straight 200 m test lane along z (team A's end at z = -100) and
/// <see cref="LaneServer"/> on top. Heroes start far off to the side, out of every minion's reach.
/// </summary>
sealed class LaneHarness
{
    public readonly CombatHarness H;
    public readonly LaneServer L;
    public ProgressionRules P;

    public const float HalfLength = 100f;

    public static LaneDef TestLane() => new LaneDef { id = "test", points = { new Vec3(0f, 0f, -LaneHarness.HalfLength), new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, LaneHarness.HalfLength) }, leash = 8f };

    public LaneHarness(List<CampDef> camps = null, Ruleset rules = null, bool waves = true, bool progression = false, int perTeam = 2)
    {
        var r = rules ?? CombatHarness.QuickRules();
        if (!waves) r.lanes.firstWaveSeconds = 100000f;
        H = new CombatHarness(perTeam, r, toPlay: false);
        if (progression) P = new ProgressionRules(H.Match, ProgressionHarness.DataCatalog(), H.Map, H.Events);
        L = new LaneServer(H.Match, H.Combat, H.Map, H.Events, new List<LaneDef> { TestLane() }, camps ?? new List<CampDef>(), P);
        Check.True(H.Match.StartMatch().IsAccepted, "match starts");
        while (H.S.phase != MatchPhase.Play) H.Tick();
        Park();
        Tick(); // camps put out their guards on the first tick of play
        H.Forget();
    }

    /// <summary>Every hero far away from the lane and any camp.</summary>
    public void Park()
    {
        foreach (var p in H.S.players) H.Place(p.id, 300f + p.id, p.team == TeamId.A ? -50f : 50f);
    }

    public void Tick()
    {
        H.Match.Tick(CombatHarness.Dt);
        P?.Tick();
        H.Combat.Tick(CombatHarness.Dt);
        L.Tick(CombatHarness.Dt);
    }

    public void Run(float seconds, Action each = null)
    {
        int n = (int)Math.Round(seconds / CombatHarness.Dt);
        for (int i = 0; i < n; i++) { Tick(); each?.Invoke(); }
    }

    public IEnumerable<UnitState> Minions(TeamId team) =>
        L.State.minions.Where(m => m.team == team).Select(m => H.Combat.Units.Unit(m.unitId)).Where(u => u != null && u.alive);
}

/// <summary>WP15: lanes, minion waves and the four kinds of neutral camp.</summary>
static class LaneTests
{
    static CampDef Camp(string id, CampKind kind, float x, float z, TeamId half = TeamId.None) =>
        new CampDef { id = id, kind = kind, pad = new Vec3(x, 0f, z), half = half };

    // ---- Lane data ----

    [Test]
    static void LanePointsDirectionsAndProjection()
    {
        var lane = new LaneDef { points = { new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 10f), new Vec3(10f, 0f, 10f) } };
        Check.Near(20f, lane.Length, "length");
        Check.True(Vec3.Approximately(new Vec3(0f, 0f, 5f), lane.PointAt(5f)), "point on the first leg");
        Check.True(Vec3.Approximately(new Vec3(5f, 0f, 10f), lane.PointAt(15f)), "point on the second leg");
        Check.True(Vec3.Approximately(new Vec3(10f, 0f, 10f), lane.PointAt(99f)), "clamped to the end");
        Check.True(Vec3.Approximately(Vec3.Right, lane.DirectionAt(12f)), "direction on the second leg");
        float along = lane.Project(new Vec3(3f, 0f, 13f), 0f, 20f, out float off);
        Check.Near(13f, along, "projected along");
        Check.Near(3f, off, "off the line");
        lane.Project(new Vec3(3f, 0f, 13f), 0f, 5f, out float offNear);
        Check.True(offNear > 7f, "a window keeps the projection on the right part of a winding lane");
        Check.True(new LaneDef { points = { Vec3.Zero } }.Validate().Count > 0, "one point is not a lane");
        Check.Equal(0, LaneHarness.TestLane().Validate().Count, "the test lane is fine");
    }

    [Test]
    static void RulesetIsValidAndCaughtWhenBroken()
    {
        var r = new LaneRuleset();
        Check.Equal(0, r.Validate().Count, "default numbers: " + string.Join("; ", r.Validate()));
        var bad = r.Clone();
        bad.waveSeconds = 0f;
        bad.camps = bad.camps.Where(c => c.kind != CampKind.Speed).ToArray();
        bad.units[0].attackSeconds = 0f;
        var e = bad.Validate();
        Check.True(e.Count >= 3, "broken numbers are caught: " + string.Join("; ", e));
        Check.Equal(0, r.Validate().Count, "the clone didn't change the original");
        Check.Equal(3, r.RowAt(0f).scrappers, "a wave is three Scrappers");
        Check.Equal(1, r.RowAt(14f).wardens, "and one Warden, all match long");
        r.waveRows = new[] { new WaveRow(0f, 3, 1), new WaveRow(8f, 4, 1) };
        Check.Equal(3, r.RowAt(7.9f).scrappers, "row by minute: before");
        Check.Equal(4, r.RowAt(8f).scrappers, "row by minute: after");
    }

    // ---- Waves ----

    [Test]
    static void WavesLeaveOnTheTimerFromBothEnds()
    {
        var h = new LaneHarness();
        Check.Equal(1, h.L.State.wavesA, "team A's first wave left when play began");
        Check.Equal(1, h.L.State.wavesB, "and team B's");
        Check.Equal(4, h.Minions(TeamId.A).Count(), "three Scrappers and a Warden");
        Check.Equal(4, h.Minions(TeamId.B).Count(), "on both sides");
        Check.True(h.Minions(TeamId.A).All(u => u.position.z < -LaneHarness.HalfLength + 0.5f), "team A's wave at its end");
        Check.True(h.Minions(TeamId.B).All(u => u.position.z > LaneHarness.HalfLength - 0.5f), "team B's wave at its end");
        Check.Equal(1, h.Minions(TeamId.A).Count(u => u.typeId == LaneRuleset.Warden), "one Warden");
        var xs = h.Minions(TeamId.A).Select(u => u.position.x).OrderBy(x => x).ToList();
        Check.True(xs.Zip(xs.Skip(1), (a, b) => b - a).All(d => d > 1f), "side by side, not stacked");
        h.H.Forget();
        h.Run(29.7f);
        Check.Equal(0, h.H.Of<WaveSpawnedEvent>().Count, "nothing more before 30 s");
        h.Run(0.3f);
        Check.Equal(2, h.H.Of<WaveSpawnedEvent>().Count, "the next waves at 30 s");
        Check.True(h.H.Of<WaveSpawnedEvent>().All(w => w.wave == 2 && w.unitIds.Length == 4), "numbered, four minions each");
    }

    [Test]
    static void UnopposedMinionWalksTheLaneAndStopsAtTheFarEnd()
    {
        var h = new LaneHarness(waves: false);
        int id = h.L.Waves.SpawnMinion(LaneRuleset.Scrapper, TeamId.A, 0);
        var u = h.H.Combat.Units.Unit(id);
        h.Run(10f);
        Check.Near(-LaneHarness.HalfLength + 40f, u.position.z, "4 m a second", 0.6f);
        Check.Near(0f, u.position.x, "on the line", 0.01f);
        h.Run(60f);
        Check.Near(LaneHarness.HalfLength - h.L.Rules.laneEndStop, u.position.z, "stops short of the far end", 0.5f);
        float z = u.position.z;
        h.Run(5f);
        Check.Near(z, u.position.z, "and waits there", 0.01f);

        h.H.Combat.Units.Remove(id);
        int b = h.L.Waves.SpawnMinion(LaneRuleset.Scrapper, TeamId.B, 0);
        var bu = h.H.Combat.Units.Unit(b);
        h.Run(5f);
        Check.Near(LaneHarness.HalfLength - 20f, bu.position.z, "team B walks the other way", 0.6f);
    }

    [Test]
    static void MirroredWavesMeetAndStallInTheMiddle()
    {
        var h = new LaneHarness();
        var deaths = new List<float>();
        float furthestA = -LaneHarness.HalfLength, furthestB = LaneHarness.HalfLength;
        int most = 0;
        h.H.Events.Subscribe(e => { if (e is UnitDestroyedEvent d) deaths.Add(d.position.z); });
        h.Run(600f, () =>
        {
            foreach (var u in h.Minions(TeamId.A)) furthestA = Math.Max(furthestA, u.position.z);
            foreach (var u in h.Minions(TeamId.B)) furthestB = Math.Min(furthestB, u.position.z);
            most = Math.Max(most, h.L.State.minions.Count);
        });
        Check.Equal(21, h.L.State.wavesA, "21 waves in 10 minutes (at 0, 30 s, ... 600 s)");
        Check.True(deaths.Count >= 120, "the waves fought: " + deaths.Count + " minions destroyed");
        Check.True(deaths.All(z => Math.Abs(z) < 25f), "every minion fell in the middle quarter of the lane: " + deaths.Min() + " .. " + deaths.Max());
        Check.True(furthestA < 25f && furthestB > -25f, "neither side got past the middle: A " + furthestA + ", B " + furthestB);
        Check.Near(-furthestB, furthestA, "mirrored", 0.5f);
        Check.True(most <= 2 * h.L.Rules.maxMinionsPerTeam, "within the unit budget: " + most);
        Check.Equal(h.Minions(TeamId.A).Count(), h.Minions(TeamId.B).Count(), "even at the end");
    }

    [Test]
    static void TheUnitCapHoldsBackNewMinions()
    {
        var r = CombatHarness.QuickRules();
        r.lanes.maxMinionsPerTeam = 6;
        var h = new LaneHarness(rules: r, waves: false);
        h.L.Waves.SpawnWaves();
        h.L.Waves.SpawnWaves();
        Check.Equal(6, h.Minions(TeamId.A).Count(), "a second wave only fills up to the cap");
        h.L.Waves.QueueJuggernaut(TeamId.A);
        h.L.Waves.SpawnWaves();
        Check.Equal(1, h.Minions(TeamId.A).Count(u => u.typeId == LaneRuleset.Juggernaut), "a Juggernaut comes whatever the cap");
    }

    // ---- Minions fighting ----

    [Test]
    static void MinionsShootHeroesNearTheLaneButNeverLeaveIt()
    {
        var h = new LaneHarness(waves: false);
        h.H.NoShield(CombatHarness.B1);
        h.H.Place(CombatHarness.B1, 12f, -60f); // 12 m off the line: in sight (14), inside leash (8) + range (10)
        int id = h.L.Waves.SpawnMinion(LaneRuleset.Scrapper, TeamId.A, 0);
        var u = h.H.Combat.Units.Unit(id);
        float worstOff = 0f;
        h.Run(20f, () => { worstOff = Math.Max(worstOff, Math.Abs(u.position.x)); h.H.Place(CombatHarness.B1, 12f, -60f); });
        var shots = h.H.Of<DamagedEvent>().Where(d => d.attackerId == id && d.targetId == CombatHarness.B1).ToList();
        Check.True(shots.Count >= 5, "it stopped to shoot the hero: " + shots.Count);
        Check.True(shots.All(s => s.abilityId == LaneRuleset.AttackId), "as a minion attack");
        Check.True(worstOff <= 8.01f, "never more than the leash from the line: " + worstOff);
        Check.True(u.position.z < -55f, "and didn't walk on while it had a target");

        h.H.Place(CombatHarness.B1, 40f, -60f);
        h.H.Forget();
        h.Run(5f, () => h.H.Place(CombatHarness.B1, 40f, -60f));
        Check.Equal(0, h.H.Of<DamagedEvent>().Count(d => d.attackerId == id), "a hero far from the lane is left alone");
        Check.True(u.position.z > -50f && Math.Abs(u.position.x) < 1f, "it went back to the lane and walked on: " + u.position);
    }

    [Test]
    static void MinionsIgnoreNeutralUnitsAndFriends()
    {
        var h = new LaneHarness(waves: false);
        int neutral = h.H.Combat.Units.Spawn(LaneRuleset.CampGuard, TeamId.None, new Vec3(3f, 0f, -90f));
        h.H.Place(CombatHarness.A1, 3f, -85f);
        int id = h.L.Waves.SpawnMinion(LaneRuleset.Scrapper, TeamId.A, 0);
        h.Run(4f, () => h.H.Place(CombatHarness.A1, 3f, -85f));
        Check.Equal(0, h.H.Of<DamagedEvent>().Count(d => d.attackerId == id), "no shots at a neutral guard or a teammate");
        Check.True(h.H.Combat.Units.Unit(id).position.z > -86f, "it walked past");
        Check.True(h.H.Combat.Units.IsAlive(neutral), "the guard is untouched");
    }

    [Test]
    static void WallsBlockMinionShots()
    {
        var h = new LaneHarness(waves: false);
        h.H.Map.Wall(new Vec3(6f, 1.5f, -70f), new Vec3(1f, 3f, 30f));
        h.H.Place(CombatHarness.B1, 9f, -75f);
        int id = h.L.Waves.SpawnMinion(LaneRuleset.Scrapper, TeamId.A, 0);
        h.Run(8f, () => h.H.Place(CombatHarness.B1, 9f, -75f));
        Check.Equal(0, h.H.Of<DamagedEvent>().Count(d => d.attackerId == id), "the wall stops every shot");
        var u = h.H.Combat.Units.Unit(id);
        Check.True(u.position.x < 5.6f, "and the minion doesn't walk through it: " + u.position);
    }

    [Test]
    static void HeroesKillMinionsAndShotsLandTogether()
    {
        var h = new LaneHarness(waves: false);
        int a = h.L.Waves.SpawnMinion(LaneRuleset.Scrapper, TeamId.A, 0);
        int b = h.L.Waves.SpawnMinion(LaneRuleset.Scrapper, TeamId.B, 0);
        h.Run(45f);
        Check.False(h.H.Combat.Units.IsAlive(a) || h.H.Combat.Units.IsAlive(b), "two lone Scrappers destroy each other");
        Check.True(h.H.Of<UnitDestroyedEvent>().Select(d => d.tick).Distinct().Count() == 1, "on the same tick: shots land together");
        Check.True(h.L.State.minions.Count == 0, "and leave the wave list");

        int c = h.L.Waves.SpawnMinion(LaneRuleset.Scrapper, TeamId.B, 0);
        h.H.Combat.Damage.Damage(CombatHarness.A1, c, 1000f, "test");
        Check.False(h.H.Combat.Units.IsAlive(c), "heroes kill minions as they do any unit");
    }

    [Test]
    static void WardensShieldNearbyScrappers()
    {
        var h = new LaneHarness(waves: false);
        h.L.Waves.SpawnWaves();
        var scrappers = h.L.State.minions.Where(m => m.typeId == LaneRuleset.Scrapper && m.team == TeamId.A).Select(m => m.unitId).ToList();
        h.Run(h.L.Rules.wardenPulseSeconds + 0.1f);
        var shields = h.H.Of<ShieldedEvent>().Where(s => s.abilityId == LaneRuleset.WardenShieldId).ToList();
        Check.Equal(6, shields.Count, "both Wardens shield their three Scrappers");
        foreach (int s in scrappers) Check.Near(h.L.Rules.wardenShield, h.H.Shield(s), "a light shield");
        h.Run(h.L.Rules.wardenShieldSeconds - 0.3f);
        foreach (int s in scrappers) Check.True(h.H.Shield(s) > 0f, "still there before the next pulse");
    }

    // ---- Camps ----

    static void ClearGuards(LaneHarness h, string campId, int by)
    {
        foreach (int g in h.L.State.Camp(campId).guardIds) h.H.Combat.Damage.Damage(by, g, 5000f, "test");
        h.Tick();
    }

    static void Hold(LaneHarness h, string campId, float seconds, params int[] heroes)
    {
        var d = h.L.Camps.Def(campId);
        h.Run(seconds, () => { for (int i = 0; i < heroes.Length; i++) h.H.Place(heroes[i], d.pad.x + i * 0.5f, d.pad.z); });
    }

    [Test]
    static void CampGuardsDefendAndThePadIsTakenLikeAPoint()
    {
        var camps = new List<CampDef> { Camp("exp_a", CampKind.Experience, 60f, -40f, TeamId.A) };
        var h = new LaneHarness(camps, waves: false);
        var c = h.L.State.Camp("exp_a");
        Check.Equal(2, c.guardIds.Length, "two guards placed around the pad");
        Check.True(c.guardIds.All(g => h.H.Combat.Damage.TeamOf(g) == TeamId.None), "neutral");

        h.H.NoShield(CombatHarness.A1);
        float before = h.H.Health(CombatHarness.A1);
        Hold(h, "exp_a", 2f, CombatHarness.A1);
        Check.True(h.H.Health(CombatHarness.A1) < before, "the guards shoot a hero who walks up");
        Check.Equal(CampPhase.Guarded, c.phase, "standing on the pad does nothing while guards stand");

        ClearGuards(h, "exp_a", CombatHarness.A1);
        Check.Equal(CampPhase.Open, c.phase, "open once the guards are down");
        Check.Equal(1, h.H.Of<CampOpenedEvent>().Count, "announced");

        h.Park();
        Hold(h, "exp_a", 3f, CombatHarness.A1);
        Check.Near(0.6f, c.progress, "3 of 5 seconds", 0.03f);
        Hold(h, "exp_a", 1f, CombatHarness.A1, CombatHarness.B1);
        Check.True(c.contested, "an enemy on the pad freezes it");
        Check.Near(0.6f, c.progress, "frozen", 0.03f);
        h.Park();
        h.Run(1f);
        Check.Near(0.4f, c.progress, "an empty pad drains", 0.03f);
        Hold(h, "exp_a", 0.5f, CombatHarness.B1);
        Check.Equal(TeamId.A, c.progressTeam, "the other team drains it before it starts its own");
        h.Park();
        Hold(h, "exp_a", 4f, CombatHarness.A1);
        Check.Equal(CampPhase.Resting, c.phase, "taken");
        var taken = h.H.Last<CampTakenEvent>();
        Check.True(taken != null && taken.team == TeamId.A && taken.respawnSeconds == 120f, "by team A, back in 2 minutes");

        h.Park();
        h.Run(119f);
        Check.Equal(CampPhase.Resting, c.phase, "resting");
        h.Run(1.1f);
        Check.Equal(CampPhase.Guarded, c.phase, "guards back after 2 minutes");
        Check.Equal(2, c.guardIds.Count(g => h.H.Combat.Units.IsAlive(g)), "two fresh guards");
    }

    [Test]
    static void JuggernautCampSendsAJuggernautWithTheNextWave()
    {
        var camps = new List<CampDef> { Camp("jugg", CampKind.Juggernaut, 40f, 0f) };
        var h = new LaneHarness(camps, waves: false);
        Check.Equal(2, h.L.State.Camp("jugg").guardIds.Length, "two brutes");
        Check.True(h.L.State.Camp("jugg").guardIds.All(g => h.H.Combat.Units.Unit(g).typeId == LaneRuleset.CampBrute), "the toughest guards");
        ClearGuards(h, "jugg", CombatHarness.B1);
        Hold(h, "jugg", 5.1f, CombatHarness.B1);
        Check.True(h.L.State.juggernautB, "team B's next wave brings a Juggernaut");
        Check.False(h.L.State.juggernautA, "not team A's");
        h.H.Forget();
        h.L.Waves.SpawnWaves();
        var waves = h.H.Of<WaveSpawnedEvent>();
        Check.True(waves.Single(w => w.team == TeamId.B).juggernaut, "it walks with the wave");
        Check.Equal(5, waves.Single(w => w.team == TeamId.B).unitIds.Length, "four minions and the Juggernaut");
        Check.False(h.L.State.juggernautB, "only once");
        var jugg = h.Minions(TeamId.B).Single(u => u.typeId == LaneRuleset.Juggernaut);
        Check.Near(1200f, h.H.Health(jugg.id), "much stronger than a Scrapper");
        Check.Near(240f, h.L.State.Camp("jugg").respawnIn, "back after 4 minutes", 0.1f);

        // The Juggernaut tips an even lane: team B pushes past the middle.
        h.Run(150f);
        float deepest = h.Minions(TeamId.B).Select(u => u.position.z).DefaultIfEmpty(LaneHarness.HalfLength).Min();
        Check.True(deepest < -25f || h.H.Of<UnitDestroyedEvent>().Count(d => d.team == TeamId.A) > h.H.Of<UnitDestroyedEvent>().Count(d => d.team == TeamId.B),
            "the Juggernaut's side wins the lane: deepest B minion at " + deepest);
    }

    [Test]
    static void DamageCampBoostsTheWholeTeamForAWhile()
    {
        var camps = new List<CampDef> { Camp("dmg_b", CampKind.Damage, 60f, 40f, TeamId.B) };
        var h = new LaneHarness(camps, waves: false);
        Check.Equal(3, h.L.State.Camp("dmg_b").guardIds.Length, "three guards");
        ClearGuards(h, "dmg_b", CombatHarness.A1);
        Hold(h, "dmg_b", 5.1f, CombatHarness.A1); // team A steals team B's camp
        Check.Near(1.1f, h.H.Combat.Damage.Stat(CombatHarness.A1, StatIds.DamageDealt), "+10% damage for the taker");
        Check.Near(1.1f, h.H.Combat.Damage.Stat(CombatHarness.A2, StatIds.DamageDealt), "and every teammate, wherever they are");
        Check.Near(1f, h.H.Combat.Damage.Stat(CombatHarness.B1, StatIds.DamageDealt), "not the other team");
        Check.Equal(2, h.L.State.boosts.Count, "two boosts");

        int target = h.H.Combat.Units.Spawn(UnitLibrary.TrainingDrone, TeamId.B, new Vec3(200f, 0f, 0f));
        Check.Near(11f, h.H.Combat.Damage.Damage(CombatHarness.A2, target, 10f, "test"), "hits harder");

        h.Run(89.5f);
        Check.Near(1.1f, h.H.Combat.Damage.Stat(CombatHarness.A2, StatIds.DamageDealt), "lasts 90 s");
        h.Run(0.6f);
        Check.Near(1f, h.H.Combat.Damage.Stat(CombatHarness.A2, StatIds.DamageDealt), "then ends");
        Check.Equal(2, h.H.Of<CampBoostEndedEvent>().Count, "announced");
        Check.Equal(0, h.L.State.boosts.Count, "gone");
    }

    [Test]
    static void ExperienceCampSplitsBetweenHelpers()
    {
        var camps = new List<CampDef> { Camp("exp_a", CampKind.Experience, 60f, -40f, TeamId.A) };
        var h = new LaneHarness(camps, waves: false, progression: true);
        var c = h.L.State.Camp("exp_a");
        h.H.Combat.Damage.Damage(CombatHarness.A2, c.guardIds[0], 5000f, "test"); // A2 helps from range
        h.H.Combat.Damage.Damage(CombatHarness.A1, c.guardIds[1], 5000f, "test");
        h.Tick();
        h.H.Forget();
        Hold(h, "exp_a", 5.1f, CombatHarness.A1);
        var paid = h.H.Of<ExperienceAwardedEvent>().Where(e => e.source == ExperienceSource.Camp).ToList();
        Check.Equal(2, paid.Count, "both helpers paid");
        Check.True(paid.Select(p => p.playerId).OrderBy(i => i).SequenceEqual(new[] { CombatHarness.A1, CombatHarness.A2 }), "A1 and A2");
        Check.Near(paid[0].amount, paid[1].amount, "an even split");
        Check.True(paid.All(p => p.amount >= 70f), "about 75 each: " + paid[0].amount);
        Check.True(h.H.Last<CampTakenEvent>().heroes.SequenceEqual(new[] { CombatHarness.A1, CombatHarness.A2 }), "named in the event");
        Check.Equal(0, h.L.State.boosts.Count, "no boost from an experience camp");
    }

    [Test]
    static void SpeedCampSpeedsUpOnlyTheHeroesOnThePad()
    {
        var camps = new List<CampDef> { Camp("spd_a", CampKind.Speed, 60f, -70f, TeamId.A) };
        var h = new LaneHarness(camps, waves: false);
        Check.Equal(1, h.L.State.Camp("spd_a").guardIds.Length, "one guard: a lone hero can clear it");
        ClearGuards(h, "spd_a", CombatHarness.A1);
        float top = h.H.Combat.Movement.TopSpeed(CombatHarness.A1);
        Hold(h, "spd_a", 5.1f, CombatHarness.A1);
        Check.Near(1.15f, h.H.Combat.Damage.Stat(CombatHarness.A1, StatIds.MoveSpeed), "+15% speed on the pad");
        Check.Near(top * 1.15f, h.H.Combat.Movement.TopSpeed(CombatHarness.A1), "the movement check allows it");
        Check.Near(1f, h.H.Combat.Damage.Stat(CombatHarness.A2, StatIds.MoveSpeed), "not for a teammate elsewhere");
        h.Run(60.1f);
        Check.Near(1f, h.H.Combat.Damage.Stat(CombatHarness.A1, StatIds.MoveSpeed), "60 s later it ends");
        Check.Near(90f - 60.1f, h.L.State.Camp("spd_a").respawnIn, "back after 90 s", 0.1f);
    }

    // ---- Clients ----

    [Test]
    static void SnapshotCarriesWavesAndCamps()
    {
        var camps = new List<CampDef> { Camp("spd_a", CampKind.Speed, 60f, -70f, TeamId.A) };
        var h = new LaneHarness(camps);
        h.Run(1f);
        ClearGuards(h, "spd_a", CombatHarness.A1);
        Hold(h, "spd_a", 5.1f, CombatHarness.A1);
        var snap = h.H.Match.TakeSnapshot();
        h.H.Combat.FillSnapshot(snap);
        h.L.FillSnapshot(snap);
        var copy = ((SnapshotMessage)WireCodec.Decode(WireCodec.Encode(new SnapshotMessage { snapshot = snap }))).snapshot;
        Check.Equal(1, copy.lanes.wavesA, "wave count");
        Check.Near(30f - 6.1f, copy.lanes.nextWaveIn, "next wave timer", 0.1f);
        Check.Equal(1, copy.lanes.camps.Count, "the camp");
        Check.Equal(CampPhase.Resting, copy.lanes.camps[0].phase, "resting");
        Check.Equal(TeamId.A, copy.lanes.camps[0].takenBy, "taken by A");
        Check.Equal(1, copy.lanes.boosts.Count, "A1's boost");
        Check.Equal(8, copy.units.Count(u => u.typeId == LaneRuleset.Scrapper || u.typeId == LaneRuleset.Warden), "minion bodies ride in units");
    }

    // ---- The cloud wave report ----

    /// <summary>
    /// dotnet run --project Tests/CoreSim -- report waves [minutes]: waves on the 200 m test lane with
    /// no heroes, every 30 s: minions alive, how far each side has got, and where minions fell.
    /// </summary>
    public static int Report(string[] args)
    {
        float minutes = args.Length > 0 && float.TryParse(args[0], out var m) ? m : 10f;
        var h = new LaneHarness();
        var deaths = new List<float>();
        h.H.Events.Subscribe(e => { if (e is UnitDestroyedEvent d) deaths.Add(d.position.z); });
        Console.WriteLine("Waves on a straight " + (2 * LaneHarness.HalfLength) + " m test lane, no heroes. Team A walks from z = -100, team B from z = +100.");
        Console.WriteLine("Wave: " + h.L.Rules.RowAt(0f).scrappers + " Scrappers + " + h.L.Rules.RowAt(0f).wardens + " Warden every " + h.L.Rules.waveSeconds + " s.");
        Console.WriteLine();
        Console.WriteLine("  time   alive A  alive B   front A   front B   fell (this 30 s)   where they fell (z)");
        int most = 0;
        float frontA = -LaneHarness.HalfLength, frontB = LaneHarness.HalfLength, worstA = -LaneHarness.HalfLength, worstB = LaneHarness.HalfLength;
        int steps = (int)(minutes * 2);
        for (int s = 1; s <= steps; s++)
        {
            int before = deaths.Count;
            frontA = -LaneHarness.HalfLength; frontB = LaneHarness.HalfLength;
            h.Run(30f, () =>
            {
                most = Math.Max(most, h.L.State.minions.Count);
                foreach (var u in h.Minions(TeamId.A)) { frontA = Math.Max(frontA, u.position.z); worstA = Math.Max(worstA, u.position.z); }
                foreach (var u in h.Minions(TeamId.B)) { frontB = Math.Min(frontB, u.position.z); worstB = Math.Min(worstB, u.position.z); }
            });
            var fell = deaths.Skip(before).ToList();
            string where = fell.Count == 0 ? "-" : fell.Min().ToString("0.0") + " .. " + fell.Max().ToString("0.0");
            Console.WriteLine($"  {s * 0.5f,4:0.0}m  {h.Minions(TeamId.A).Count(),7}  {h.Minions(TeamId.B).Count(),7}   {frontA,7:0.0}   {frontB,7:0.0}   {fell.Count,16}   {where}");
        }
        Console.WriteLine();
        bool stalled = deaths.Count > 0 && deaths.All(z => Math.Abs(z) < 25f) && worstA < 25f && worstB > -25f;
        Console.WriteLine("Most minions alive at once: " + most + " (budget " + 2 * h.L.Rules.maxMinionsPerTeam + ").");
        Console.WriteLine("Furthest any minion got: A to z = " + worstA.ToString("0.0") + ", B to z = " + worstB.ToString("0.0") + ".");
        Console.WriteLine(stalled ? "STALLED IN THE MIDDLE: every minion fell within 25 m of the centre." : "NOT STALLED: a wave got through.");
        return stalled ? 0 : 1;
    }
}
