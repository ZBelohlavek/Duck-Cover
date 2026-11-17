using System.Linq;
using DuckAndCover.Core;
using static AbilityHarness;

/// <summary>Objects that abilities place: mines, fields, turrets and barriers (WP7).</summary>
static class ObjectTests
{
    [Test]
    static void MineArmsTriggersOnceAndIsUsedUp()
    {
        var h = new AbilityHarness();
        h.Give(A1, "test_mine");
        h.Place(A1, 0); h.Place(B1, 9); h.Place(A2, 8, 1);
        h.Use(A1);
        var spawned = h.Last<ObjectSpawnedEvent>();
        Check.Equal("test_mine", spawned.defId, "spawned event");
        Check.Near(8f, spawned.position.x, "placed where it was aimed (8 m range)");
        Check.Equal(TeamId.A, spawned.team, "on the owner's team");
        Check.True(spawned.objectId >= 10000, "object ids start clear of map devices");

        h.Run(0.9f);
        Check.Near(200f, h.Health(B1), "not armed for the first second");
        h.Run(0.2f);
        Check.Near(160f, h.Health(B1), "then it goes off on the enemy standing on it");
        Check.Near(200f, h.Health(A2), "teammates are safe");
        Check.Near(0.5f, h.Abilities.MoveSpeed(B1), "and it slows");
        Check.Equal(spawned.objectId, h.Last<DamagedEvent>().objectId, "the damage names the object");
        Check.Equal(A1, h.Last<DamagedEvent>().attackerId, "and credits its owner");
        Check.Equal("101", string.Join(" ", h.Last<ObjectTriggeredEvent>().targets), "triggered event");
        Check.Equal("triggered", h.Last<ObjectRemovedEvent>().reason, "used up");
        Check.Equal(0, h.Abilities.State.objects.Count, "gone");
        h.Run(3f);
        Check.Near(160f, h.Health(B1), "it only goes off once");
    }

    [Test]
    static void MineWaitsForSomeoneAndIsLimitedPerOwner()
    {
        var h = new AbilityHarness();
        h.Give(A1, "test_mine");
        h.Place(A1, 0);
        h.Use(A1);
        int first = h.Last<ObjectSpawnedEvent>().objectId;
        h.Run(5f);
        Check.Equal(1, h.Abilities.State.objects.Count, "still waiting");
        h.Place(B2, 8, 2);
        h.Run(0.1f);
        Check.Near(160f, h.Health(B2), "triggers when someone walks in");

        h.Run(20f);
        h.Use(A1, 0, new Vec3(0, 0, 1));
        h.Use(A1, 0, new Vec3(0, 0, -1));
        Check.Equal(2, h.Abilities.State.objects.Count, "two out");
        int oldest = h.Abilities.State.objects[0].id;
        h.Run(10f);
        h.Forget();
        h.Use(A1, 0, new Vec3(1, 0, 0));
        Check.Equal(2, h.Abilities.State.objects.Count, "still two: the limit per owner");
        Check.Equal("replaced", h.Last<ObjectRemovedEvent>().reason, "the oldest was replaced");
        Check.Equal(oldest, h.Last<ObjectRemovedEvent>().objectId, "the oldest one");
        Check.True(first != oldest, "ids are never reused");
    }

    [Test]
    static void HealingFieldPulsesAndExpires()
    {
        var h = new AbilityHarness();
        h.Give(A1, "test_dash");
        Check.True(h.Abilities.ApplyParamChanges(A1, new[] { new AbilityParamChange("test_dash", "enable", ModOp.Set, 0f, "healTrail") }).IsAccepted, "talent turns the trail on");
        h.Place(A1, 0); h.Place(A2, 2); h.Place(B1, 1);
        h.Combat.Damage(-1, A1, 100f); h.Combat.Damage(-1, A2, 100f); h.Combat.Damage(-1, B1, 100f);
        h.Use(A1);
        Check.Equal("test_heal_trail", h.Last<ObjectSpawnedEvent>().defId, "the tagged spawn is on");
        h.Run(0.9f);
        Check.Near(100f, h.Health(A2), "first pulse comes after one interval");
        h.Run(0.2f);
        Check.Near(110f, h.Health(A2), "teammate healed");
        Check.Near(110f, h.Health(A1), "owner healed (alliesAndSelf)");
        Check.Near(100f, h.Health(B1), "enemy not healed");
        h.Run(3f);
        Check.Near(130f, h.Health(A2), "three pulses in a 4 s life (the fourth would land as it expires)", 10.1f);
        Check.Equal("expired", h.Last<ObjectRemovedEvent>().reason, "gone after its lifetime");
        float hp = h.Health(A2);
        h.Run(3f);
        Check.Near(hp, h.Health(A2), "no pulses after it expires");
    }

    [Test]
    static void TurretNeedsLineOfSightAndPicksOneTarget()
    {
        var h = new AbilityHarness();
        h.Give(A1, "test_turret");
        h.Place(A1, 0); h.Place(B1, 10); h.Place(B2, 12);
        h.World.Hidden.Add(B1);
        h.Use(A1);
        h.Run(1.6f); // 1 s arming, then a shot every 0.5 s
        Check.Near(200f, h.Health(B1), "can't see B1");
        Check.Near(192f, h.Health(B2), "so it shoots B2");
        h.World.Hidden.Clear();
        h.Run(0.5f);
        Check.Near(192f, h.Health(B1), "now B1 (lowest id) is the one target");
        Check.Near(192f, h.Health(B2), "B2 is left alone");
        h.Place(B1, 100); h.Place(B2, 100);
        h.Forget();
        h.Run(2f);
        Check.Equal(0, h.Of<ObjectTriggeredEvent>().Count, "nobody in range, nothing happens");

        h.Combat.Damage(-1, A1, 9999f);
        Check.Equal(1, h.Abilities.State.objects.Count, "the turret outlives its owner going down");
        h.Match.RemovePlayer(A1);
        h.Run(Dt);
        Check.Equal("ownerLeft", h.Last<ObjectRemovedEvent>().reason, "removed when the owner leaves");
        Check.Equal(0, h.Abilities.State.objects.Count, "but not its owner leaving");
    }

    [Test]
    static void ObjectsCanBeShotAndBlasted()
    {
        var h = new AbilityHarness();
        h.Give(A1, "test_barrier");
        h.Give(B1, "test_bolt", "test_grenade");
        h.Give(A2, "test_bolt");
        h.Place(A1, 0); h.Place(B1, 14); h.Place(A2, -6);
        h.Use(A1);
        var barrier = h.Abilities.State.objects[0];
        Check.Near(4f, barrier.position.x, "barrier 4 m ahead");
        Check.True(h.Last<ObjectSpawnedEvent>().blocksShots, "the Game layer is told it blocks shots");

        h.Use(B1, 0, new Vec3(-1, 0, 0));
        h.Run(0.55f);
        Check.Near(280f, barrier.health, "the enemy's bolt hit the barrier (20 damage)");
        Check.Near(200f, h.Health(A1), "and not the player behind it");
        Check.Equal(B1, h.Last<ObjectDamagedEvent>().attackerId, "object damaged event");

        h.Use(A2);
        h.Run(0.55f);
        Check.Near(280f, barrier.health, "a teammate's shot doesn't hurt it");

        h.Place(A1, 2);
        h.Use(B1, 1, new Vec3(-1, 0, 0)); // grenade lands at the barrier (the ray stops on it)
        h.Run(1.1f);
        Check.Near(220f, barrier.health, "area damage reaches objects too (60)");
        Check.Near(140f, h.Health(A1), "as well as players in the blast");

        Check.Near(220f, h.Abilities.DamageObject(barrier.id, 500f, B1), "weapons can call DamageObject");
        Check.Equal("destroyed", h.Last<ObjectRemovedEvent>().reason, "destroyed at zero");
        Check.Equal(B1, h.Last<ObjectRemovedEvent>().byPlayerId, "by whom");
        Check.Near(0f, h.Abilities.DamageObject(barrier.id, 10f, B1), "gone");
    }

    [Test]
    static void BarrierExpiresAndFieldsCantBeShot()
    {
        var h = new AbilityHarness();
        h.Give(A1, "test_barrier", "test_dash");
        h.Abilities.ApplyParamChanges(A1, new[] { new AbilityParamChange("test_dash", "enable", ModOp.Set, 0f, "healTrail") });
        h.Place(A1, 0);
        h.Use(A1); h.Use(A1, 1);
        var trail = h.Abilities.State.objects.First(o => o.defId == "test_heal_trail");
        Check.Near(0f, h.Abilities.DamageObject(trail.id, 50f, B1), "an object with no health can't be destroyed");
        h.Run(7.9f);
        Check.True(h.Abilities.State.objects.Any(o => o.defId == "test_barrier"), "barrier still up before 8 s");
        h.Run(0.2f);
        Check.Equal(0, h.Abilities.State.objects.Count, "expired");
    }
}
