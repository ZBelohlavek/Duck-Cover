using System;
using System.Linq;
using DuckAndCover.Core;
using static HeroHarness;

/// <summary>
/// The vampire lord (Nocturne): Blood Darts thrown from his hands (no ammo, no reload), Grasping Claw (a
/// grab that steals max health), Bat Swarm (an untouchable dash) and Crimson Sweep (a life-stealing sweep
/// that hits everyone in front), and his two paths, Blood Tyrant (left) and Nightstalker (right). Also the
/// new pieces they rest on: endless weapons, projectile head hits, the headDamage stat, the invulnerable
/// status flag, arc sweeps, effect life steal, minTargets, pulls toward the caster and the execute effect.
/// </summary>
static class VampireKitTests
{
    const int Grasp = 0, Bats = 1, Sweep = 2;
    const string Drained = "vampire_drained", Gorged = "vampire_gorged";

    /// <summary>The vampire at the origin facing +z and a Bulwark (300 health) <paramref name="ahead"/> m in front, no shields.</summary>
    static HeroHarness Duel(float ahead = 4f, params string[] upgrades)
    {
        var h = With(A1, "vampire", B1, "bulwark");
        h.Place(A1, 0f, 0f);
        h.Place(B1, 0f, ahead);
        foreach (var u in upgrades) Check.True(h.Heroes.ApplyUpgrade(A1, "vampire", u).IsAccepted, u);
        h.NoShield(B1);
        h.Tick();
        h.Forget();
        return h;
    }

    /// <summary>Damage one dart thrown at <paramref name="at"/> does to the Bulwark, once it has flown.</summary>
    static float Dart(HeroHarness h, Vec3 at)
    {
        h.Run(0.3f); // throw rate
        h.NoShield(B1);
        float before = h.Health(B1);
        Check.True(h.FireAt(A1, at).IsAccepted, "throw");
        h.Run(0.3f);
        return before - h.Health(B1);
    }

    static Vec3 Head(HeroHarness h, int id) => Hitboxes.HeadCenter(h.R, h.M(id).Pose(true));

    // ---- The kit ----

    [Test]
    static void TheKitIsDartsAGrabBatsAndASweepAllFromLevelOne()
    {
        var h = Duel();
        var slots = h.Unit(A1).slots.Select(s => s.abilityId).ToArray();
        Check.Equal("vampire_grasp,vampire_bats,vampire_sweep", string.Join(",", slots), "E, F and X");
        Check.Equal("blood_darts", string.Join(",", h.Combat.Weapons.Weapons(A1).weapons), "his darts and nothing else: no gun");
        var darts = h.Combat.Weapons.Library.Weapon("blood_darts");
        Check.Equal(WeaponKind.Projectile, darts.kind, "darts fly");
        Check.True(darts.NoAmmo(), "and spend no ammo");
        Check.False(h.Abilities.AbilityIn(A1, Sweep).ultimate, "Crimson Sweep runs on a cooldown");
        Check.Near(250f, h.Health(A1), "250 health");
        Check.True(h.Use(A1, Sweep, Ahead).IsAccepted, "and works at level 1");
    }

    [Test]
    static void DartsFlyNeverReloadAndHitHarderInTheHead()
    {
        var h = Duel(15f);
        float body = Dart(h, h.ChestOf(B1));
        Check.Near(28f, body, "28 to the body", 0.01f);
        Check.Equal(1, h.Of<ProjectileLaunchedEvent>().Count, "a dart is a projectile");
        Check.Near(28f * 1.75f, Dart(h, Head(h, B1)), "1.75x to the head: a dart counts where it strikes", 0.01f);

        h.Forget();
        int thrown = 0;
        for (int i = 0; i < 60; i++)
        {
            h.Run(0.25f);
            if (h.FireAt(A1, h.ChestOf(B1) + Vec3.Up * 30f).IsAccepted) thrown++;
        }
        Check.Equal(60, thrown, "60 throws in a row, one every quarter second");
        Check.Equal(0, h.Of<ReloadStartedEvent>().Count, "and never a reload");
        Check.False(h.Combat.Weapons.Weapons(A1).Reloading, "nothing to wait for");
        Check.False(h.Combat.Apply(new ReloadCommand { playerId = A1 }).IsAccepted, "there is nothing to reload");
        Check.False(h.FireAt(A1, h.ChestOf(B1)).IsAccepted, "throws faster than the rate are refused");
    }

    [Test]
    static void GraspingClawStealsMaxHealthAndPulls()
    {
        var h = Duel(5f);
        float theirs = h.Combat.Damage.MaxHealth(B1), before = h.Health(B1);
        h.Unit(A1).health = 200f;
        Check.True(h.UseAt(A1, Grasp, B1).IsAccepted, "Grasping Claw");
        Check.Near(before - 45f, h.Health(B1), "45 damage", 0.01f);
        Check.Near(theirs - 30f, h.Combat.Damage.MaxHealth(B1), "they lose 30 max health", 0.01f);
        Check.Near(280f, h.Combat.Damage.MaxHealth(A1), "he gains 30 max health", 0.01f);
        Check.Near(230f, h.Health(A1), "and the 30 comes with it as health", 0.01f);
        var pull = h.Of<PushedEvent>().Single(e => e.targetId == B1);
        Check.Equal(PushDirections.Caster, pull.mode, "the target is pulled");
        Check.Near(2f, pull.distance, "2 m", 0.01f);
        Check.True(pull.direction.z < -0.99f, "toward him");

        for (int i = 0; i < 3; i++) { h.Run(14.1f); Check.True(h.UseAt(A1, Grasp, B1).IsAccepted, "grab " + (i + 2)); }
        Check.Near(theirs - 90f, h.Combat.Damage.MaxHealth(B1), "the steal stacks 3 times at most", 0.01f);
        Check.Near(340f, h.Combat.Damage.MaxHealth(A1), "for 90 max health", 0.01f);
        h.Run(30.1f);
        Check.Near(theirs, h.Combat.Damage.MaxHealth(B1), "30 s after the last grab they have it back", 0.01f);
        Check.Near(250f, h.Combat.Damage.MaxHealth(A1), "and he loses it", 0.01f);
    }

    [Test]
    static void GraspingClawMissingStealsNothingAndPurgeGivesItBack()
    {
        var h = Duel(15f);
        Check.True(h.Use(A1, Grasp, Ahead).IsAccepted, "a grab at nobody (the Bulwark is 15 m off)");
        Check.Near(250f, h.Combat.Damage.MaxHealth(A1), "gains nothing", 0.01f);
        Check.False(h.Unit(A1).HasStatus(Gorged), "no steal on a miss");

        var m = With(A1, "vampire", B1, "mender");
        m.Place(A1, 0f, 0f);
        m.Place(B1, 0f, 4f);
        m.Tick();
        Check.True(m.UseAt(A1, Grasp, B1).IsAccepted, "grab the Mender");
        Check.True(m.Unit(B1).HasStatus(Drained), "drained");
        Check.True(m.Use(B1, 1, Ahead).IsAccepted, "Purge");
        Check.False(m.Unit(B1).HasStatus(Drained), "Purge clears the drain off them");
        Check.Near(200f, m.Combat.Damage.MaxHealth(B1), "and their max health is back", 0.01f);
    }

    [Test]
    static void BatSwarmDashesAndCantBeHurtOrDebuffed()
    {
        var h = Duel(15f);
        Check.True(h.Use(A1, Bats, Ahead).IsAccepted, "Bat Swarm");
        var dash = h.Of<PushedEvent>().Single(e => e.targetId == A1);
        Check.Equal(PushDirections.Aim, dash.mode, "a dash along the aim");
        Check.Near(7f, dash.distance, "7 m", 0.01f);
        Check.True(h.Unit(A1).invulnerable, "untouchable while a swarm");
        float health = h.Health(A1);
        h.Run(0.3f); // the Bulwark's shotgun is ready
        Check.True(h.FireAt(B1, h.ChestOf(A1)).IsAccepted, "the Bulwark fires");
        Check.Near(health, h.Health(A1), "no damage lands", 0.01f);
        Check.False(h.Abilities.ApplyStatus(A1, AbilityLibrary.StunStatus, B1, 1f, 1f), "no stun lands");
        Check.True(h.Abilities.ApplyStatus(A1, "vampire_night_hunger", A1, 0.3f, 1f), "his own good effects still do");
        h.Run(0.3f);
        Check.False(h.Unit(A1).invulnerable, "half a second later it's over");
        h.Run(0.6f);
        Check.True(h.FireAt(B1, h.ChestOf(A1)).IsAccepted, "fires again");
        Check.True(h.Health(A1) < health, "and now it hurts");
    }

    [Test]
    static void CrimsonSweepHitsEveryoneInFrontAndHeals()
    {
        var h = new HeroHarness(new System.Collections.Generic.Dictionary<int, string> { [A1] = "vampire", [A2] = "lancer", [B1] = "bulwark", [B2] = "rigger" });
        h.Place(A1, 0f, 0f);
        h.Place(B1, -2f, 3f);  // ahead, to the left: in the 120 degree arc
        h.Place(B2, 2f, 3.5f); // ahead, to the right: in too
        h.Place(A2, 0f, 2f);   // a teammate in front
        foreach (var p in new[] { A1, A2, B1, B2 }) h.NoShield(p);
        h.Tick();
        h.Forget();
        h.Unit(A1).health = 100f;
        float b1 = h.Health(B1), b2 = h.Health(B2), a2 = h.Health(A2);
        Check.True(h.Use(A1, Sweep, Ahead).IsAccepted, "Crimson Sweep");
        Check.Near(b1 - 55f, h.Health(B1), "55 to the enemy on the left", 0.01f);
        Check.Near(b2 - 55f, h.Health(B2), "and the one on the right", 0.01f);
        Check.Near(a2, h.Health(A2), "the teammate is untouched");
        Check.Near(100f + 110f * 0.4f, h.Health(A1), "he heals 40% of the 110 dealt", 0.01f);
        Check.Equal(0, h.Abilities.State.objects.Count, "no aura without the talent");

        var miss = Duel(3f);
        miss.Place(B1, 3f, -0.5f); // beside and a little behind: outside the arc
        miss.Tick();
        float before = miss.Health(B1);
        Check.True(miss.Use(A1, Sweep, Ahead).IsAccepted, "Crimson Sweep");
        Check.Near(before, miss.Health(B1), "beside him is outside the sweep");
        miss.Place(B1, 0f, 5.6f);
        miss.Run(16.1f);
        Check.True(miss.Use(A1, Sweep, Ahead).IsAccepted, "again");
        Check.Near(before, miss.Health(B1), "5.6 m is out of reach");
    }

    [Test]
    static void EveryVampireAbilityAndUpgradeIsData()
    {
        // The hero data loads the strict way, and each upgrade resolves against his abilities.
        var h = Duel();
        foreach (var level in new[] { 2, 4, 6, 8, 10, 12, 14, 16, 18, 20 })
            Check.True(ProgressionHarness.DataCatalog().Get("vampire").levels.Any(l => l.level == level && l.options.Length == 2), "a pick of two at level " + level);
        Check.Equal(20, ProgressionHarness.DataCatalog().Get("vampire").upgrades.Length, "20 upgrades");
    }

    // ---- Blood Tyrant (left) ----

    [Test]
    static void BloodTyrantSiphonsMore()
    {
        var h = Duel(3f, "vampire_thick_blood", "vampire_deep_siphon", "vampire_thirst", "vampire_wide_sweep", "vampire_blood_bank",
            "vampire_greedy_grasp", "vampire_iron_veins");
        Check.Near((250f + 25f) * 1.08f, h.Combat.Damage.MaxHealth(A1), "Thick Blood and Iron Veins", 0.05f);
        Check.Near(1.1f, h.Stat(A1, StatIds.HealingTaken), "Blood Bank", 0.001f);
        Check.Near(0.08f, h.Stat(A1, StatIds.LifeSteal), "Thirst", 0.001f);

        h.Unit(A1).health = 100f;
        Check.True(h.Use(A1, Sweep, Ahead).IsAccepted, "Crimson Sweep");
        Check.Near(100f + 55f * 0.6f * 1.1f, h.Health(A1), "Deep Siphon: 60% back, and Blood Bank on top", 0.05f);

        h.Place(B1, 3.5f, 0.5f); // beside him: inside a 180 degree, 6.5 m sweep
        h.Run(16.1f);
        float before = h.Health(B1);
        Check.True(h.Use(A1, Sweep, Ahead).IsAccepted, "again");
        Check.Near(before - 55f, h.Health(B1), "Wide Sweep reaches beside him", 0.01f);

        float max = h.Combat.Damage.MaxHealth(B1);
        Check.True(h.UseAt(A1, Grasp, B1).IsAccepted, "Grasping Claw");
        Check.Near(max - 45f, h.Combat.Damage.MaxHealth(B1), "Greedy Grasp: 45 max health a grab", 0.01f);
    }

    [Test]
    static void BloodAuraDrainsWhenTheSweepHitsACrowd()
    {
        var h = new HeroHarness(new System.Collections.Generic.Dictionary<int, string> { [A1] = "vampire", [B1] = "bulwark", [101 + 1] = "rigger", [103] = "lancer" }, perTeam: 3);
        Check.True(h.Heroes.ApplyUpgrade(A1, "vampire", "vampire_blood_aura").IsAccepted, "Blood Aura");
        h.Place(A1, 0f, 0f);
        h.Place(B1, -1.5f, 3f);
        h.Place(B2, 1.5f, 3f);
        h.Place(103, 50f, 50f); // far away at first
        foreach (var p in new[] { A1, B1, B2, 103 }) h.NoShield(p);
        h.Tick();
        h.Forget();
        Check.True(h.Use(A1, Sweep, Ahead).IsAccepted, "a sweep that hits 2");
        Check.Equal(0, h.Abilities.State.objects.Count, "two isn't a crowd: no aura");

        h.Place(103, 0f, 2f);
        h.Run(16.1f);
        h.Unit(A1).health = 50f;
        Check.True(h.Use(A1, Sweep, Ahead).IsAccepted, "a sweep that hits 3");
        Check.Equal(1, h.Abilities.State.objects.Count, "Blood Aura is up");
        float b1 = h.Health(B1), hp = h.Health(A1);
        h.Run(2f);
        Check.Near(b1 - 30f, h.Health(B1), "15 health a second from enemies within 4 m", 0.6f);
        Check.True(h.Health(A1) > hp + 3 * 30f - 2f, "and he gains what they lose");
        h.Place(A1, 0f, -20f); // he walks off: the aura goes with him
        h.Run(1f);
        Check.Near(b1 - 30f, h.Health(B1), "out of its reach once he leaves", 0.6f);
        h.Run(2.5f);
        Check.Equal(0, h.Abilities.State.objects.Count, "gone after 5 seconds");

        var k = new HeroHarness(new System.Collections.Generic.Dictionary<int, string> { [A1] = "vampire", [B1] = "bulwark", [B2] = "rigger" });
        foreach (var u in new[] { "vampire_blood_aura", "vampire_hungry_aura", "vampire_blood_tyrant" })
            Check.True(k.Heroes.ApplyUpgrade(A1, "vampire", u).IsAccepted, u);
        k.Place(A1, 0f, 0f);
        k.Place(B1, -1.5f, 3f);
        k.Place(B2, 1.5f, 3f);
        k.Tick();
        Check.True(k.Use(A1, Sweep, Ahead).IsAccepted, "a sweep that hits 2");
        Check.Equal(1, k.Abilities.State.objects.Count, "Hungry Aura: two is enough");
        var aura = k.Abilities.State.objects[0];
        Check.Near(5.5f, aura.def.radius, "Blood Tyrant: 1.5 m further", 0.01f);
        Check.Near(7f, aura.def.lifetime, "Hungry Aura: 2 seconds longer", 0.01f);
        Check.Near(0.1f, k.Stat(A1, StatIds.LifeSteal), "Blood Tyrant: 10% more dart life steal", 0.001f);
    }

    // ---- Nightstalker (right) ----

    [Test]
    static void NightstalkerHeadHitsAndFasterDarts()
    {
        var h = Duel(15f, "vampire_keen_eye", "vampire_quick_hands", "vampire_killing_eye", "vampire_deathmark", "vampire_night_runner");
        Check.Near(0.55f, h.Stat(A1, StatIds.HeadDamage), "Keen Eye, Killing Eye and Deathmark", 0.001f);
        Check.Near(1.08f, h.Stat(A1, StatIds.FireRate), "Quick Hands", 0.001f);
        Check.Near(1.08f, h.Stat(A1, StatIds.MoveSpeed), "Night Runner", 0.001f);
        Check.Near(28f, Dart(h, h.ChestOf(B1)), "body hits are unchanged", 0.01f);
        Check.Near(28f * 1.75f * 1.55f, Dart(h, Head(h, B1)), "head hits deal 55% more", 0.01f);
    }

    [Test]
    static void NightstalkerFliesFurtherTwiceAndHitsHarderAfter()
    {
        var h = Duel(15f, "vampire_long_flight", "vampire_twin_flight", "vampire_out_of_the_dark");
        Check.True(h.Use(A1, Bats, Ahead).IsAccepted, "Bat Swarm");
        Check.Near(10f, h.Of<PushedEvent>().Single(e => e.targetId == A1).distance, "Long Flight: 10 m", 0.01f);
        Check.Near(1.3f, h.Stat(A1, StatIds.WeaponDamage), "Out of the Dark: darts deal 30% more", 0.001f);
        Check.True(h.Use(A1, Bats, Ahead).IsAccepted, "Twin Flight: a second swarm straight away");
        h.Run(3.1f);
        Check.Near(1f, h.Stat(A1, StatIds.WeaponDamage), "for 3 seconds", 0.001f);

        var q = Duel(3f, "vampire_quick_sweep");
        Check.True(q.Use(A1, Sweep, Ahead).IsAccepted, "Crimson Sweep");
        q.Run(13.1f);
        Check.True(q.Use(A1, Sweep, Ahead).IsAccepted, "Quick Sweep: ready again after 13 s");
    }

    [Test]
    static void ExecutionerFinishesOffTheWeak()
    {
        var plain = Duel(4f);
        plain.Unit(B1).health = 50f;
        Check.True(plain.UseAt(A1, Grasp, B1).IsAccepted, "a grab without Executioner");
        Check.Near(5f, plain.Health(B1), "just the 45 damage");

        var h = Duel(4f, "vampire_executioner");
        h.Unit(B1).health = 110f; // 65 after the grab: above 20% of the 270 max he has left (54)
        Check.True(h.UseAt(A1, Grasp, B1).IsAccepted, "grab a healthy Bulwark");
        Check.True(h.S.IsAlive(B1), "too healthy to execute");
        h.Unit(B1).health = 93f; // 48 after the grab: 20% of the 240 max he has left after two steals
        h.Unit(B1).shields.Add(new ShieldState { amount = 40f, timeLeft = 10f });
        h.Run(14.1f);
        h.Forget();
        Check.True(h.UseAt(A1, Grasp, B1).IsAccepted, "grab him again");
        Check.False(h.S.IsAlive(B1), "Executioner: finished off at 20% or less, shield and all");
        var kill = h.Of<DamagedEvent>().Last(e => e.targetId == B1);
        Check.True(kill.eliminated && kill.attackerId == A1 && kill.abilityId == "vampire_grasp", "credited to the grab");

        var d = Duel(4f, "vampire_executioner", "vampire_deathmark");
        d.Unit(B1).health = 120f; // 75 after the grab, 30% of 270 is 81
        Check.True(d.UseAt(A1, Grasp, B1).IsAccepted, "grab");
        Check.False(d.S.IsAlive(B1), "Deathmark: executes at 30%");

        var u = Duel(20f, "vampire_executioner");
        WaveHarness.AddKinds(u.Combat.Units);
        int minion = u.Combat.Units.Spawn(WaveHarness.Scrapper, TeamId.B, new Vec3(0f, 0f, 4f));
        u.Tick();
        u.Combat.Damage.Unit(minion).health = 1f + 45f;
        Check.True(u.Use(A1, Grasp, u.Combat.Units.Unit(minion).position + Vec3.Up - u.Eye(A1)).IsAccepted, "grab a minion");
        Check.True(u.Combat.Units.IsAlive(minion), "units are never executed, only hurt");
    }
}
