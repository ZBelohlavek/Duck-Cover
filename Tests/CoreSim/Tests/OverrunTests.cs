using System;
using System.Collections.Generic;
using System.Linq;
using DuckAndCover.Core;
using static OverrunHarness;

/// <summary>WP16: Overrun's rules. Bastions, who may hurt them, their shields and fire, the late push, the result.</summary>
static class OverrunTests
{
    static BastionState FwdA(OverrunHarness h) => h.Bastion(TeamId.A, BastionTier.Forward);
    static BastionState FwdB(OverrunHarness h) => h.Bastion(TeamId.B, BastionTier.Forward);
    static BastionState CoreB(OverrunHarness h) => h.Bastion(TeamId.B, BastionTier.Core);

    [Test]
    static void BastionsStandOnBreakwatersSpots()
    {
        var h = new OverrunHarness();
        var o = h.Overrun.Rules;
        Check.Equal(4, h.Overrun.State.bastions.Count, "four Bastions");
        foreach (var spot in h.Breakwater.bastions)
        {
            var b = h.Bastion(spot.team, spot.tier);
            var u = h.Units.Unit(b.unitId);
            Check.True(u != null && u.alive && u.team == spot.team, spot.team + " " + spot.tier + " is a living unit of its team");
            Check.True(Vec3.Approximately(spot.position, u.position), "at the map's spot");
            Check.True(u.anchored, "anchored");
            Check.Near(o.Kind(spot.tier).maxHealth, b.health, "full health");
            Check.Near(o.Shield(spot.tier), b.shield, "full shield");
            Check.Equal(spot.tier == BastionTier.Core, b.sealedOff, "only the Core is sealed at the start");
        }
        Check.True(h.S.zones.Count == 0, "no zones in Overrun");
        Check.Equal(0, ModeDef.Overrun().Validate(h.Breakwater.ToMatchMap()).Count, "Overrun fits Breakwater");
        var bad = ModeDef.Overrun(); bad.zoneSet = "bases";
        Check.True(bad.Validate(null).Count > 0, "Overrun with a zone set is refused");
    }

    [Test]
    static void HeroesHurtBastionsOnlyNearTheirOwnMinions()
    {
        var h = new OverrunHarness();
        var b = FwdB(h);
        h.Place(A1, b.position.x, b.position.z - 10f);
        float before = h.Total(b);
        for (int i = 0; i < 20; i++) { h.FireAt(A1, h.ChestOf(b.unitId)); h.Tick(); }
        Check.Near(before, h.Total(b), "no own minion near: hero shots do nothing");
        Check.False(b.openToHeroes, "and the Bastion shows it");
        Check.False(h.Overrun.CanDamage(A1, b.unitId), "the gate says no");

        // An enemy minion near doesn't count; one of A's own does.
        int theirs = h.Scrapper(TeamId.B, b.position.x + 3f, b.position.z - 3f);
        h.Tick();
        Check.False(h.Overrun.CanDamage(A1, b.unitId), "the Bastion's own team's minion doesn't open it");
        h.Units.Remove(theirs);
        int far = h.Scrapper(TeamId.A, b.position.x, b.position.z - 14f);
        h.Tick();
        Check.False(h.Overrun.CanDamage(A1, b.unitId), "an own minion 14 m off is too far");
        h.Units.Remove(far);
        h.Scrapper(TeamId.A, b.position.x + 2f, b.position.z - 11f);
        h.Tick();
        Check.True(b.openToHeroes && h.Overrun.CanDamage(A1, b.unitId), "an own minion within 12 m opens it to heroes");
        Check.False(h.Overrun.CanDamage(B1, b.unitId), "never to its own team");
        Check.False(h.Overrun.CanDamage(-1, b.unitId), "nor to the world");

        float health = h.Combat.Damage.Unit(b.unitId).health;
        before = h.Total(b);
        h.Combat.Damage.Damage(A1, b.unitId, 100f);
        Check.Near(before - 100f, h.Total(b), "hero damage lands");
        Check.Near(health, h.Combat.Damage.Unit(b.unitId).health, "on the shield first");
    }

    [Test]
    static void MinionsAlwaysHurtBastionsAndNothingRestoresThem()
    {
        var h = new OverrunHarness();
        var b = FwdB(h);
        int far = h.Scrapper(TeamId.A, 300f, 0f); // nowhere near: minion damage needs no escort
        float shield = b.shield;
        h.Combat.Damage.Damage(far, b.unitId, shield + 50f);
        h.Tick();
        var c = h.Combat.Damage.Unit(b.unitId);
        Check.Near(0f, b.shield, "shield gone");
        Check.Near(h.Overrun.Rules.Kind(BastionTier.Forward).maxHealth - 50f, c.health, "the rest on health");
        Check.Equal(1, h.Of<BastionShieldBrokenEvent>().Count, "shield broken event");

        int guard = h.Units.Spawn(LaneRuleset.CampGuard, TeamId.None, new Vec3(300f, 0f, 5f));
        Check.Near(0f, h.Combat.Damage.Damage(guard, b.unitId, 50f), "a camp guard isn't a minion");
        Check.Near(0f, h.Combat.Damage.Heal(B1, b.unitId, 500f), "no healing");
        Check.Near(0f, h.Combat.Damage.AddShield(B1, b.unitId, 100f, 5f), "no shields from outside");
        Check.True(h.Combat.Damage.Heal(B2, B1, 1f) >= 0f && h.Combat.Damage.AddShield(B2, B1, 10f, 5f) > 0f, "heroes still shield heroes");
    }

    [Test]
    static void ShieldRefillsAfterTenQuietSecondsAndHealthNever()
    {
        var h = new OverrunHarness();
        var b = FwdB(h);
        int near = h.Scrapper(TeamId.A, b.position.x, b.position.z - 8f);
        h.Combat.Damage.Damage(near, b.unitId, b.maxShield + 300f);
        // The Bastion shoots it; keep one standing there.
        h.Run(15f, () => { if (!h.Units.IsAlive(near)) near = h.Scrapper(TeamId.A, b.position.x, b.position.z - 8f); });
        Check.Near(0f, b.shield, "no refill while an enemy minion is near");
        h.Units.Remove(near);
        h.Run(9.5f);
        Check.Near(0f, b.shield, "not yet after 9.5 s");
        h.Run(1f);
        Check.Near(b.maxShield, b.shield, "full again after 10 s");
        Check.Equal(1, h.Of<BastionShieldRestoredEvent>().Count, "restored event");
        Check.Near(b.maxHealth - 300f, b.health, "health never comes back");
    }

    [Test]
    static void CoreIsSealedUntilItsForwardFallsAndThenTheGateOpens()
    {
        var h = new OverrunHarness();
        var core = CoreB(h);
        int m = h.Scrapper(TeamId.A, core.position.x, core.position.z - 5f);
        Check.Near(0f, h.Combat.Damage.Damage(m, core.unitId, 100f), "a sealed Core takes nothing, even from minions");
        Check.False(h.Overrun.State.gateOpenB, "gate shut");
        float waitBefore = h.Match.Spawn.WaitFor(TeamId.B, 1);

        h.Destroy(FwdB(h));
        Check.False(FwdB(h).alive, "Forward down");
        Check.False(h.Units.IsAlive(FwdB(h).unitId), "its unit too");
        Check.Equal(1, h.S.scoreA, "one point to A");
        Check.True(h.S.phase == MatchPhase.Play, "the match goes on");
        Check.True(h.Overrun.State.gateOpenB && !h.Overrun.State.gateOpenA, "B's side gate open, A's not");
        Check.Equal(1, h.Of<SideGateOpenedEvent>().Count(e => e.team == TeamId.B), "gate event");
        var down = h.Of<BastionDestroyedEvent>().Single();
        Check.True(down.team == TeamId.B && down.tier == BastionTier.Forward && down.byTeam == TeamId.A, "destroyed event");
        Check.False(core.sealedOff, "the Core can be hurt now");
        Check.Near(waitBefore - 2f, h.Match.Spawn.WaitFor(TeamId.B, 1), "B respawns 2 s sooner");
        Check.Near(h.Match.Spawn.WaitFor(1), h.Match.Spawn.WaitFor(TeamId.A, 1), "A's wait unchanged");
        h.Match.EliminatePlayer(B1, A1);
        Check.Near(h.S.elapsed + waitBefore - 2f, h.S.Player(B1).respawnAt, "a defender who goes down waits less");

        Check.True(h.Combat.Damage.Damage(m, core.unitId, 100f) > 0f, "minions hurt the Core now");
    }

    [Test]
    static void DestroyingTheCoreWins()
    {
        var h = new OverrunHarness();
        h.Destroy(FwdB(h));
        h.Destroy(CoreB(h));
        Check.Equal(MatchPhase.MatchEnd, h.S.phase, "match over");
        Check.Equal(TeamId.A, h.S.winner, "A wins");
        Check.Equal(MatchEndReason.ScoreReached, h.S.endReason, "by the Core");
        Check.Equal(2, h.S.scoreA, "two Bastions");
    }

    [Test]
    static void BastionsShootMinionsFirstThenHeroesAndGrudgesFirstOfAll()
    {
        var h = new OverrunHarness();
        var b = FwdA(h); // team A's: B's heroes and minions are its enemies
        var kind = h.Overrun.Rules.Kind(BastionTier.Forward);
        h.Place(B1, b.position.x + 3f, b.position.z + 10f);
        int minion = h.Scrapper(TeamId.B, b.position.x, b.position.z + 20f);
        h.Tick();
        Check.Equal(minion, b.targetId, "a minion before a closer hero");
        h.Forget();
        h.Run(1.1f);
        Check.True(h.Of<DamagedEvent>().Any(e => e.attackerId == b.unitId && e.targetId == minion && e.abilityId == OverrunRuleset.AttackId), "it shoots");
        Check.True(h.Of<DamagedEvent>().Where(e => e.attackerId == b.unitId).All(e => Math.Abs(e.amount - kind.attackDamage) < 1e-3f), "for its damage");

        // B1 hurts an A hero inside the Bastion's range: B1 is its target now, over the minion.
        h.Place(A1, b.position.x - 3f, b.position.z + 5f);
        h.Combat.Damage.Damage(B1, A1, 10f);
        h.Tick();
        Check.Equal(B1, b.targetId, "the hero who hit an ally in range draws its fire");
        h.Run(h.Overrun.Rules.aggroSeconds + 0.2f);
        Check.Equal(minion, b.targetId, "the grudge passes");

        h.Units.Remove(minion);
        h.Tick();
        Check.Equal(B1, b.targetId, "with no minions it shoots heroes");
        h.Place(B1, b.position.x, b.position.z + kind.attackRange + 1f);
        h.Tick();
        Check.Equal(-1, b.targetId, "out of range");
    }

    [Test]
    static void LatePushFromMinuteElevenSpeedsWavesAndHurtsBastionsMore()
    {
        var r = QuickRules();
        r.overrun.latePushMinute = 1f;
        var h = new OverrunHarness(r, waves: true);
        Check.Near(30f, h.Lanes.Rules.waveSeconds, "30 s waves before");
        h.Run(61f);
        Check.True(h.Overrun.State.latePush, "late push on");
        Check.Equal(1, h.Of<LatePushStartedEvent>().Count, "announced once");
        Check.Near(20f, h.Lanes.Rules.waveSeconds, "20 s waves");
        Check.True(h.Lanes.State.nextWaveIn <= 20f + 1e-3f, "the next wave no further off than that");
        var b = CoreB(h); // far from any fighting at the far end
        int m = h.Scrapper(TeamId.A, 300f, 0f);
        h.Destroy(FwdB(h));
        float shield = h.Combat.Damage.Unit(b.unitId).Shield;
        Check.Near(150f, h.Combat.Damage.Damage(m, b.unitId, 100f), "+50% damage on Bastions");
        Check.Near(shield - 150f, h.Combat.Damage.Unit(b.unitId).Shield, "taken off the shield");
    }

    [Test]
    static void TimeCapGoesToMoreBastionsThenToTheLargerShareOfHealth()
    {
        // More Bastions destroyed.
        var r = QuickRules();
        r.lanes.firstWaveSeconds = 100000f;
        var h = new OverrunHarness(r);
        h.Destroy(FwdB(h));
        int m = h.Scrapper(TeamId.B, 300f, 0f);
        h.Combat.Damage.Damage(m, FwdA(h).unitId, FwdA(h).maxShield + FwdA(h).maxHealth - 200f); // B hurt A's Forward a lot, but destroyed none
        h.Run(ModeDef.Overrun().timeCapSeconds + 1f);
        Check.Equal(TeamId.A, h.S.winner, "the team with more Bastions destroyed wins at the cap");
        Check.Equal(MatchEndReason.TimeCap, h.S.endReason, "by the time cap");

        // Level on Bastions: the larger share of enemy Bastion health.
        h = new OverrunHarness();
        int a = h.Scrapper(TeamId.A, 300f, 0f), b = h.Scrapper(TeamId.B, 310f, 0f);
        h.Combat.Damage.Damage(a, FwdB(h).unitId, FwdB(h).maxShield + 400f);
        h.Combat.Damage.Damage(b, FwdA(h).unitId, FwdA(h).maxShield + 300f);
        h.Run(ModeDef.Overrun().timeCapSeconds + 1f);
        Check.Equal(TeamId.A, h.S.winner, "level on Bastions: A took more health");
        Check.Equal(MatchEndReason.BastionDamage, h.S.endReason, "by Bastion damage");

        // Exactly level: play on until it isn't.
        h = new OverrunHarness();
        h.Run(ModeDef.Overrun().timeCapSeconds + 1f);
        Check.Equal(MatchPhase.Overtime, h.S.phase, "no draws: sudden death");
        a = h.Scrapper(TeamId.A, 300f, 0f);
        h.Combat.Damage.Damage(a, FwdB(h).unitId, FwdB(h).maxShield + 10f);
        h.Tick();
        Check.Equal(MatchPhase.MatchEnd, h.S.phase, "the first health taken settles it");
        Check.Equal(TeamId.A, h.S.winner, "A");
        Check.Equal(MatchEndReason.BastionDamage, h.S.endReason, "by Bastion damage");
    }

    [Test]
    static void PushesDontMoveBastions()
    {
        var h = new OverrunHarness();
        var b = FwdB(h);
        h.Events.Publish(new PushedEvent { targetId = b.unitId, direction = new Vec3(1f, 0f, 0f), distance = 5f, seconds = 0f });
        h.Events.Publish(new PushedEvent { targetId = b.unitId, direction = new Vec3(0f, 0f, 1f), distance = 5f, seconds = 0.5f });
        h.Run(1f);
        Check.True(Vec3.Approximately(b.position, h.Units.Unit(b.unitId).position), "still on its spot");
    }

    [Test]
    static void OverrunPaysExperience()
    {
        var h = new OverrunHarness();
        var o = h.Overrun.Rules;
        float Xp(int id) => h.Of<ExperienceAwardedEvent>().Where(e => e.playerId == id && e.source == ExperienceSource.Overrun).Sum(e => e.amount);

        // Near your own minion: 3 a second (catch-up can scale it; everyone is level 1 here).
        h.Place(A2, 260f, -50f);
        h.Scrapper(TeamId.A, 200f + A1 + 2f, -50f);
        h.Run(5f);
        Check.Near(5f * o.xpNearWavePerSecond, Xp(A1), "near own wave", 0.5f);
        Check.Near(0f, Xp(A2), "not near it, nothing");

        // A minion kill.
        h.Forget();
        int enemy = h.Scrapper(TeamId.B, 0f, 0f);
        h.Combat.Damage.Damage(A2, enemy, 1000f);
        Check.Near(o.xpMinionKill, Xp(A2), "minion kill");

        // Bastion damage, then every hero of the team for the Bastion destroyed.
        h.Forget();
        var b = FwdB(h);
        h.Scrapper(TeamId.A, b.position.x, b.position.z - 5f);
        h.Tick();
        h.Forget();
        h.Combat.Damage.Damage(A2, b.unitId, 200f);
        Check.Near(200f * o.xpPerBastionDamage, Xp(A2), "Bastion damage");
        h.Forget();
        h.Destroy(b);
        Check.Near(o.xpBastionDestroyed, Xp(A1), "A1 for the Bastion");
        Check.Near(o.xpBastionDestroyed, Xp(A2), "A2 for the Bastion");
        Check.Near(0f, Xp(B1), "never the side that lost it");
    }

    [Test]
    static void OverrunStateRidesTheSnapshotAndTheWire()
    {
        var h = new OverrunHarness();
        h.Destroy(FwdB(h));
        var snap = h.Match.TakeSnapshot();
        h.Overrun.FillSnapshot(snap);
        var copy = ((SnapshotMessage)WireCodec.Decode(WireCodec.Encode(new SnapshotMessage { snapshot = snap }))).snapshot;
        Check.True(copy.overrun != null && copy.overrun.bastions.Count == 4, "four Bastions on the wire");
        Check.True(copy.overrun.gateOpenB && !copy.overrun.Bastion(TeamId.B, BastionTier.Forward).alive, "gate and the fallen Bastion");
        Check.Near(CoreB(h).shield, copy.overrun.Bastion(TeamId.B, BastionTier.Core).shield, "shields");
        foreach (var e in new GameEvent[] { new BastionDestroyedEvent { team = TeamId.B }, new BastionShieldBrokenEvent(), new BastionShieldRestoredEvent(), new SideGateOpenedEvent(), new LatePushStartedEvent { waveSeconds = 20f } })
            Check.True(WireCodec.Decode(WireCodec.Encode(new EventMessage { gameEvent = e })) is EventMessage, e.GetType().Name + " on the wire");
    }

    [Test]
    static void OverrunNeedsTheModeAndFourBastions()
    {
        var h = new OverrunHarness();
        var spots = h.Breakwater.bastions.Take(3).ToList();
        bool threw = false;
        try { new OverrunServer(h.Lanes, h.Events, spots); } catch (ArgumentException) { threw = true; }
        Check.True(threw, "three Bastions are refused");
        var c = new CombatHarness(1);
        var lanes = new LaneServer(c.Match, c.Combat, c.Map, c.Events, h.Breakwater);
        threw = false;
        try { new OverrunServer(lanes, c.Events, h.Breakwater); } catch (ArgumentException) { threw = true; }
        Check.True(threw, "only in Overrun");
    }
}

/// <summary>One bot match of the Overrun report, as a test: it ends inside 15 minutes with a winner.</summary>
static class OverrunBotTests
{
    [Test]
    static void ABotMatchEndsInsideFifteenMinutesWithAWinner()
    {
        var r = OverrunSim.Play(10);
        Check.True(r.seconds <= ModeDef.Overrun().timeCapSeconds + 1f, "ended by the cap (" + r.seconds.ToString("0") + " s)");
        Check.True(r.winner != TeamId.None, "with a winner");
        Check.True(r.minionsKilled > 0 && r.heroDeaths > 0, "waves and heroes fought");
    }
}
