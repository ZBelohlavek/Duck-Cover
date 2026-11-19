using System;
using System.Linq;
using DuckAndCover.Core;
using static HeroHarness;

/// <summary>
/// The war chief (Gashrak), the melee bruiser: a greataxe swing instead of a gun, War Leap (a leap that
/// knocks up where it lands), Axe Throw (a stun) and Rage (regen and damage), and his two paths,
/// Bruiser (left) and Warlord (right). Also the melee weapon kind and the leap and knock-up pushes.
/// </summary>
static class WarchiefKitTests
{
    const int Leap = 0, Throw = 1, Rage = 2;

    /// <summary>The war chief at the origin facing +z and a Bulwark (300 health) <paramref name="ahead"/> m in front, no shields.</summary>
    static HeroHarness Duel(float ahead = 2f, params string[] upgrades)
    {
        var h = With(A1, "warchief", B1, "bulwark");
        h.Place(A1, 0f, 0f);
        h.Place(B1, 0f, ahead);
        foreach (var u in upgrades) Check.True(h.Heroes.ApplyUpgrade(A1, "warchief", u).IsAccepted, u);
        h.NoShield(B1);
        h.Tick();
        h.Forget();
        return h;
    }

    /// <summary>Damage one swing straight ahead does to the Bulwark now.</summary>
    static float Swing(HeroHarness h)
    {
        h.Run(1f); // swing rate
        h.NoShield(B1);
        float before = h.Health(B1);
        Check.True(h.FireAt(A1, h.Eye(A1) + Ahead * 2f).IsAccepted, "swing");
        return before - h.Health(B1);
    }

    // ---- The kit ----

    [Test]
    static void TheKitIsAnAxeLeapThrowAndRageAllFromLevelOne()
    {
        var h = Duel();
        var slots = h.Unit(A1).slots.Select(s => s.abilityId).ToArray();
        Check.Equal("warchief_leap,warchief_axe_throw,warchief_rage", string.Join(",", slots), "E, F and X");
        var weapons = h.Combat.Weapons.Weapons(A1).weapons;
        Check.Equal("greataxe", string.Join(",", weapons), "the greataxe and nothing else: no gun");
        Check.Equal(WeaponKind.Melee, h.Combat.Weapons.Library.Weapon("greataxe").kind, "a melee weapon");
        Check.False(h.Abilities.AbilityIn(A1, Rage).ultimate, "Rage runs on a cooldown");
        Check.Near(275f, h.Health(A1), "275 health");
        Check.True(h.Use(A1, Rage, Ahead).IsAccepted, "and works at level 1");
    }

    [Test]
    static void TheAxeHitsEveryoneInFrontWithinReachAndNeedsNoAmmo()
    {
        var h = new HeroHarness(new System.Collections.Generic.Dictionary<int, string> { [A1] = "warchief", [A2] = "lancer", [B1] = "bulwark", [B2] = "rigger" });
        h.Place(A1, 0f, 0f);
        h.Place(B1, -1f, 2f);  // ahead, to the left: in the arc
        h.Place(B2, 1.2f, 2f); // ahead, to the right: in the arc too
        h.Place(A2, 0f, 1.5f); // a teammate right in front
        foreach (var p in new[] { A1, A2, B1, B2 }) h.NoShield(p);
        h.Tick();
        h.Forget();
        float b1 = h.Health(B1), b2 = h.Health(B2), a2 = h.Health(A2);
        Check.True(h.FireAt(A1, h.Eye(A1) + Ahead * 2f).IsAccepted, "swing");
        Check.Near(b1 - 60f, h.Health(B1), "60 to the enemy on the left", 0.01f);
        Check.Near(b2 - 60f, h.Health(B2), "and the one on the right: one swing cleaves", 0.01f);
        Check.Near(a2, h.Health(A2), "the teammate in front is untouched");
        var swung = h.Last<MeleeSwungEvent>();
        Check.True(swung != null && swung.hitIds.Length == 2, "clients are told who the swing reached");
        Check.Equal(0, h.Of<ShotFiredEvent>().Count, "a swing is no shot");

        Check.False(h.FireAt(A1, h.Eye(A1) + Ahead * 2f).IsAccepted, "swings faster than 0.75 s apart are refused");
        for (int i = 0; i < 5; i++) { h.Run(0.8f); Check.True(h.FireAt(A1, h.Eye(A1) + Ahead * 2f).IsAccepted, "swing " + (i + 2)); }
        Check.Equal(1, h.Combat.Weapons.Weapons(A1).CurrentAmmo, "swings never spend the magazine");
        Check.False(h.Combat.Weapons.Weapons(A1).Reloading, "and never reload");
        Check.False(h.Combat.Apply(new ReloadCommand { playerId = A1 }).IsAccepted, "there is nothing to reload");
    }

    [Test]
    static void TheAxeMissesBehindBeyondReachAndThroughWalls()
    {
        var h = Duel(4.5f);
        Check.Near(0f, Swing(h), "4.5 m away is out of reach");
        h.Place(B1, 0f, -2f);
        Check.Near(0f, Swing(h), "behind him is outside the swing");
        h.Place(B1, 2.5f, 0.3f);
        Check.Near(0f, Swing(h), "beside him, outside the 100 degree arc");
        h.Place(B1, 0f, 3.3f);
        Check.Near(60f, Swing(h), "3.3 m ahead: the 3.2 m reach plus the body", 0.01f);
        h.Map.Wall(new Vec3(0f, 1f, 1.5f), new Vec3(4f, 3f, 0.2f));
        Check.Near(0f, Swing(h), "a wall in between stops the axe");
    }

    [Test]
    static void TheAxeHitsMinionsToo()
    {
        var h = Duel(20f);
        WaveHarness.AddKinds(h.Combat.Units);
        int unit = h.Combat.Units.Spawn(WaveHarness.Scrapper, TeamId.B, new Vec3(0.5f, 0f, 2f));
        h.Tick();
        float before = h.Health(unit);
        h.Run(1f);
        Check.True(h.FireAt(A1, h.Eye(A1) + Ahead * 2f).IsAccepted, "swing");
        Check.Near(before - 60f, h.Health(unit), "a minion in front takes the swing", 0.01f);
    }

    [Test]
    static void WarLeapLeapsToTheAimAndKnocksUpWhereHeLands()
    {
        var h = Duel(10f);
        h.Map.Wall(new Vec3(0f, -0.5f, 0f), new Vec3(400f, 1f, 400f)); // a floor to land on
        h.Place(B1, 2.5f, 11f); // beside the landing spot, out of the aim's way
        h.Place(B2, 30f, 10f); // far off to the side: not caught
        h.Tick();
        float before = h.Health(B1);
        Check.True(h.Use(A1, Leap, Ahead).IsAccepted, "War Leap straight ahead");
        var leap = h.Of<PushedEvent>().Single(e => e.targetId == A1);
        Check.Equal(PushDirections.Leap, leap.mode, "a leap");
        Check.Near(11.5f, leap.distance, "aimed into the open, he goes the full 12 m (to the floor below, a step back)", 0.3f);
        Check.True(leap.lift > 1f, "in an arc");
        Check.Near(before, h.Health(B1), "nothing lands until he does");

        h.Run(0.85f);
        Check.Near(before - 30f, h.Health(B1), "30 damage where he lands", 0.01f);
        Check.True(h.Abilities.IsStunned(B1), "the enemy there is stunned");
        var knock = h.Of<PushedEvent>().Single(e => e.targetId == B1);
        Check.True(knock.lift > 0.5f && knock.distance < 0.01f, "and thrown straight up: a knock-up");
        h.Run(0.8f);
        Check.False(h.Abilities.IsStunned(B1), "for 0.75 seconds");
    }

    [Test]
    static void WarLeapStopsWhereTheAimLands()
    {
        var h = Duel(20f);
        h.Map.Wall(new Vec3(0f, 1.5f, 6f), new Vec3(10f, 3f, 0.4f));
        Check.True(h.Use(A1, Leap, Ahead).IsAccepted, "War Leap at a wall 6 m ahead");
        var leap = h.Of<PushedEvent>().Single(e => e.targetId == A1);
        Check.True(leap.distance > 4.5f && leap.distance < 6f, "he lands just in front of the wall: " + leap.distance.ToString("0.00") + " m");
    }

    [Test]
    static void AxeThrowStunsTheFirstEnemyHit()
    {
        var h = Duel(15f);
        float before = h.Health(B1);
        Check.True(h.UseAt(A1, Throw, B1).IsAccepted, "Axe Throw");
        Check.Near(before - 40f, h.Health(B1), "40 damage", 0.01f);
        Check.True(h.Abilities.IsStunned(B1), "stunned");
        Check.False(h.Combat.Apply(new MoveCommand { playerId = B1, position = h.M(B1).position + new Vec3(1f, 0f, 0f), placement = h.M(B1).placement }).IsAccepted, "can't move");
        h.Run(1.3f);
        Check.False(h.Abilities.IsStunned(B1), "for 1.25 seconds");
    }

    [Test]
    static void PurgeClearsHisStunsAndHushStopsHisAbilitiesButNotTheAxe()
    {
        var h = new HeroHarness(new System.Collections.Generic.Dictionary<int, string> { [A1] = "warchief", [B1] = "mender", [B2] = "rigger" });
        h.Place(A1, 0f, 0f);
        h.Place(B1, 0f, 12f);
        h.Place(B2, 3f, 2f);
        h.Tick();
        Check.True(h.UseAt(A1, Throw, B1).IsAccepted, "Axe Throw at the Mender");
        Check.True(h.Abilities.IsStunned(B1), "stunned");
        h.Abilities.Cleanse(B1);
        Check.False(h.Abilities.IsStunned(B1), "a cleanse (Purge) frees him");

        h.Abilities.ApplyStatus(A1, AbilityLibrary.SilenceStatus, B2, 1f, 3f);
        Check.False(h.Use(A1, Rage, Ahead).IsAccepted, "silenced: no Rage");
        Check.False(h.Use(A1, Leap, Ahead).IsAccepted, "and no War Leap");
        h.Run(1f);
        Check.True(h.FireAt(A1, h.Eye(A1) + Ahead * 2f).IsAccepted, "but the axe still swings");
    }

    [Test]
    static void RageRegeneratesAndHitsHarder()
    {
        var h = Duel();
        float plain = Swing(h);
        Check.Near(60f, plain, "a plain swing", 0.01f);
        h.Unit(A1).health = 100f;
        Check.True(h.Use(A1, Rage, Ahead).IsAccepted, "Rage");
        h.Run(2f);
        Check.Near(130f, h.Health(A1), "15 health a second", 1f);
        Check.Near(72f, Swing(h), "20% more damage", 0.01f);
        h.Run(5f);
        Check.Near(plain, Swing(h), "gone after 6 seconds", 0.01f);
    }

    // ---- The paths ----

    [Test]
    static void BruiserHitsHarderInRage()
    {
        var h = Duel(2f, "warchief_heavy_hands", "warchief_blood_up", "warchief_long_reach", "warchief_long_rage", "warchief_battle_hunger");
        Check.Near(60f * 1.06f, Swing(h), "Heavy Hands", 0.01f);
        h.Place(B1, 0f, 3.8f);
        Check.Near(60f * 1.06f, Swing(h), "Long Reach: 3.8 m is in reach now", 0.01f);
        h.Place(B1, 0f, 2f);
        h.Unit(A1).health = 100f;
        Check.True(h.Use(A1, Rage, Ahead).IsAccepted, "Rage");
        float hit = Swing(h);
        Check.Near(60f * 1.06f * 1.3f, hit, "Blood Up: 30% more in Rage", 0.05f);
        h.Run(6f);
        Check.True(h.Unit(A1).HasStatus("warchief_rage"), "Long Rage: still up after 7 seconds");
        h.Run(1.1f);
        Check.False(h.Unit(A1).HasStatus("warchief_rage"), "and over after 8");

        var b = Duel(10f, "warchief_crushing_landing", "warchief_wide_landing");
        b.Place(B1, 3.5f, 12f); // 3.5 m off the landing spot: only Wide Landing reaches him
        b.Tick();
        float before = b.Health(B1);
        Check.True(b.Use(A1, Leap, Ahead).IsAccepted, "War Leap");
        b.Run(0.85f);
        Check.Near(before - 70f, b.Health(B1), "Crushing Landing: 70 damage, reaching 5 m", 0.01f);
    }

    [Test]
    static void BruiserCapstoneAndBloodRage()
    {
        var h = Duel(2f, "warchief_blood_rage", "warchief_berserker");
        h.Unit(A1).health = 100f;
        Check.True(h.Use(A1, Rage, Ahead).IsAccepted, "Rage");
        h.Tick();
        Check.Near(1.2f, h.Stat(A1, StatIds.FireRate), "Berserker: swings 20% faster in Rage", 0.001f);
        Check.Near(0.15f, h.Stat(A1, StatIds.LifeSteal), "Blood Rage: 15% life steal in Rage", 0.001f);
        float healthBefore = h.Health(A1);
        float hit = Swing(h);
        Check.Near(60f * 1.4f, hit, "Berserker: 40% more damage in Rage", 0.05f);
        Check.True(h.Health(A1) > healthBefore + 15f * 0.8f + hit * 0.14f, "the swing heals him on top of the regen");
    }

    [Test]
    static void WarlordChainsLongerStuns()
    {
        var h = Duel(15f, "warchief_heavy_haft", "warchief_twin_axes");
        Check.True(h.UseAt(A1, Throw, B1).IsAccepted, "first axe");
        h.Run(1.6f);
        Check.True(h.Abilities.IsStunned(B1), "Heavy Haft: still stunned after 1.6 s");
        h.Run(0.1f);
        Check.False(h.Abilities.IsStunned(B1), "1.65 s in all");
        Check.True(h.UseAt(A1, Throw, B1).IsAccepted, "Twin Axes: a second axe straight away");

        var k = Duel(10f, "warchief_ground_shaker");
        Check.True(k.Use(A1, Leap, Ahead).IsAccepted, "War Leap");
        k.Run(0.85f + 0.95f);
        Check.True(k.Abilities.IsStunned(B1), "Ground Shaker: the knock-up stun lasts 1.15 s");

        var w = Duel(15f, "warchief_iron_grip", "warchief_warlord");
        Check.True(w.UseAt(A1, Throw, B1).IsAccepted, "axe");
        Check.Near(1.25f * 1.4f, w.Last<StatusAppliedEvent>().seconds, "Iron Grip and Warlord: stuns 40% longer", 0.01f);
    }

    [Test]
    static void WarlordStaysInTheFight()
    {
        var h = Duel(2f, "warchief_thick_hide", "warchief_second_skin", "warchief_hard_to_kill");
        h.Tick();
        Check.Near(300f, h.Combat.Damage.MaxHealth(A1), "Thick Hide", 0.01f);
        h.Unit(A1).health = 100f;
        Check.Near(0.9f, h.Stat(A1, StatIds.DamageTaken), "Hard to Kill: 10% less damage at half health", 0.001f);
        Check.True(h.Use(A1, Rage, Ahead).IsAccepted, "Rage");
        h.Run(2f);
        Check.Near(145f, h.Health(A1), "Second Skin: 22.5 health a second", 1f);
    }

    [Test]
    static void UnbrokenRagesThroughAStun()
    {
        var plain = Duel(15f);
        plain.Abilities.ApplyStatus(A1, AbilityLibrary.StunStatus, B1, 1f, 1.5f);
        Check.False(plain.Use(A1, Rage, Ahead).IsAccepted, "without Unbroken a stun stops Rage");

        var h = Duel(15f, "warchief_unbroken");
        h.Abilities.ApplyStatus(A1, AbilityLibrary.StunStatus, B1, 1f, 1.5f);
        h.Abilities.ApplyStatus(A1, AbilityLibrary.SlowStatus, B1, 0.4f, 3f);
        Check.True(h.Use(A1, Rage, Ahead).IsAccepted, "Unbroken: Rage while stunned");
        Check.False(h.Abilities.IsStunned(A1), "clears the stun");
        Check.False(h.Unit(A1).HasStatus(AbilityLibrary.SlowStatus), "and the slow");
        Check.True(h.Unit(A1).HasStatus("warchief_rage"), "and Rage is up");
        Check.True(h.Use(A1, Leap, Ahead).IsAccepted, "free to leap again");
    }
}
