using System;
using System.Collections.Generic;
using System.Linq;
using DuckAndCover.Core;
using static HeroHarness;

/// <summary>
/// Hero kits against minion waves (WP20): every starter hero has a tested way to clear or protect a
/// wave, barriers and gates stop minion fire and minion feet, the point upgrades have a lane
/// version, and units give ultimate charge at a share. The wave is <see cref="WaveHarness"/>'s
/// stand-in (WP15's numbers).
/// </summary>
static class WaveKitTests
{
    /// <summary>A hero at a level on flat ground at the origin, facing +z.</summary>
    static HeroHarness OnGround(string heroId, int level = 1, int path = 0)
    {
        var h = WaveHarness.Hero(heroId, level, path);
        WaveHarness.AddKinds(h.Combat.Units);
        h.Map.Wall(new Vec3(0f, -0.5f, 0f), new Vec3(400f, 1f, 400f));
        h.Place(A1, 0f, 0f);
        h.Tick();
        h.Forget();
        return h;
    }

    static UnitRules U(HeroHarness h) => h.Combat.Units;

    static float WaveDamage(HeroHarness h, int[] wave, Func<DamagedEvent, bool> which = null) =>
        h.Of<DamagedEvent>().Where(e => wave.Contains(e.targetId) && (which == null || which(e))).Sum(e => e.amount);

    // ---- Each hero's job ----

    [Test]
    static void LancersGrenadeCatchesTheWholeWave()
    {
        foreach (int form in new[] { 0, 1 })
        {
            string name = form == 0 ? "Thermite" : "Clinging Fire";
            var h = OnGround("lancer", 8, form);
            var wave = WaveHarness.Spawn(U(h), TeamId.B, new Vec3(0f, 0f, 14f));
            h.Tick();
            Check.True(h.Use(A1, 2, WaveHarness.Center(U(h), wave) - h.Eye(A1)).IsAccepted, name + ": grenade thrown");
            h.Run(0.1f);
            var landed = h.Of<DamagedEvent>().Where(e => e.abilityId == "lancer_firebomb").Select(e => e.targetId).Distinct().ToList();
            Check.Equal(4, landed.Count(wave.Contains), name + ": the blast reaches all four minions");
            h.Run(5f);
            float dealt = WaveDamage(h, wave);
            Check.True(dealt >= 400f, name + ": blast and fire take " + dealt.ToString("0") + " off the wave on their own (of 650 health)");
            if (form == 0) Check.True(WaveHarness.Alive(U(h), wave) <= 2, "Thermite destroys Scrappers outright: " + WaveHarness.Alive(U(h), wave) + " left");
        }
    }

    [Test]
    static void BulwarksRampartShieldsHimFromWaveFire()
    {
        var h = OnGround("bulwark");
        h.Place(A2, 4f, 0f); // a teammate off to the side, outside Rampart's cover
        h.Tick();
        Check.True(h.Use(A1, 0, Ahead).IsAccepted, "Rampart up");
        h.Run(0.4f);
        var wave = WaveHarness.Spawn(U(h), TeamId.B, new Vec3(0f, 0f, 10f));
        h.Tick();
        Check.False(U(h).LineOfFire(wave[2], A1), "the Scrapper straight ahead can't hit Bulwark through Rampart");
        Check.True(U(h).LineOfFire(wave[2], A2), "but can hit the teammate it doesn't cover");
        int ours = U(h).Spawn(WaveHarness.Scrapper, TeamId.A, new Vec3(0f, 0f, -1.5f));
        h.Tick();
        Check.True(U(h).LineOfFire(ours, wave[2]), "Bulwark's own minions shoot through Rampart, as his teammates do");
    }

    [Test]
    static void BulwarksBulkheadHoldsAWave()
    {
        var h = OnGround("bulwark", 8, 1); // Wide Bulkhead
        h.Place(A2, 3f, 0f);
        Check.True(h.Use(A1, 2, new Vec3(0f, 0f, 6f) - h.Eye(A1)).IsAccepted, "Bulkhead across the lane 6 m ahead");
        h.Run(0.6f);
        var wave = WaveHarness.Spawn(U(h), TeamId.B, new Vec3(0f, 0f, 12f));
        h.Tick();
        foreach (int id in wave) Check.False(U(h).LineOfFire(id, A1) || U(h).LineOfFire(id, A2), "minion " + id + " can't shoot through it");
        for (int i = 0; i < 60; i++) { WaveHarness.WalkTick(U(h), wave, -1f, Dt); h.Tick(); }
        var wall = h.Abilities.State.objects.First(o => o.defId == "bulwark_bulkhead");
        foreach (int id in wave) Check.True(U(h).Unit(id).position.z > wall.position.z, "minion " + id + " stops at the wall");
    }

    [Test]
    static void MenderKeepsHerWaveAlive()
    {
        var h = OnGround("mender");
        var ours = WaveHarness.Spawn(U(h), TeamId.A, new Vec3(0f, 0f, 6f), 1f);
        foreach (int id in ours) h.Combat.Damage.Damage(B1, id, 100f, "setup");
        foreach (int id in ours) h.Abilities.ApplyStatus(id, AbilityLibrary.SlowStatus, B1, 0.5f, 5f);
        h.Tick();
        h.Forget();

        Check.True(h.Use(A1, 0, WaveHarness.Center(U(h), ours) - h.Eye(A1)).IsAccepted, "Mending Canister on the wave");
        h.Run(3.1f);
        foreach (int id in ours)
            Check.True(h.Of<HealedEvent>().Any(e => e.targetId == id && e.healerId == A1), "the canister mends minion " + id);

        h.Place(A1, 0f, 4f);
        h.Tick();
        Check.True(U(h).Speed(ours[0]) < 3f, "slowed before Purge");
        Check.True(h.Use(A1, 1, Ahead).IsAccepted, "Purge");
        h.Tick();
        foreach (int id in ours) Check.Near(4f, U(h).Speed(id), "Purge frees minion " + id + " from the slow", 0.01f);

        Check.True(h.Use(A1, 2, Ahead).IsAccepted, "Mending Rounds");
        h.Forget();
        h.Run(0.2f);
        Check.True(h.FireAt(A1, Hitboxes.Chest(h.R, new BodyPose(U(h).Unit(ours[0]).position, false))).IsAccepted, "a healing round at a minion");
        Check.True(h.Of<HealedEvent>().Any(e => e.targetId == ours[0] && e.healerId == A1 && e.abilityId == "mender_sidearm"), "Mending Rounds mend a minion too");
    }

    [Test]
    static void EveryHeroCanClearAWaveAlone()
    {
        // The report's bot: stand still, shoot the first minion, use every ability when ready.
        // Bulwark is a close-range hero (shotgun), so he works from 3 m; the rest from 9 m.
        var limits = new Dictionary<string, (float at, float seconds)>
        {
            ["bulwark"] = (3f, 20f), ["lancer"] = (9f, 10f), ["mender"] = (9f, 15f), ["rigger"] = (9f, 8f), ["warchief"] = (2f, 20f),
        };
        foreach (var heroId in StarterIds)
        {
            var c = WaveHarness.Clear(heroId, 1, 0, limits[heroId].at, 30f);
            Check.True(c.left == 0 && c.seconds <= limits[heroId].seconds,
                heroId + " at level 1 clears a wave in " + c.seconds.ToString("0.0") + " s (" + c.left + " left), within " + limits[heroId].seconds + " s");
        }
    }

    [Test]
    static void AGateHoldsALaneMinionAndItsFire()
    {
        // WP15's real minion walking and shooting (WaveRules.StepUnit and CanHit) against a placed gate.
        var h = new LaneHarness(waves: false);
        var gateDef = new ObjectDef { id = "test_gate", lifetime = 6f, health = 1500f, blocksMovement = true, blocksShots = true, width = 8f, height = 4f };
        var gate = h.H.Combat.Abilities.SpawnObject(gateDef, CombatHarness.B1, new Vec3(0f, 0f, -80f), "", Vec3.Forward);
        Check.True(gate != null, "team B's gate across the lane, 20 m from team A's end");
        int id = h.L.Waves.SpawnMinion(LaneRuleset.Scrapper, TeamId.A, 0);
        Action hold = () => h.H.Place(CombatHarness.B1, 0f, -75f); // a hero just behind the gate, in range
        h.Run(5.5f, hold);
        var u = h.H.Combat.Units.Unit(id);
        Check.True(u.position.z < -80f, "the minion is held at the gate: z " + u.position.z.ToString("0.00"));
        Check.Equal(0, h.H.Of<DamagedEvent>().Count(d => d.attackerId == id), "and can't shoot the hero behind it");
        var m = h.L.State.minions.First(x => x.unitId == id);
        Check.Near(u.position.z + LaneHarness.HalfLength, m.progress, "its place along the lane is where it stands, not where it wanted to be", 0.5f);

        h.Run(3f, hold); // the gate's 6 s run out
        Check.False(h.H.Combat.Abilities.State.objects.Contains(gate), "the gate is gone");
        Check.True(h.H.Of<DamagedEvent>().Any(d => d.attackerId == id && d.targetId == CombatHarness.B1), "then the minion shoots the hero");
    }

    // ---- The lane version of point upgrades ----

    [Test]
    static void PointUpgradesWorkBesideOwnMinions()
    {
        var h = OnGround("bulwark", 14, 0); // Anchor and Braced
        float alone = h.Stat(A1, StatIds.DamageTaken);
        int enemy = U(h).Spawn(WaveHarness.Scrapper, TeamId.B, new Vec3(0f, 0f, 3f));
        h.Tick();
        Check.Near(alone, h.Stat(A1, StatIds.DamageTaken), "an enemy minion close by is not holding ground");
        int ours = U(h).Spawn(WaveHarness.Scrapper, TeamId.A, new Vec3(0f, 0f, h.R.escortRadius + 1f));
        h.Tick();
        Check.Near(alone, h.Stat(A1, StatIds.DamageTaken), "an own minion more than " + h.R.escortRadius + " m away doesn't count");
        U(h).Walk(ours, new Vec3(0f, 0f, -3f), 180f);
        h.Tick();
        float beside = h.Stat(A1, StatIds.DamageTaken);
        Check.Near(alone * 0.92f * 0.85f, beside, "beside his own minion: Anchor (8%) and Braced (15%) less damage taken", 1e-3f);
        h.Combat.Damage.Damage(enemy, ours, 1000f);
        h.Tick();
        Check.Near(alone, h.Stat(A1, StatIds.DamageTaken), "gone when the minion is destroyed");
    }

    // ---- Ultimate charge ----

    [Test]
    static void UnitsGiveUltimateChargeAtAShare()
    {
        var h = OnGround("mender");
        h.R.ultimateChargePerSecond = 0f;
        h.LevelTo(A1, 8, false);
        h.Place(B1, 0f, 6f);
        int unit = U(h).Spawn(WaveHarness.Warden, TeamId.B, new Vec3(3f, 0f, 6f));
        int own = U(h).Spawn(WaveHarness.Warden, TeamId.A, new Vec3(-3f, 0f, 6f));
        h.Tick();
        h.Unit(A1).ultimateCharge = 0f;
        float dealt = h.Combat.Damage.Damage(A1, B1, 100f, "test");
        float fromHero = h.Unit(A1).ultimateCharge;
        Check.Near(dealt * h.R.ultimateChargePerDamage, fromHero, "damage to a hero: full charge");
        h.Unit(A1).ultimateCharge = 0f;
        Check.Near(dealt, h.Combat.Damage.Damage(A1, unit, 100f, "test"), "the same damage to a minion");
        Check.Near(fromHero * h.R.ultimateChargeUnitShare, h.Unit(A1).ultimateCharge, "gives the unit share of the charge");
        h.Unit(A1).ultimateCharge = 0f;
        h.Combat.Damage.Damage(B1, own, 100f, "setup");
        h.Combat.Damage.Heal(A1, own, 100f, "test");
        Check.Near(100f * h.R.ultimateChargePerHealing * h.R.ultimateChargeUnitShare, h.Unit(A1).ultimateCharge, "healing an own minion: the unit share too");
    }
}
