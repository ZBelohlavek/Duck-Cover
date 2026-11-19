using System;
using System.Linq;
using DuckAndCover.Core;
using static HeroHarness;

/// <summary>
/// Rigger, the stealth gunner: Cloak (stealth that ends when he attacks and opens an ambush window),
/// Combat Stim, Hush Grenade (silence), and his two paths, Assassin (left) and Shutdown (right).
/// Also the stealth rules every hero can use: turrets, minions and Bastions ignore a cloaked enemy,
/// and a reveal beats a cloak.
/// </summary>
static class RiggerKitTests
{
    const int Cloak = 0, Stim = 1, Hush = 2;

    /// <summary>Rigger at the origin and a Bulwark (300 health) 12 m ahead, with no shields.</summary>
    static HeroHarness Duel(params string[] upgrades)
    {
        var h = With(A1, "rigger", B1, "bulwark");
        h.Place(A1, 0f, 0f);
        h.Place(B1, 0f, 12f);
        foreach (var u in upgrades) Check.True(h.Heroes.ApplyUpgrade(A1, "rigger", u).IsAccepted, u);
        h.NoShield(B1);
        h.Forget();
        return h;
    }

    /// <summary>Damage one SMG shot at the Bulwark's chest does now.</summary>
    static float Shot(HeroHarness h)
    {
        h.Run(0.2f); // fire rate
        h.NoShield(B1);
        float before = h.Health(B1);
        Check.True(h.FireAt(A1, h.ChestOf(B1)).IsAccepted, "shot");
        return before - h.Health(B1);
    }

    // ---- The kit ----

    [Test]
    static void TheKitIsCloakStimAndHushAllFromLevelOne()
    {
        var h = Duel();
        var slots = h.Unit(A1).slots.Select(s => s.abilityId).ToArray();
        Check.Equal("rigger_cloak,rigger_stim,rigger_hush", string.Join(",", slots), "E, F and X");
        Check.Equal("smg", h.Combat.Weapons.Weapons(A1).weapons[0], "the SMG in hand");
        Check.False(h.Abilities.AbilityIn(A1, Hush).ultimate, "Hush Grenade runs on a cooldown, not ultimate charge");
        Check.True(h.UseAt(A1, Hush, B1).IsAccepted, "and works at level 1");
    }

    [Test]
    static void CloakHidesRiggerUntilHeFires()
    {
        var h = Duel();
        Check.True(h.Use(A1, Cloak, Ahead).IsAccepted, "cloak");
        Check.True(h.Abilities.IsStealthed(A1), "hidden");
        Check.True(h.Last<StealthChangedEvent>()?.stealthed == true, "clients are told");
        Check.True(h.Combat.Damage.Holds(A1, StatConditions.Stealthed), "the stealthed condition holds");
        h.Run(3f);
        Check.True(h.Abilities.IsStealthed(A1), "still hidden after 3 s");

        float shot = Shot(h);
        Check.True(shot > 0f, "the shot lands");
        Check.False(h.Abilities.IsStealthed(A1), "firing gives him away");
        Check.Equal("fired", h.Of<StatusEndedEvent>().Last(e => e.statusId == "rigger_cloaked").reason, "ended by the shot");
        Check.Near(h.R.ambushSeconds, h.Unit(A1).ambushLeft, "the ambush window opens", 0.06f);
        Check.Near(shot, Shot(h), "with no Ambush upgrade the window changes nothing");

        var h2 = Duel();
        Check.True(h2.Use(A1, Cloak, Ahead).IsAccepted, "cloak again");
        h2.Run(5.1f);
        Check.False(h2.Abilities.IsStealthed(A1), "it runs out after 5 s");
        Check.Near(0f, h2.Unit(A1).ambushLeft, "running out opens no ambush");
    }

    [Test]
    static void StimSpeedsHimUpWithoutBreakingCloak()
    {
        var h = Duel();
        Check.True(h.Use(A1, Cloak, Ahead).IsAccepted, "cloak");
        Check.True(h.Use(A1, Stim, Ahead).IsAccepted, "stim");
        Check.True(h.Abilities.IsStealthed(A1), "a stim only touches him, so the cloak stays");
        Check.Near(1.25f, h.Stat(A1, StatIds.FireRate), "fires 25% faster");
        Check.Near(1.25f, h.Stat(A1, StatIds.MoveSpeed), "moves 25% faster");
        var smg = h.Combat.Weapons.Library.Weapon("smg");
        Check.Near(smg.fireInterval / 1.25f, h.Combat.Weapons.FireInterval(A1, smg), "the SMG's fire interval drops");
        h.Run(5.1f);
        Check.Near(1f, h.Stat(A1, StatIds.FireRate), "for 5 seconds");
        Check.Near(1f, h.Stat(A1, StatIds.MoveSpeed), "speed back too");
    }

    [Test]
    static void HushGrenadeSilencesButLetsThemShoot()
    {
        var h = Duel();
        h.Place(B2, 1.5f, 12f); // a second enemy beside the first
        Check.True(h.Use(A1, Cloak, Ahead).IsAccepted, "cloak");
        Check.True(h.UseAt(A1, Hush, B1).IsAccepted, "throw");
        Check.False(h.Abilities.IsStealthed(A1), "throwing gives him away");
        Check.Near(300f - 15f, h.Health(B1), "15 damage");
        Check.True(h.Abilities.IsSilenced(B1) && h.Abilities.IsSilenced(B2), "both enemies within 3 m are silenced");
        var refused = h.Use(B1, 0, Ahead);
        Check.False(refused.IsAccepted, "a silenced hero can't use abilities");
        Check.Equal("silenced", refused.reason, "and is told why");
        Check.True(h.FireAt(B1, h.ChestOf(A1)).IsAccepted, "but can still shoot");
        h.Run(2.6f);
        Check.False(h.Abilities.IsSilenced(B1), "for 2.5 seconds");
        Check.True(h.Use(B1, 0, Ahead).IsAccepted, "then abilities work again");
        Check.False(h.Use(A1, Hush, Ahead).IsAccepted, "Hush Grenade is on its cooldown");
    }

    // ---- Stealth against everyone else ----

    [Test]
    static void TurretsDontPickACloakedEnemy()
    {
        var h = Duel();
        var turret = new ObjectDef { id = "test_turret", lifetime = 30f, health = 100f, radius = 14f, trigger = Triggers.Pulse, interval = 0.4f,
            affects = Affects.Enemies, maxTargets = 1, effects = new[] { new EffectDef { kind = EffectKinds.Damage, amount = 10f } } };
        h.Abilities.SpawnObject(turret, B1, new Vec3(0f, 0f, 6f), "", Vec3.Forward);
        Check.True(h.Use(A1, Cloak, Ahead).IsAccepted, "cloak");
        h.Run(2f);
        Check.Near(200f, h.Health(A1), "the turret doesn't see him");
        h.Run(3.5f); // the cloak runs out
        Check.True(h.Health(A1) < 200f, "then it does");
    }

    [Test]
    static void MinionsDontShootACloakedHero()
    {
        var h = new LaneHarness(waves: false);
        int id = h.L.Waves.SpawnMinion(LaneRuleset.Scrapper, TeamId.A, 0);
        Action stand = () => h.H.Place(CombatHarness.B1, 0f, -85f); // in front of the minion, in reach
        h.H.Combat.Damage.Unit(CombatHarness.B1).stealthed = true;
        h.Run(3f, stand);
        Check.Equal(0, h.H.Of<DamagedEvent>().Count(d => d.attackerId == id), "a cloaked hero isn't a target");
        h.H.Combat.Damage.Unit(CombatHarness.B1).stealthed = false;
        h.Run(3f, stand);
        Check.True(h.H.Of<DamagedEvent>().Any(d => d.attackerId == id && d.targetId == CombatHarness.B1), "a visible one is");
    }

    [Test]
    static void ARevealBeatsTheCloak()
    {
        var h = With(A1, "rigger", B1, "lancer");
        h.Place(A1, 0f, 0f);
        h.Place(B1, 0f, 12f);
        Check.True(h.Use(A1, Cloak, Ahead).IsAccepted, "cloak");
        Check.True(h.UseAt(B1, 1, A1).IsAccepted, "Lancer's Tracer Mark finds him");
        Check.True(h.Abilities.IsRevealed(A1), "revealed");
        Check.False(h.Abilities.IsStealthed(A1), "so not hidden");
        Check.True(h.Unit(A1).HasStatus("rigger_cloaked"), "the cloak is still running underneath");
        Check.True(h.Abilities.RemoveStatus(A1, "lancer_marked"), "mark taken off");
        Check.True(h.Abilities.IsStealthed(A1), "hidden again");
    }

    [Test]
    static void SilenceStopsACloakButNotAStimmedGun()
    {
        var h = With(A1, "rigger", B1, "rigger");
        h.Place(A1, 0f, 0f);
        h.Place(B1, 0f, 12f);
        Check.True(h.UseAt(B1, Hush, A1).IsAccepted, "the enemy Rigger throws first");
        Check.False(h.Use(A1, Cloak, Ahead).IsAccepted, "a silenced Rigger can't cloak");
        Check.True(h.FireAt(A1, h.ChestOf(B1)).IsAccepted, "but he can shoot back");
    }

    // ---- Assassin (left) ----

    [Test]
    static void AmbushHitsHarderOutOfCloak()
    {
        var h = Duel("rigger_ambush");
        float plain = Shot(h);
        Check.True(h.Use(A1, Cloak, Ahead).IsAccepted, "cloak");
        h.Run(0.5f);
        Check.Near(plain * 1.2f, Shot(h), "the shot that breaks the cloak already counts", 0.01f);
        Check.True(h.Last<AmbushEvent>() != null, "the ambush is announced");
        Check.Near(plain * 1.2f, Shot(h), "and the next ones in the window", 0.01f);
        h.Run(2.6f);
        Check.Near(plain, Shot(h), "back to normal after 2.5 s", 0.01f);
    }

    [Test]
    static void TheAssassinPathStacksTheAmbush()
    {
        var h = Duel("rigger_ambush", "rigger_killing_window", "rigger_phantom");
        float plain = Shot(h);
        Check.True(h.Use(A1, Cloak, Ahead).IsAccepted, "cloak");
        h.Unit(A1).health = 100f;
        float ambush = Shot(h);
        Check.Near(plain * 1.6f, ambush, "Ambush, Killing Window and Phantom: 60% more", 0.01f);
        Check.Near(100f + ambush * 0.15f, h.Health(A1), "Phantom heals 15% of it", 0.01f);
        Check.Near(h.R.ambushSeconds + 1.5f, h.Unit(A1).ambushLeft, "Killing Window: 1.5 s longer", 0.06f);
    }

    [Test]
    static void AssassinTalentsReshapeTheCloak()
    {
        var h = Duel("rigger_twin_fades", "rigger_smoke_stim", "rigger_light_feet");
        Check.Near(1.06f, h.Stat(A1, StatIds.MoveSpeed), "Light Feet: 6% faster");
        Check.True(h.Use(A1, Stim, Ahead).IsAccepted, "stim");
        Check.True(h.Abilities.IsStealthed(A1), "Smoke Stim cloaks him");
        Check.Near(1.25f + 0.06f + 0.1f, h.Stat(A1, StatIds.MoveSpeed), "Light Feet adds 10% while cloaked", 0.001f);
        h.Run(2.1f);
        Check.False(h.Abilities.IsStealthed(A1), "for 2 seconds");
        Check.True(h.Use(A1, Cloak, Ahead).IsAccepted, "first cloak charge");
        Check.True(h.Use(A1, Cloak, Ahead).IsAccepted, "Twin Fades: a second charge");
        h.Run(6.9f);
        Check.True(h.Abilities.IsStealthed(A1), "Twin Fades: 7 seconds");
        h.Run(0.2f);
        Check.False(h.Abilities.IsStealthed(A1), "then it ends");
    }

    // ---- Shutdown (right) ----

    [Test]
    static void HamperWeakensWhoeverHushHits()
    {
        var h = Duel("rigger_hamper");
        Check.True(h.UseAt(A1, Hush, B1).IsAccepted, "throw");
        Check.True(h.Unit(B1).HasStatus("rigger_hampered"), "hampered");
        Check.Near(0.8f, h.Stat(B1, StatIds.DamageDealt), "deals 20% less");
        Check.Near(0.75f, h.Stat(B1, StatIds.HealingDone), "heals 25% less");
        float before = h.Health(A1);
        h.Run(0.2f);
        Check.True(h.FireAt(B1, h.ChestOf(A1)).IsAccepted, "the Bulwark shoots back");
        var hit = h.Last<DamagedEvent>();
        Check.True(hit != null && hit.targetId == A1, "and hits");
        h.Run(5f);
        Check.False(h.Unit(B1).HasStatus("rigger_hampered"), "for 5 seconds");
    }

    [Test]
    static void TheShutdownPathLocksTheCarryDown()
    {
        var h = Duel("rigger_deep_hush", "rigger_tracking_hush", "rigger_blackout", "rigger_double_hush");
        Check.True(h.UseAt(A1, Hush, B1).IsAccepted, "first grenade");
        Check.True(h.Abilities.IsRevealed(B1), "Tracking Hush: shown through walls");
        Check.Near(1.15f, h.Stat(B1, StatIds.DamageTaken), "Blackout: takes 15% more while silenced");
        Check.True(h.UseAt(A1, Hush, B1).IsAccepted, "Double Hush: a second grenade");
        h.Run(4.4f);
        Check.True(h.Abilities.IsSilenced(B1), "Deep Hush and Blackout: silenced for 4.5 s");
        h.Run(0.2f);
        Check.False(h.Abilities.IsSilenced(B1), "then free");
        Check.Near(1f, h.Stat(B1, StatIds.DamageTaken), "and no longer exposed");
    }

    [Test]
    static void TrackingHushUncloaksAnEnemyRigger()
    {
        var h = With(A1, "rigger", B1, "rigger");
        h.Place(A1, 0f, 0f);
        h.Place(B1, 0f, 12f);
        Check.True(h.Heroes.ApplyUpgrade(A1, "rigger", "rigger_tracking_hush").IsAccepted, "Tracking Hush");
        Check.True(h.Use(B1, Cloak, Ahead).IsAccepted, "the enemy cloaks");
        Check.True(h.UseAt(A1, Hush, B1).IsAccepted, "a grenade where he was seen last");
        Check.False(h.Abilities.IsStealthed(B1), "tracked, so visible");
    }
}
