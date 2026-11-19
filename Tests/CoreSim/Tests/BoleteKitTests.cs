using System;
using System.Linq;
using DuckAndCover.Core;
using static HeroHarness;

/// <summary>
/// Bolete, the fungal healer: lobbed Spore Bombs, Bloom Cap (a healing mushroom), Rot Spore (a delayed burst
/// of poison and Wilt), Frenzy Cap (a buff grown on a teammate), and his two paths, Overgrowth (left: bigger,
/// longer, shared) and Symbiosis (right: he gains from his own fungus).
/// </summary>
static class BoleteKitTests
{
    const int Bloom = 0, Rot = 1, Frenzy = 2;

    /// <summary>Bolete at the origin on a floor, a teammate 6 m ahead and to the left, Bulwark (300 health) 12 m ahead.</summary>
    static HeroHarness Field(params string[] upgrades)
    {
        var h = With(A1, "bolete", B1, "bulwark");
        h.Map.Wall(new Vec3(0f, -0.5f, 0f), new Vec3(400f, 1f, 400f));
        h.Place(A1, 0f, 0f);
        h.Place(A2, -3f, 6f);
        h.Place(B1, 0f, 12f);
        foreach (var u in upgrades) Check.True(h.Heroes.ApplyUpgrade(A1, "bolete", u).IsAccepted, u);
        h.NoShield(B1);
        h.Tick();
        h.Forget();
        return h;
    }

    static void Hurt(HeroHarness h, int id, float amount) => h.Combat.Damage.Damage(B2, id, amount, "setup");

    // ---- The kit ----

    [Test]
    static void TheKitIsBloomRotAndFrenzyWithSporeBombs()
    {
        var h = Field();
        var slots = h.Unit(A1).slots.Select(s => s.abilityId).ToArray();
        Check.Equal("bolete_bloom,bolete_rot,bolete_frenzy", string.Join(",", slots), "E, F and X");
        Check.Equal("spore_bombs", h.Combat.Weapons.Weapons(A1).weapons[0], "spore bombs in hand");
        Check.Equal(1, h.Combat.Weapons.Weapons(A1).weapons.Length, "and nothing else");
        Check.True(h.Abilities.AbilityIn(A1, Frenzy).ultimate, "Frenzy Cap runs on ultimate charge");
        Check.Near(200f, h.Combat.Damage.MaxHealth(A1), "200 health");
        Check.Equal("support", ProgressionHarness.DataCatalog().Get("bolete").role, "the healer pick");
    }

    [Test]
    static void SporeBombsAreLobbedAndBurstOnEnemiesOnly()
    {
        var h = Field();
        var w = h.Combat.Weapons.Library.Weapon("spore_bombs");
        Check.Equal(WeaponKind.Projectile, w.kind, "a thrown projectile");
        Check.True(w.projectileGravity > 0f, "that falls");
        var aim = w.AimAt(h.Eye(A1), h.ChestOf(B1));
        Check.True(aim.y > (h.ChestOf(B1) - h.Eye(A1)).Normalized.y + 0.01f, "so it is thrown above the line to the target");

        // A second enemy 1.5 m beside the first, and a teammate beside him too.
        h.Place(B2, 1.5f, 12f);
        h.Place(A2, -1.5f, 12f);
        h.NoShield(B2);
        Hurt(h, A2, 50f);
        float a2 = h.Health(A2);
        h.Tick();
        h.Forget();
        Check.True(h.FireAt(A1, h.ChestOf(B1)).IsAccepted, "throw");
        h.Run(1.5f);
        var hit = h.Of<ProjectileExplodedEvent>().Single();
        Check.Equal(B1, hit.directHit, "it lands on the Bulwark");
        float direct = 300f - h.Health(B1), splash = h.Health(B2) <= 0f ? 0f : h.Combat.Damage.MaxHealth(B2) - h.Health(B2);
        Check.Near(w.damage + w.splashDamage, direct, "a direct hit takes the bomb and its burst", 3f);
        Check.True(splash > w.splashDamage * w.splashMinShare - 0.01f && splash < w.splashDamage, "the enemy beside him takes part of the burst: " + splash);
        Check.Near(a2, h.Health(A2), "and the teammate nothing: bombs never heal or hurt teammates");
    }

    [Test]
    static void BloomCapHealsTeammatesAndHimNotEnemies()
    {
        var h = Field();
        h.Place(B2, -1.5f, 6f);
        h.Place(A1, -3f, 3f);
        Hurt(h, A2, 100f);
        Hurt(h, A1, 100f);
        h.Combat.Damage.Damage(A1, B2, 100f, "setup");
        h.Tick();
        h.Forget();
        float a2 = h.Health(A2), me = h.Health(A1), enemy = h.Health(B2);
        Check.True(h.UseAt(A1, Bloom, A2).IsAccepted, "plant Bloom Cap at the teammate");
        Check.True(h.Last<ObjectSpawnedEvent>()?.defId == "bolete_bloom", "the mushroom grows");
        h.Run(3f);
        Check.Near(a2 + 18f * 3f, h.Health(A2), "teammate heals 18 a second", 9.5f);
        Check.Near(me + 18f * 3f, h.Health(A1), "Bolete in reach heals too", 9.5f);
        Check.Near(enemy, h.Health(B2), "the enemy beside it doesn't");
        Check.False(h.Of<DamagedEvent>().Any(e => e.attackerId == A1), "and nobody is hurt by it");
        h.Run(4.5f);
        Check.False(h.Abilities.State.objects.Any(o => o.defId == "bolete_bloom"), "it withers after 6 s");
        float after = h.Health(A2);
        h.Run(2f);
        Check.Near(after, h.Health(A2), "and its healing stops a second later");
    }

    [Test]
    static void RotSporeBurstsAfterASecondPoisoningAndWilting()
    {
        var h = Field();
        float hp = h.Health(B1);
        Check.True(h.UseAt(A1, Rot, B1).IsAccepted, "throw Rot Spore");
        h.Run(0.9f);
        Check.True(h.Unit(B1).Status("bolete_poison") == null, "nothing until it bursts");
        Check.Near(1f, h.Stat(B1, StatIds.DamageDealt), "full damage until then");
        h.Run(0.2f);
        Check.True(h.Unit(B1).Status("bolete_poison") != null, "poisoned once it bursts");
        Check.Near(0.8f, h.Stat(B1, StatIds.DamageDealt), "and Wilted: 20% less damage");
        h.Run(5f);
        Check.Near(hp - 50f, h.Health(B1), "10 poison a second for 5 seconds", 5.5f);
        Check.True(h.Unit(B1).Status("bolete_poison") == null, "then it wears off");
        Check.Near(1f, h.Stat(B1, StatIds.DamageDealt), "and so does Wilt");

        var p = Field();
        Check.True(p.UseAt(A1, Rot, B1).IsAccepted, "again");
        p.Run(1.2f);
        Check.Equal(2, p.Abilities.Cleanse(B1), "both are harmful effects, so a cleanse (Mender's Purge) clears them");
        Check.True(p.Abilities.Library.Status("bolete_poison").negative && p.Abilities.Library.Status("bolete_wilt").negative, "marked harmful");
    }

    [Test]
    static void FrenzyCapGrowsOnATeammate()
    {
        var h = Field();
        h.Unit(A1).ultimateCharge = 100f;
        Check.Equal("no target in reach", h.Use(A1, Frenzy, Vec3.Up).reason, "aimed at nobody, it waits");
        Check.Near(100f, h.Unit(A1).ultimateCharge, "and keeps its charge");
        Check.Equal("no target in reach", h.UseAt(A1, Frenzy, B1).reason, "an enemy is no target either");
        Check.True(h.UseAt(A1, Frenzy, A2).IsAccepted, "grow it on the teammate");
        Check.Near(1.25f, h.Stat(A2, StatIds.FireRate), "they fire 25% faster");
        Check.Near(1.15f, h.Stat(A2, StatIds.DamageDealt), "and deal 15% more damage");
        Check.Near(1f, h.Stat(A2, StatIds.MoveSpeed), "no extra speed without Wild Frenzy");
        Check.Near(1f, h.Stat(A1, StatIds.FireRate), "Bolete himself gets nothing without Kindred Frenzy");
        h.Run(6.1f);
        Check.Near(1f, h.Stat(A2, StatIds.FireRate), "it lasts 6 seconds");
    }

    // ---- Left: Overgrowth ----

    [Test]
    static void WildAndSharedFrenzyReachFurther()
    {
        var h = Field("bolete_wild_frenzy", "bolete_shared_frenzy");
        // A third teammate 3 m from the one aimed at.
        h.Place(A1, 0f, 0f);
        h.Unit(A1).ultimateCharge = 100f;
        Check.True(h.UseAt(A1, Frenzy, A2).IsAccepted, "Frenzy Cap");
        Check.Near(1.2f, h.Stat(A2, StatIds.MoveSpeed), "Wild Frenzy: 20% faster");

        var g = new HeroHarness(new System.Collections.Generic.Dictionary<int, string> { [A1] = "bolete" }, perTeam: 3);
        foreach (var u in new[] { "bolete_shared_frenzy" }) Check.True(g.Heroes.ApplyUpgrade(A1, "bolete", u).IsAccepted, u);
        g.Place(A1, 0f, 0f);
        g.Place(A2, 0f, 8f);
        g.Place(3, 3f, 8f);
        g.Tick();
        g.Unit(A1).ultimateCharge = 100f;
        Check.True(g.UseAt(A1, Frenzy, A2).IsAccepted, "Shared Frenzy");
        Check.Near(1.25f, g.Stat(A2, StatIds.FireRate), "the teammate aimed at");
        Check.Near(1.25f, g.Stat(3, StatIds.FireRate), "and the one 3 m from them");
        Check.Near(1f, g.Stat(A1, StatIds.FireRate), "not Bolete (that is the other path)");
    }

    [Test]
    static void ClingingSporesCarryTheHealAway()
    {
        var plain = Field();
        var cling = Field("bolete_clinging_spores");
        foreach (var h in new[] { plain, cling })
        {
            Hurt(h, A2, 100f);
            Check.True(h.UseAt(A1, Bloom, A2).IsAccepted, "Bloom Cap");
            h.Run(1f);
            h.Place(A2, 0f, 20f); // steps out
            h.Tick();
        }
        float p0 = plain.Health(A2), c0 = cling.Health(A2);
        plain.Run(3f);
        cling.Run(3f);
        Check.True(plain.Health(A2) - p0 < 20f, "without it the healing stops a second after stepping out");
        Check.Near(c0 + 54f, cling.Health(A2), "with it they keep healing 18 a second after leaving", 10f);
    }

    [Test]
    static void BigBombsAndCreepingRotWiden()
    {
        var h = Field("bolete_big_bombs");
        h.Place(B2, 3f, 12f); // outside the 2.5 m burst, inside 3.25 m
        h.NoShield(B2);
        h.Tick();
        float before = h.Health(B2);
        Check.True(h.FireAt(A1, h.ChestOf(B1)).IsAccepted, "throw");
        h.Run(1.5f);
        Check.Near(3.25f, h.Of<ProjectileExplodedEvent>().Single().splashRadius, "Big Bombs: 30% wider burst");
        Check.True(h.Health(B2) < before, "reaching the enemy 3 m from the blast");

        var r = Field("bolete_creeping_rot");
        r.Place(B1, 0f, 12f);
        Check.True(r.UseAt(A1, Rot, B1).IsAccepted, "Rot Spore");
        r.Run(1.1f);
        Check.Near(8f, r.Unit(B1).Status("bolete_poison").timeLeft, "Creeping Rot: poison lasts 8 s", 0.15f);
        Check.Near(4.5f, r.Abilities.AbilityIn(A1, Rot).targeting.radius, "and reaches 4.5 m");
    }

    // ---- Right: Symbiosis ----

    [Test]
    static void KindredFrenzyAlsoGrowsOnHim()
    {
        var h = Field("bolete_kindred_frenzy");
        h.Unit(A1).ultimateCharge = 100f;
        Check.True(h.UseAt(A1, Frenzy, A2).IsAccepted, "Frenzy Cap");
        Check.Near(1.25f, h.Stat(A2, StatIds.FireRate), "the teammate");
        Check.Near(1.25f, h.Stat(A1, StatIds.FireRate), "and Bolete");
    }

    [Test]
    static void SharedRootsHealsHimWhenHeHealsATeammate()
    {
        var h = Field("bolete_shared_roots");
        h.Place(A2, -3f, 10f); // Bolete stays out of Bloom Cap's reach
        Hurt(h, A2, 100f);
        Hurt(h, A1, 100f);
        h.Tick();
        float me = h.Health(A1);
        Check.True(h.UseAt(A1, Bloom, A2).IsAccepted, "Bloom Cap on the teammate");
        h.Run(2f);
        float given = h.Of<HealedEvent>().Where(e => e.healerId == A1 && e.targetId == A2).Sum(e => e.amount);
        Check.True(given > 20f, "the teammate heals: " + given);
        Check.Near(me + given * 0.3f, h.Health(A1), "Bolete heals 30% as much", 0.5f);
        Check.True(h.Of<HealedEvent>().Where(e => e.targetId == A1).All(e => e.abilityId == StatIds.HealShare), "all of it through Shared Roots");

        // His own heal doesn't echo, and healing a minion doesn't either.
        var m = Field("bolete_shared_roots", "bolete_mycelial_link");
        Check.Near(0.6f, m.Stat(A1, StatIds.HealShare), "Mycelial Link doubles it to 60%");
    }

    [Test]
    static void LeechingRotStealsFromEachEnemyHero()
    {
        var h = Field("bolete_leeching_rot");
        h.Place(B2, 1f, 12f);
        h.Tick();
        Check.True(h.UseAt(A1, Rot, B1).IsAccepted, "Rot Spore on two enemy heroes");
        h.Run(0.5f);
        Check.Near(1f, h.Stat(A1, StatIds.DamageDealt), "nothing before it bursts");
        h.Run(0.6f);
        Check.Equal(2, h.Unit(A1).Status("bolete_leech").stacks, "a stack for each enemy hero");
        Check.Near(1.12f, h.Stat(A1, StatIds.DamageDealt), "6% more damage each");
        Check.Near(1.10f, h.Stat(A1, StatIds.MoveSpeed), "5% more speed each");

        // Minions don't feed it.
        var u = Field("bolete_leeching_rot");
        u.Place(B1, 0f, 40f);
        u.Combat.Units.Spawn(UnitLibrary.TrainingDrone, TeamId.B, new Vec3(0f, 0f, 12f));
        u.Tick();
        Check.True(u.Use(A1, Rot, new Vec3(0f, 0f, 12f) + Vec3.Up * 0.5f - u.Eye(A1)).IsAccepted, "Rot Spore on a minion");
        u.Run(1.1f);
        Check.True(u.Unit(A1).Status("bolete_leech") == null, "no stack from a minion");
    }

    [Test]
    static void FeedingRotAndHeartOfTheBloomKeepHimAlive()
    {
        var h = Field("bolete_feeding_rot");
        Hurt(h, A1, 100f);
        h.Tick();
        float me = h.Health(A1), them = h.Health(B1);
        Check.True(h.UseAt(A1, Rot, B1).IsAccepted, "Rot Spore");
        h.Run(4f);
        Check.Near((them - h.Health(B1)) * 0.5f, h.Health(A1) - me, "Feeding Rot: his poison heals him half its damage", 0.5f);

        var b = Field("bolete_heart_of_bloom");
        b.Place(A1, -3f, 4f); // 2 m from the teammate where the mushroom grows
        b.Tick();
        Check.True(b.UseAt(A1, Bloom, A2).IsAccepted, "Bloom Cap beside him");
        b.Run(0.6f);
        Check.Near(0.85f, b.Stat(A1, StatIds.DamageTaken), "Heart of the Bloom: 15% less damage in it");
        Check.Near(1f, b.Stat(A2, StatIds.DamageTaken), "only for Bolete");
        b.Place(A1, 0f, -10f);
        b.Run(1.5f);
        Check.Near(1f, b.Stat(A1, StatIds.DamageTaken), "and only while he stands in it");
    }
}
