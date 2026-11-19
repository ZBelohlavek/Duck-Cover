using System;
using System.Collections.Generic;
using System.Linq;
using DuckAndCover.Core;
using static HeroHarness;

/// <summary>
/// Bulwark's kit: Rampart (a shield held up in front of him that opens out, keeps its strength between
/// raises and can send damage back), Patch Drone (a repair drone at his shoulder that enemies can shoot
/// down) and Bulkhead (a wall for cover or to cut off a route), plus the held-ability and
/// follow-the-owner rules they are built on.
/// </summary>
static class BulwarkKitTests
{
    const int Rampart = 0, Drone = 1, Bulkhead = 2;

    /// <summary>Bulwark at the origin facing +z on a floor, a teammate Lancer behind him and an enemy Lancer 15 m ahead.</summary>
    static HeroHarness Lineup()
    {
        var h = new HeroHarness(new Dictionary<int, string> { [A1] = "bulwark", [A2] = "lancer", [B1] = "lancer" });
        h.Map.Wall(new Vec3(0f, -0.5f, 0f), new Vec3(200f, 1f, 200f)); // a floor
        h.Place(A1, 0f, 0f);
        h.Place(A2, 0.5f, -2f);
        h.Place(B1, 0f, 15f);
        h.Tick();
        return h;
    }

    static CommandResult Lower(HeroHarness h, int id, int slot) =>
        h.Combat.Apply(new UseAbilityCommand { playerId = id, slot = slot, abilityId = "", lower = true, aimOrigin = h.Eye(id), aimDirection = Ahead, tick = h.S.tick });

    static ObjectState Held(HeroHarness h, string defId) => h.Abilities.State.objects.FirstOrDefault(o => o.defId == defId);

    // ---- Rampart ----

    [Test]
    static void RampartGoesUpInFrontOpensOutAndBlocksEnemyFire()
    {
        var h = Lineup();
        Check.True(h.Use(A1, Rampart, Ahead).IsAccepted, "raise Rampart");
        Check.True(h.Abilities.IsRaised(A1, Rampart), "it is up");
        var shield = Held(h, "bulwark_rampart");
        Check.True(shield != null && shield.def.blocksShots && !shield.def.blocksMovement, "a shield that stops shots, not feet");
        Check.Near(1f, shield.position.z, "standing 1 m in front of him", 0.01f);
        Check.Near(2f, shield.Width, "it appears 2 m wide, at his arm", 0.01f);
        h.Run(0.4f);
        Check.Near(5f, shield.Width, "and opens out to 5 m to cover the team", 0.01f);

        float before = h.Health(A1);
        Check.True(h.FireAt(B1, h.ChestOf(A1)).IsAccepted, "enemy fires");
        Check.Near(before, h.Health(A1), "Rampart took it");
        Check.True(shield.health < 900f, "and lost strength: " + shield.health.ToString("0"));
        float b1 = h.Health(B1);
        Check.True(h.FireAt(A2, h.ChestOf(B1)).IsAccepted, "a teammate fires from behind");
        Check.True(h.Health(B1) < b1, "teammates shoot through it");
        Check.True(h.UseAt(B1, 1, A1).IsAccepted, "enemy Tracer Mark");
        Check.False(h.Unit(A1).HasStatus("lancer_marked"), "enemy ability rays stop on it too");

        var fire = h.FireAt(A1, h.ChestOf(B1));
        Check.False(fire.IsAccepted, "Bulwark can't fire with it up");
        Check.True(fire.reason.Contains("Rampart"), "and is told why: " + fire.reason);
        Check.Near(0.65f, h.Stat(A1, StatIds.MoveSpeed), "and moves 35% slower", 1e-3f);
    }

    [Test]
    static void RampartMovesWithHim()
    {
        var h = Lineup();
        h.Use(A1, Rampart, Ahead);
        var shield = Held(h, "bulwark_rampart");
        h.Place(A1, 4f, 2f);
        h.M(A1).yaw = 90f; // turned to face +x
        h.Tick();
        Check.Near(5f, shield.position.x, "1 m ahead of where he stands now", 0.01f);
        Check.Near(2f, shield.position.z, "beside him, not where he raised it", 0.01f);
        Check.Near(1f, shield.facing.x, "facing where he faces", 0.01f);
    }

    [Test]
    static void RampartKeepsItsStrengthBetweenRaises()
    {
        var h = Lineup();
        h.Use(A1, Rampart, Ahead);
        var shield = Held(h, "bulwark_rampart");
        h.Abilities.DamageObject(shield.id, 450f, B1);
        Check.True(Lower(h, A1, Rampart).IsAccepted, "key released");
        Check.False(h.Abilities.IsRaised(A1, Rampart), "down");
        Check.Equal(0, h.Abilities.State.objects.Count(o => o.defId == "bulwark_rampart"), "the shield is gone");
        Check.True(h.FireAt(A1, h.ChestOf(B1)).IsAccepted, "he can fire again");
        Check.Near(1f, h.Stat(A1, StatIds.MoveSpeed), "at full speed", 1e-3f);
        Check.Near(0.5f, h.Unit(A1).slots[Rampart].reserve, "half its strength kept", 0.01f);
        Check.True(Lower(h, A1, Rampart).IsAccepted && !h.Abilities.IsRaised(A1, Rampart), "a second release does nothing");

        h.Run(1.4f);
        Check.Near(0.5f, h.Unit(A1).slots[Rampart].reserve, "nothing comes back for 1.5 seconds", 0.01f);
        h.Run(1.1f);
        float share = h.Unit(A1).slots[Rampart].reserve;
        Check.True(share > 0.6f && share < 0.75f, "then 180 a second (of 900): " + share.ToString("0.00"));
        Check.True(h.Use(A1, Rampart, Ahead).IsAccepted, "up again");
        Check.Near(900f * share, Held(h, "bulwark_rampart").health, "with the strength it had", 1f);
        Check.True(h.Use(A1, Rampart, Ahead).IsAccepted && !h.Abilities.IsRaised(A1, Rampart), "pressing again also lowers it");
        h.Run(6f);
        Check.Near(1f, h.Unit(A1).slots[Rampart].reserve, "and it fills back up", 1e-3f);
    }

    [Test]
    static void ABrokenRampartWaitsOutItsCooldown()
    {
        var h = Lineup();
        h.Use(A1, Rampart, Ahead);
        h.Abilities.DamageObject(Held(h, "bulwark_rampart").id, 5000f, B1);
        Check.False(h.Abilities.IsRaised(A1, Rampart), "broken, it comes down");
        Check.Equal(0, h.Unit(A1).slots[Rampart].charges, "and needs to recover");
        var again = h.Use(A1, Rampart, Ahead);
        Check.False(again.IsAccepted, "it can't go straight back up");
        Check.True(h.FireAt(A1, h.ChestOf(B1)).IsAccepted, "Bulwark can shoot in the meantime");
        h.Run(5.1f);
        Check.True(h.Use(A1, Rampart, Ahead).IsAccepted, "up after 5 seconds");
        float health = Held(h, "bulwark_rampart").health;
        Check.True(health > 500f && health < 800f, "with what came back while broken (from 1.5 s on): " + health.ToString("0"));
    }

    [Test]
    static void RampartComesDownWhenStunnedOrDown()
    {
        var h = Lineup();
        h.Use(A1, Rampart, Ahead);
        h.Abilities.ApplyStatus(A1, AbilityLibrary.StunStatus, B1, 1f, 0.5f);
        h.Tick();
        Check.False(h.Abilities.IsRaised(A1, Rampart), "a stun knocks it down");
        Check.Equal(0, h.Abilities.State.objects.Count(o => o.defId == "bulwark_rampart"), "and the shield is gone");
        h.Run(0.6f);
        Check.True(h.Use(A1, Rampart, Ahead).IsAccepted, "up again after the stun");
        h.Combat.Damage.Damage(B1, A1, 10000f);
        Check.False(h.Abilities.IsRaised(A1, Rampart), "down with him");
        Check.Equal(0, h.Abilities.State.objects.Count(o => o.defId == "bulwark_rampart"), "no shield left standing");
        Check.Near(1f, h.Stat(A1, StatIds.MoveSpeed), "and the slow is gone", 1e-3f);
    }

    [Test]
    static void DeflectorSendsBlockedDamageBack()
    {
        var h = Lineup();
        Check.True(h.Heroes.ApplyUpgrade(A1, "bulwark", "bulwark_deflector").IsAccepted, "Deflector");
        h.Use(A1, Rampart, Ahead);
        var shield = Held(h, "bulwark_rampart");
        float b1 = h.Health(B1);
        h.FireAt(B1, h.ChestOf(A1));
        float blocked = 900f - shield.health;
        Check.True(blocked > 0f, "the shot hit Rampart");
        Check.Near(b1 - blocked * 0.25f, h.Health(B1), "a quarter of it went back to the shooter", 0.5f);
        Check.True(h.Last<DamagedEvent>().attackerId == A1 && h.Last<DamagedEvent>().objectId == shield.id, "credited to Bulwark, from Rampart");

        var w = Lineup();
        w.Use(A1, Rampart, Ahead);
        float before = w.Health(B1);
        w.FireAt(B1, w.ChestOf(A1));
        Check.Near(before, w.Health(B1), "without Deflector nothing comes back");
    }

    [Test]
    static void GuardianPathMakesRampartStrongerAndFaster()
    {
        var h = Lineup();
        foreach (var id in new[] { "bulwark_dense_field", "bulwark_broad_rampart", "bulwark_mobile_rampart" })
            Check.True(h.Heroes.ApplyUpgrade(A1, "bulwark", id).IsAccepted, id);
        h.Use(A1, Rampart, Ahead);
        var shield = Held(h, "bulwark_rampart");
        Check.Near(1080f, shield.health, "Dense Field: 20% more", 0.5f);
        h.Run(0.4f);
        Check.Near(6.5f, shield.Width, "Broad Rampart: 1.5 m wider", 0.01f);
        Check.Near(3f, shield.def.height, "and taller", 0.01f);
        Check.Near(1f, h.Stat(A1, StatIds.MoveSpeed), "Mobile Rampart: full speed with it up", 1e-3f);
    }

    // ---- Patch Drone ----

    [Test]
    static void PatchDroneHealsHimFromBehind()
    {
        var h = Lineup();
        h.Unit(A1).health = 100f;
        Check.True(h.Use(A1, Drone, Ahead).IsAccepted, "drone out");
        var drone = Held(h, "bulwark_drone");
        Check.True(drone != null && drone.position.z < 0f && drone.position.y > 2f, "hovering behind and above him");
        h.Run(2.05f);
        Check.Near(140f, h.Health(A1), "20 a second", 0.5f);
        float a2 = h.Health(A2) - 50f;
        h.Unit(A2).health = a2;
        h.Run(1f);
        Check.Near(a2, h.Health(A2), "only Bulwark");
        h.Place(A1, 10f, 0f);
        h.Tick();
        Check.Near(10f, drone.position.x, "it follows him", 0.01f);
        h.Run(10f);
        Check.True(Held(h, "bulwark_drone") == null, "gone after 10 seconds");
        Check.False(h.Use(A1, Drone, Ahead).IsAccepted, "cooldown still running at 13 s");
        h.Run(1.2f);
        Check.True(h.Use(A1, Drone, Ahead).IsAccepted, "ready after 14 s");
    }

    [Test]
    static void EnemiesCanShootTheDroneDown()
    {
        var h = Lineup();
        h.Place(B1, 0f, -15f); // behind Bulwark, where the drone hovers
        h.Use(A1, Drone, Ahead);
        var drone = Held(h, "bulwark_drone");
        h.Tick();
        Check.True(h.FireAt(B1, drone.position).IsAccepted, "enemy shoots at the drone");
        Check.True(h.Of<ObjectDamagedEvent>().Any(e => e.objectId == drone.id && e.attackerId == B1), "and hits it");
        for (int i = 0; i < 40 && Held(h, "bulwark_drone") != null; i++)
        {
            h.Run(0.2f);
            h.FireAt(B1, Held(h, "bulwark_drone") == null ? drone.position : Held(h, "bulwark_drone").position);
        }
        Check.True(Held(h, "bulwark_drone") == null, "shot down");
        Check.True(h.Of<ObjectRemovedEvent>().Any(e => e.objectId == drone.id && e.reason == "destroyed" && e.byPlayerId == B1), "by the enemy");

        // A teammate's shots pass it, and it hides nobody from line of sight.
        var t = Lineup();
        t.Place(B1, 0f, -15f);
        t.Place(A2, 0f, 10f);
        t.Use(A1, Drone, Ahead);
        t.Tick();
        var d = Held(t, "bulwark_drone");
        Check.True(t.FireAt(A2, d.position).IsAccepted, "teammate fires through it");
        Check.False(t.Of<ObjectDamagedEvent>().Any(e => e.objectId == d.id), "the drone isn't hit");
        Vec3 behindDrone = d.position + new Vec3(0f, 0f, -3f);
        Check.True(t.Combat.World.HasLineOfSight(behindDrone, A1, t.S.tick), "a drone never blocks line of sight");
    }

    [Test]
    static void BrawlerPathBuildsUpTheDrone()
    {
        var h = Lineup();
        foreach (var id in new[] { "bulwark_spare_parts", "bulwark_armoured_drone", "bulwark_fast_rotors", "bulwark_field_repairs" })
            Check.True(h.Heroes.ApplyUpgrade(A1, "bulwark", id).IsAccepted, id);
        h.Unit(A1).health = 100f;
        h.Unit(A2).health = 100f;
        h.NoShield(A1);
        h.Use(A1, Drone, Ahead);
        var drone = Held(h, "bulwark_drone");
        Check.Near(200f, drone.health, "Armoured Drone: twice the health", 0.01f);
        Check.Near(14f, drone.timeLeft, "and 4 s longer", 0.01f);
        h.Run(1.25f);
        // 13 a pulse (Spare Parts: 30% more), three pulses in 1.25 s at Fast Rotors' 0.4 s instead of two at 0.5 s.
        Check.Near(139f, h.Health(A1), "Spare Parts and Fast Rotors: 13 a pulse, every 0.4 s", 0.5f);
        Check.Near(0f, h.Shield(A1), "and no shield: heroes have none");
        Check.True(h.Health(A2) > 100f, "Field Repairs: the teammate beside him is healed");
    }

    // ---- Bulkhead ----

    static HeroHarness WithBulkhead(string form)
    {
        var h = Lineup();
        h.LevelTo(A1, h.Heroes.Progression.Rules.UltimateFormLevel());
        Check.True(h.PickForm(A1, form).IsAccepted, "pick " + form);
        h.Unit(A1).ultimateCharge = h.R.ultimateChargeMax;
        return h;
    }

    [Test]
    static void BulkheadIsThereFromLevelOne()
    {
        var h = Lineup();
        Check.Equal(1, h.Heroes.Progression.Level(A1), "level 1, no upgrade picked");
        h.Unit(A1).ultimateCharge = h.R.ultimateChargeMax;
        Check.True(h.Use(A1, Bulkhead, new Vec3(0f, 0f, 10f) - h.Eye(A1)).IsAccepted, "Bulkhead goes up once charged");
        Check.True(Held(h, "bulwark_bulkhead") != null, "the wall is out");
        Check.False(h.Use(A1, Bulkhead, Ahead).IsAccepted, "and needs charging again");
    }

    [Test]
    static void BulkheadDropsAWallWhereYouAim()
    {
        var h = WithBulkhead("bulwark_reinforced_bulkhead");
        Vec3 aim = new Vec3(0f, 0f, 10f) - h.Eye(A1);
        Check.True(h.Use(A1, Bulkhead, aim).IsAccepted, "Bulkhead");
        var wall = Held(h, "bulwark_bulkhead");
        Check.True(wall != null && wall.def.blocksMovement && wall.def.blocksShots, "a wall nobody walks through");
        Check.Near(10f, wall.position.z, "where he aimed", 0.3f);
        Check.Near(0f, wall.position.y, "standing on the floor", 0.01f);
        h.Run(0.6f);
        Check.Near(9f, wall.Width, "9 m wide once it has risen", 0.01f);
        Check.Near(4000f, wall.health, "Reinforced Bulkhead: twice the strength", 0.01f);

        float before = h.Health(A1);
        h.FireAt(B1, h.ChestOf(A1));
        Check.Near(before, h.Health(A1), "enemy shots stop on it");
        float b1 = h.Health(B1);
        h.FireAt(A2, h.ChestOf(B1));
        Check.True(h.Health(B1) < b1, "his team shoots through");
        h.Run(11f);
        Check.True(Held(h, "bulwark_bulkhead") != null, "Reinforced: still up at 11.6 s");
        h.Run(1f);
        Check.True(Held(h, "bulwark_bulkhead") == null, "gone after 12 s");

        var w = WithBulkhead("bulwark_wide_bulkhead");
        w.Use(A1, Bulkhead, aim);
        w.Run(0.6f);
        var wide = Held(w, "bulwark_bulkhead");
        Check.Near(13f, wide.Width, "Wide Bulkhead: 4 m wider", 0.01f);
        Check.Near(4.5f, wide.def.height, "and 1 m taller", 0.01f);
    }

    // ---- Shotgun ----

    [Test]
    static void PointBlankKnocksBackOnlyUpClose()
    {
        foreach (float distance in new[] { 3f, 8f })
        {
            var h = Lineup();
            Check.True(h.Heroes.ApplyUpgrade(A1, "bulwark", "bulwark_point_blank").IsAccepted, "Point Blank");
            h.Place(B1, 0f, distance);
            h.Tick();
            h.Forget();
            Check.True(h.FireAt(A1, h.ChestOf(B1)).IsAccepted, "shotgun at " + distance + " m");
            Check.True(h.Of<DamagedEvent>().Any(d => d.targetId == B1), "it hits at " + distance + " m");
            bool pushed = h.Of<PushedEvent>().Any(p => p.targetId == B1 && p.abilityId == StatIds.WeaponPush);
            if (distance <= 4f) Check.True(pushed, "knocked back within 4 m");
            else Check.False(pushed, "no knockback at " + distance + " m");
            Check.False(h.Unit(B1).HasStatus(AbilityLibrary.SlowStatus), "and never slowed");
        }
    }
}
