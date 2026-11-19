using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DuckAndCover.Core;

/// <summary>
/// Weapons on the server (WP4): what a shot hits, how much it hurts, ammo, fire rate, reloads,
/// switching, projectiles, shields and lag compensation. These are the server's hit decisions
/// that the network test and the game must agree with.
/// </summary>
static class WeaponTests
{
    const int A1 = CombatHarness.A1, A2 = CombatHarness.A2, B1 = CombatHarness.B1, B2 = CombatHarness.B2;

    /// <summary>No personal shields, so damage reads straight off health.</summary>
    static CombatHarness Bare(Action<Ruleset> tweak = null)
    {
        var r = CombatHarness.QuickRules();
        r.personalShield = 0f;
        tweak?.Invoke(r);
        var h = new CombatHarness(rules: r);
        h.Place(A1, 0f, 0f);
        h.Place(A2, -20f, 0f);
        h.Place(B1, 0f, 10f);
        h.Place(B2, 20f, 10f);
        return h;
    }

    static WeaponDef Def(string id) => StarterWeapons.Library().Weapon(id);

    [Test]
    static void RifleHitsByRegion()
    {
        var rifle = Def("rifle");
        var h = Bare();
        h.Arm(A1, "rifle");
        Check.True(h.FireAt(A1, h.ChestOf(B1)).IsAccepted, "a chest shot is accepted");
        var shot = h.Last<ShotFiredEvent>();
        Check.True(shot != null && shot.hitPlayers[0] == B1 && shot.hitParts[0] == BodyParts.Body, "it hits B1 in the body");
        Check.Near(200f - rifle.damage, h.Health(B1), "body damage");
        var hurt = h.Last<DamagedEvent>();
        Check.True(hurt.attackerId == A1 && hurt.abilityId == "rifle", "credited to the shooter and the weapon");

        h.Run(1f);
        h.FireAt(A1, h.HeadOf(B1));
        Check.Equal(BodyParts.Head, h.Last<ShotFiredEvent>().hitParts[0], "a head shot hits the head");
        Check.Near(200f - rifle.damage - rifle.damage * rifle.headMultiplier, h.Health(B1), "head damage is multiplied");

        h.Run(1f);
        h.FireAt(A1, h.M(B1).position + Vec3.Up * 0.4f);
        Check.Equal(BodyParts.Legs, h.Last<ShotFiredEvent>().hitParts[0], "a low shot hits the legs");
        Check.Near(200f - rifle.damage * (2f + rifle.headMultiplier + rifle.legsMultiplier - 1f), h.Health(B1), "leg damage is reduced");
    }

    [Test]
    static void DamageFallsOffWithDistance()
    {
        var rifle = Def("rifle");
        var h = Bare();
        h.Arm(A1, "rifle");
        h.Place(B1, 0f, 45f);
        h.FireAt(A1, h.ChestOf(B1));
        var shot = h.Last<ShotFiredEvent>();
        float dist = Vec3.Distance(shot.origin, shot.ends[0]);
        Check.True(dist > rifle.falloffStart && dist < rifle.falloffEnd, "the target is in the falloff band (" + dist + ")");
        Check.Near(200f - rifle.damage * rifle.Falloff(dist), h.Health(B1), "damage scaled by distance", 1e-3f);
        Check.Near(1f, rifle.Falloff(1f), "full damage up close");
        Check.Near(rifle.falloffMinShare, rifle.Falloff(1000f), "the minimum far away");
        h.Place(B1, 0f, 200f);
        h.Run(1f);
        h.FireAt(A1, h.ChestOf(B1));
        Check.Equal(-1, h.Last<ShotFiredEvent>().hitPlayers[0], "beyond the range nothing is hit");
    }

    [Test]
    static void WallsStopShots()
    {
        var h = Bare();
        h.Arm(A1, "rifle");
        h.Map.Wall(new Vec3(0f, 1.5f, 5f), new Vec3(4f, 3f, 0.2f));
        h.FireAt(A1, h.ChestOf(B1));
        var shot = h.Last<ShotFiredEvent>();
        Check.Equal(-1, shot.hitPlayers[0], "no player hit through a wall");
        Check.Near(4.9f, shot.ends[0].z, "the shot ends on the wall's face", 1e-3f);
        Check.Near(200f, h.Health(B1), "no damage");
        // Over the wall: the head still shows.
        h.Map.Boxes.Clear();
        h.Map.Wall(new Vec3(0f, 0.6f, 5f), new Vec3(4f, 1.2f, 0.2f));
        h.Run(1f);
        h.FireAt(A1, h.HeadOf(B1));
        Check.Equal(B1, h.Last<ShotFiredEvent>().hitPlayers[0], "a head over low cover can be hit");
    }

    [Test]
    static void TeammatesDontBlockOrTakeDamage()
    {
        var h = Bare();
        h.Arm(A1, "rifle");
        h.Place(A2, 0f, 5f);
        h.FireAt(A1, h.ChestOf(B1));
        Check.Equal(B1, h.Last<ShotFiredEvent>().hitPlayers[0], "the shot passes through the teammate in the way");
        Check.Near(200f, h.Health(A2), "who takes nothing");

        var ff = Bare(r => r.friendlyFire = true);
        ff.Arm(A1, "rifle");
        ff.Place(A2, 0f, 5f);
        ff.FireAt(A1, ff.ChestOf(B1));
        Check.Equal(A2, ff.Last<ShotFiredEvent>().hitPlayers[0], "with friendly fire on, teammates are in the way");
        Check.True(ff.Health(A2) < 200f, "and get hurt");
    }

    [Test]
    static void FireRateAmmoAndReload()
    {
        var rifle = Def("rifle");
        var h = Bare();
        h.Arm(A1, "rifle");
        Check.True(h.FireAt(A1, h.ChestOf(B1)).IsAccepted, "first shot");
        var second = h.FireAt(A1, h.ChestOf(B1));
        Check.False(second.IsAccepted, "a second shot in the same tick is too fast");
        Check.True(second.reason.Contains("faster"), "and says so: " + second.reason);
        h.Run(rifle.fireInterval + 0.01f);
        Check.True(h.FireAt(A1, h.ChestOf(B1)).IsAccepted, "fine after the interval");
        Check.Equal(rifle.magazine - 2, h.W(A1).CurrentAmmo, "two rounds used");

        // Empty the magazine: the last round starts a reload by itself.
        h.Place(B1, 0f, 100f); // out of the way, so nobody dies
        for (int i = 0; i < 100 && h.W(A1).CurrentAmmo > 0; i++) { h.FireAt(A1, h.M(A1).position + new Vec3(0f, 1.6f, 50f)); h.Tick(); }
        Check.Equal(0, h.W(A1).CurrentAmmo, "empty");
        Check.True(h.W(A1).Reloading, "reloading by itself");
        Check.True(h.Last<ReloadStartedEvent>()?.playerId == A1, "everyone is told");
        Check.True(h.FireAt(A1, h.ChestOf(B1)).reason.Contains("reloading"), "can't fire while reloading");
        h.Run(rifle.reloadSeconds + 0.05f);
        Check.Equal(rifle.magazine, h.W(A1).CurrentAmmo, "full again");

        Check.True(h.Combat.Apply(new ReloadCommand { playerId = A1 }).reason.Contains("full"), "no reload when full");
        h.FireAt(A1, h.ChestOf(B1));
        Check.True(h.Combat.Apply(new ReloadCommand { playerId = A1 }).IsAccepted, "a reload when one round down");
    }

    [Test]
    static void MagazineSizeStat()
    {
        var rifle = Def("rifle");
        var h = Bare();
        h.Arm(A1, "rifle", "pistol");
        Check.Equal(rifle.magazine, h.W(A1).CurrentMagazine, "the weapon's own magazine by default");
        h.Combat.Damage.AddModifiers(A1, "test_mag", new[] { new StatModifier(StatIds.MagazineSize, ModOp.Percent, 0.25f) });
        h.Tick();
        int bigger = (int)Math.Round(rifle.magazine * 1.25f);
        Check.Equal(bigger, h.W(A1).CurrentMagazine, "a quarter bigger, shown to clients at once");
        Check.Equal(rifle.magazine, h.W(A1).CurrentAmmo, "rounds in hand stay until a reload");
        Check.Equal((int)Math.Round(Def("pistol").magazine * 1.25f), h.W(A1).magazines[1], "every weapon gets it");
        Check.True(h.Combat.Apply(new ReloadCommand { playerId = A1 }).IsAccepted, "a reload is allowed: not full any more");
        h.Run(rifle.reloadSeconds + 0.05f);
        Check.Equal(bigger, h.W(A1).CurrentAmmo, "the reload fills the bigger magazine");
        h.Combat.Damage.RemoveModifiers(A1, "test_mag");
        h.Combat.Damage.AddModifiers(A1, "test_mag", new[] { new StatModifier(StatIds.MagazineSize, ModOp.Multiply, 0f) });
        Check.Equal(1, h.Combat.Weapons.Magazine(A1, rifle), "never below one round");
    }

    [Test]
    static void LifeStealHealsTheShooter()
    {
        var rifle = Def("rifle");
        var h = Bare();
        h.Arm(A1, "rifle");
        h.Unit(A1).health = 100f;
        h.FireAt(A1, h.ChestOf(B1));
        Check.Near(100f, h.Health(A1), "no life steal by default");
        h.Combat.Damage.AddModifiers(A1, "test_steal", new[] { new StatModifier(StatIds.LifeSteal, ModOp.Add, 0.1f) });
        h.Run(1f);
        h.Forget();
        h.FireAt(A1, h.ChestOf(B1));
        Check.Near(100f + rifle.damage * 0.1f, h.Health(A1), "heals 10% of the damage dealt");
        var heal = h.Last<HealedEvent>();
        Check.True(heal != null && heal.healerId == A1 && heal.targetId == A1, "a heal on the shooter, which earns no support xp");

        // Nothing stolen from a teammate (no damage without friendly fire) or past full health.
        h.Place(A2, 0f, 5f);
        h.Place(B1, 0f, 100f);
        h.Run(1f);
        float before = h.Health(A1);
        h.FireAt(A1, h.ChestOf(A2));
        Check.Near(before, h.Health(A1), "shooting through a teammate heals nothing");
        h.Unit(A1).health = h.Combat.Damage.MaxHealth(A1);
        h.Place(B1, 0f, 10f);
        h.Run(1f);
        h.FireAt(A1, h.ChestOf(B1));
        Check.Near(h.Combat.Damage.MaxHealth(A1), h.Health(A1), "not past full");
    }

    [Test]
    static void WeaponPushAndSlow()
    {
        var h = Bare();
        h.Arm(A1, "rifle");
        h.FireAt(A1, h.ChestOf(B1));
        Check.True(h.Last<PushedEvent>() == null, "no push by default");
        Check.Near(1f, h.Combat.Abilities.MoveSpeed(B1), "no slow by default");

        h.Combat.Damage.AddModifiers(A1, "test_push", new[]
        {
            new StatModifier(StatIds.WeaponPush, ModOp.Add, 0.15f),
            new StatModifier(StatIds.WeaponSlow, ModOp.Add, 0.15f),
        });
        h.Run(1f);
        h.FireAt(A1, h.ChestOf(B1));
        var push = h.Last<PushedEvent>();
        Check.True(push != null && push.targetId == B1 && push.sourceId == A1, "the hit pushes the target");
        Check.Near(0.15f, push.distance, "by the stat's metres");
        Check.Near(1f, push.direction.z, "along the shot, level with the ground", 1e-3f);
        Check.Near(0f, push.direction.y, "never up or down");
        Check.Near(h.R.weaponPushSeconds, push.seconds, "over the ruleset's time");
        Check.Near(0.85f, h.Combat.Abilities.MoveSpeed(B1), "and slows them 15%");
        h.Run(h.R.weaponSlowSeconds + 0.1f);
        Check.Near(1f, h.Combat.Abilities.MoveSpeed(B1), "for the ruleset's seconds");

        // A miss pushes nobody.
        h.Forget();
        h.Place(B1, 0f, 200f);
        h.FireAt(A1, h.M(A1).position + new Vec3(0f, 1.6f, 50f));
        Check.True(h.Last<PushedEvent>() == null, "a miss pushes nothing");
    }

    [Test]
    static void SwitchingWeapons()
    {
        var h = Bare();
        h.Arm(A1, "rifle", "pistol");
        Check.True(h.FireAt(A1, h.ChestOf(B1), weaponId: "pistol").reason.Contains("not in hand"), "only the weapon in hand fires");
        Check.True(h.Combat.Apply(new SwitchWeaponCommand { playerId = A1, slot = 1 }).IsAccepted, "switch to the pistol");
        Check.Equal("pistol", h.W(A1).CurrentId, "pistol in hand");
        Check.True(h.FireAt(A1, h.ChestOf(B1)).reason.Contains("raising"), "it takes a moment to raise");
        h.Run(h.R.weaponSwitchSeconds + 0.05f);
        Check.True(h.FireAt(A1, h.ChestOf(B1)).IsAccepted, "then it fires");
        Check.Near(200f - Def("pistol").damage, h.Health(B1), "with pistol damage");
        Check.False(h.Combat.Apply(new SwitchWeaponCommand { playerId = A1, slot = 5 }).IsAccepted, "no slot 5");
    }

    [Test]
    static void ShotsMustComeFromThePlayer()
    {
        var h = Bare();
        h.Arm(A1, "rifle");
        var r = h.Fire(A1, h.Eye(A1) + new Vec3(0f, 0f, 8f), Vec3.Forward);
        Check.False(r.IsAccepted, "a shot from 8 m in front of the shooter is refused");
        Check.True(r.reason.Contains("too far"), r.reason);
        Check.False(h.Fire(A1, h.Eye(A1), Vec3.Zero).IsAccepted, "a shot needs a direction");
    }

    [Test]
    static void WhoMayFire()
    {
        var h = Bare();
        h.Arm(A1, "rifle");
        h.Unit(A1).statuses.Add(new StatusInstance { statusId = AbilityLibrary.StunStatus, timeLeft = 5f, duration = 5f });
        Check.True(h.FireAt(A1, h.ChestOf(B1)).reason.Contains("stunned"), "stunned players can't fire");
        h.Unit(A1).statuses.Clear();
        h.Match.EliminatePlayer(A1, B1);
        Check.False(h.FireAt(A1, h.ChestOf(B1)).IsAccepted, "the eliminated can't fire");

        var d = new CombatHarness(toPlay: false);
        d.Match.StartMatch();
        while (d.S.phase != MatchPhase.Deploy) d.Tick();
        Check.True(d.FireAt(A1, d.ChestOf(B1)).reason.Contains("play starts"), "weapons are cold during deploy");
    }

    [Test]
    static void ShotgunPelletsAddUp()
    {
        var shotgun = Def("shotgun");
        var h = Bare();
        h.Arm(A1, "shotgun");
        h.Place(B1, 0f, 4f);
        h.FireAt(A1, h.ChestOf(B1));
        var shot = h.Last<ShotFiredEvent>();
        Check.Equal(shotgun.pellets, shot.ends.Length, "one end per pellet");
        int hits = shot.hitPlayers.Count(p => p == B1);
        Check.True(hits >= shotgun.pellets - 2, "up close nearly every pellet hits (" + hits + ")");
        float expected = 0f;
        for (int i = 0; i < shot.ends.Length; i++)
            if (shot.hitPlayers[i] == B1) expected += shotgun.damage * shotgun.Falloff(Vec3.Distance(shot.origin, shot.ends[i])) * shotgun.PartMultiplier(shot.hitParts[i]);
        Check.Near(200f - expected, h.Health(B1), "damage is the sum of the pellets that hit", 1e-3f);
        Check.Equal(1, h.Of<DamagedEvent>().Count, "dealt as one hit");

        var far = Bare();
        far.Arm(A1, "shotgun");
        far.Place(B1, 0f, 30f);
        far.FireAt(A1, far.ChestOf(B1));
        Check.True(far.Last<ShotFiredEvent>().hitPlayers.Count(p => p == B1) <= 3, "far away most pellets miss");
    }

    [Test]
    static void LagCompensationHitsWhatTheShooterSaw()
    {
        var h = Bare();
        h.Arm(A1, "rifle");
        h.Tick();
        int seen = h.S.tick;
        Vec3 aim = h.ChestOf(B1);
        // B1 runs sideways for 0.2 s after the shooter's view.
        for (int i = 1; i <= 4; i++) { h.Move(B1, h.M(B1).position + Vec3.Right * 0.35f); h.Tick(); }
        Check.True(Vec3.Distance(h.ChestOf(B1), aim) > 1f, "B1 is well away from the aim point now");

        Check.True(h.FireAt(A1, aim, tick: seen).IsAccepted, "fired with the tick the shooter saw");
        Check.Equal(B1, h.Last<ShotFiredEvent>().hitPlayers[0], "rewound, the shot hits where B1 was");

        h.Run(0.5f);
        Check.True(h.FireAt(A1, aim).IsAccepted, "fired at the same spot with no rewind");
        Check.Equal(-1, h.Last<ShotFiredEvent>().hitPlayers[0], "misses: B1 isn't there now");

        int old = h.S.tick;
        h.Run(h.R.maxRewindSeconds + 0.5f);
        Check.Equal(h.S.tick - (int)Math.Round(h.R.maxRewindSeconds / CombatHarness.Dt), h.Combat.Weapons.RewindTick(old), "older ticks clamp to the rewind window");
        Check.Equal(h.S.tick, h.Combat.Weapons.RewindTick(h.S.tick + 50), "future ticks are now");
    }

    [Test]
    static void ShieldsTakeDamageFirstAndComeBack()
    {
        var rifle = Def("rifle");
        // Personal shields are off in the default rules (hero shields were taken out); the rules still work when on.
        var r = CombatHarness.QuickRules();
        r.personalShield = 50f;
        var h = new CombatHarness(rules: r);
        h.Place(A1, 0f, 0f);
        h.Place(B1, 0f, 10f);
        h.Arm(A1, "rifle");
        Check.Near(h.R.personalShield, h.Shield(B1), "everyone starts with their personal shield");
        h.FireAt(A1, h.ChestOf(B1));
        Check.Near(200f, h.Health(B1), "the shield takes the hit");
        Check.Near(h.R.personalShield - rifle.damage, h.Shield(B1), "and loses that much");
        for (int i = 0; i < 3; i++) { h.Run(rifle.fireInterval + 0.01f); h.FireAt(A1, h.ChestOf(B1)); }
        Check.Near(200f - (4f * rifle.damage - h.R.personalShield), h.Health(B1), "past the shield, health goes");
        Check.Near(0f, h.Shield(B1), "the shield is broken");

        h.Run(h.R.personalShieldDelaySeconds - 0.5f);
        Check.Near(0f, h.Shield(B1), "it waits for the delay");
        h.Run(0.5f + h.R.personalShield / h.R.personalShieldRegenPerSecond + 0.2f);
        Check.Near(h.R.personalShield, h.Shield(B1), "then grows back to full", 1e-3f);
        Check.True(h.Health(B1) < 200f, "health doesn't regenerate by itself");

        // An ability shield on top goes first.
        h.Combat.Damage.AddShield(B2, B1, 30f, 10f, "test");
        h.Run(0.1f);
        h.FireAt(A1, h.ChestOf(B1));
        Check.Near(30f - rifle.damage, h.Unit(B1).shields.First(s => s.abilityId == "test").amount, "the ability shield takes the hit");
        Check.Near(h.R.personalShield, CombatServer.PersonalShield(h.Unit(B1)).amount, "the personal shield is untouched");
    }

    [Test]
    static void EliminationAndRespawnReset()
    {
        var h = Bare();
        h.Arm(A1, "pistol");
        int shots = 0;
        while (h.S.IsAlive(B1) && shots < 40)
        {
            h.FireAt(A1, h.HeadOf(B1));
            shots++;
            h.Run(0.5f);
        }
        Check.False(h.S.IsAlive(B1), "shot down");
        var down = h.Last<PlayerEliminatedEvent>();
        Check.True(down.playerId == B1 && down.killerId == A1, "credited to the shooter");
        Check.Equal(1, h.S.Player(A1).eliminations, "on the scoreboard");
        int expectedShots = (int)Math.Ceiling(200f / (Def("pistol").damage * Def("pistol").headMultiplier));
        Check.Equal(expectedShots, shots, "pistol head shots needed");
        Check.True(h.FireAt(A1, h.HeadOf(B1)).IsAccepted, "shooting where the body was is allowed");
        Check.Equal(-1, h.Last<ShotFiredEvent>().hitPlayers[0], "but there is no body to hit");

        h.Arm(B1, "rifle", "pistol");
        h.W(B1).ammo[0] = 3;
        h.W(B1).current = 1;
        h.Run(h.Match.Spawn.WaitFor(1) + h.R.respawnWaveSeconds + 0.2f);
        Check.True(h.S.IsAlive(B1), "back");
        Check.Near(200f, h.Health(B1), "full health");
        Check.Equal(Def("rifle").magazine, h.W(B1).ammo[0], "full ammo");
        Check.Equal(0, h.W(B1).current, "first weapon in hand");
    }

    [Test]
    static void GrenadesHitDirectlyAndSplash()
    {
        var gl = Def("launcher");
        var h = Bare();
        h.Map.Wall(new Vec3(0f, -0.5f, 0f), new Vec3(200f, 1f, 200f)); // the floor
        h.Arm(A1, "launcher");
        h.Place(B1, 0f, 8f);
        h.Place(B2, 1.5f, 8.5f);
        Check.True(h.FireAt(A1, h.ChestOf(B1)).IsAccepted, "launched");
        var launched = h.Last<ProjectileLaunchedEvent>();
        Check.True(launched != null && launched.ownerId == A1, "everyone is told it flies");
        Check.Equal(1, h.Combat.Weapons.Projectiles.Count, "one in flight");
        h.Run(1f);
        var boom = h.Last<ProjectileExplodedEvent>();
        Check.True(boom != null && boom.directHit == B1, "a direct hit on B1");
        Check.Equal(0, h.Combat.Weapons.Projectiles.Count, "gone");
        float lostB1 = 200f - h.Health(B1), lostB2 = 200f - h.Health(B2);
        Check.True(lostB1 >= gl.damage + gl.splashDamage * gl.splashMinShare && lostB1 <= gl.damage + gl.splashDamage + 1e-3f, "direct plus splash (" + lostB1 + ")");
        Check.True(lostB2 > 0f && lostB2 < gl.splashDamage, "a neighbour takes some splash (" + lostB2 + ")");
        Check.Near(200f, h.Health(A1), "the shooter is far away");

        // At your own feet: no self damage, and a wall blocks the blast.
        var g = Bare();
        g.Map.Wall(new Vec3(0f, -0.5f, 0f), new Vec3(200f, 1f, 200f));
        g.Map.Wall(new Vec3(0f, 1.5f, 3.5f), new Vec3(6f, 3f, 0.2f));
        g.Arm(A1, "launcher");
        g.Place(B1, 0f, 4.5f); // behind the wall, within the radius
        g.Fire(A1, g.Eye(A1), (new Vec3(0f, 0f, 1.5f) - g.Eye(A1)).Normalized);
        g.Run(1f);
        Check.True(g.Last<ProjectileExplodedEvent>() != null, "it went off at the shooter's feet");
        Check.Near(200f, g.Health(A1), "no self damage");
        Check.Near(200f, g.Health(B1), "nothing through the wall");
    }

    [Test]
    static void UnfiredProjectilesTimeOut()
    {
        var h = Bare();
        h.Arm(A1, "launcher");
        h.Fire(A1, h.Eye(A1), Vec3.Up);
        h.Run(h.R.projectileMaxSeconds + 0.2f);
        Check.Equal(0, h.Combat.Weapons.Projectiles.Count, "removed after its time");
        Check.True(h.Last<ProjectileExplodedEvent>() != null, "with an explosion");
    }

    [Test]
    static void LoadoutsFollowTheHero()
    {
        var h = new CombatHarness(toPlay: false);
        Check.Equal("rifle,pistol", string.Join(",", h.W(A1).weapons), "the default loadout");
        h.Match.Apply(new PickHeroCommand { playerId = A1, heroId = "bulwark" });
        Check.Equal("shotgun,pistol", string.Join(",", h.W(A1).weapons), "the Bulwark carries a shotgun");
        h.Match.Apply(new PickHeroCommand { playerId = B1, heroId = "mender" });
        Check.Equal("mender_sidearm", string.Join(",", h.W(B1).weapons), "the Mender only a healing sidearm");
        Check.False(h.Combat.Weapons.SetLoadout(A1, new[] { "laser" }).IsAccepted, "unknown weapons are refused");
    }

    [Test]
    static void WeaponDataIsChecked()
    {
        Check.Equal(0, StarterWeapons.Library().Validate().Count, "the starter weapons are valid");
        var bad = StarterWeapons.File();
        bad.weapons[0].magazine = 0;
        bad.weapons.Add(bad.weapons[1].Clone());
        bad.loadouts.Add(new WeaponLoadout { heroId = "x", weapons = new[] { "nothing" } });
        var problems = WeaponLibrary.From(bad).Validate();
        Check.True(problems.Any(p => p.Contains("magazine")), "a magazine of 0");
        Check.True(problems.Any(p => p.Contains("twice")), "a weapon defined twice");
        Check.True(problems.Any(p => p.Contains("unknown weapon")), "a loadout naming nothing");
        Check.Equal(0, Ruleset.Default.ValidateCombat().Count, "the default combat numbers are valid");
    }

    static string WeaponsDir => Path.Combine(Repo.Root, "Assets", "DuckAndCover", "Data", "Weapons");

    [Test]
    static void WeaponFileMatchesTheStarterTable()
    {
        string path = Path.Combine(WeaponsDir, "starter_weapons.json");
        var loaded = JsonSerializer.Deserialize<WeaponDataFile>(File.ReadAllText(path),
            new JsonSerializerOptions { IncludeFields = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow });
        var preset = StarterWeapons.File();
        Check.Equal(preset.weapons.Count, loaded.weapons.Count, "same weapons");
        for (int i = 0; i < Math.Min(preset.weapons.Count, loaded.weapons.Count); i++)
            foreach (var f in typeof(WeaponDef).GetFields(BindingFlags.Public | BindingFlags.Instance))
                Check.Equal(f.GetValue(preset.weapons[i])?.ToString(), f.GetValue(loaded.weapons[i])?.ToString(), preset.weapons[i].id + "." + f.Name);
        Check.Equal(string.Join(";", preset.loadouts.Select(l => l.heroId + ":" + string.Join(",", l.weapons))),
            string.Join(";", loaded.loadouts.Select(l => l.heroId + ":" + string.Join(",", l.weapons))), "same loadouts");
        // Every weapon field is in the file, so JsonUtility in Unity misses nothing.
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var w in doc.RootElement.GetProperty("weapons").EnumerateArray())
        {
            var keys = w.EnumerateObject().Select(p => p.Name).OrderBy(n => n);
            var fields = typeof(WeaponDef).GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name).OrderBy(n => n);
            Check.Equal(string.Join(",", fields), string.Join(",", keys), "keys of " + w.GetProperty("id").GetString());
        }
    }
}
