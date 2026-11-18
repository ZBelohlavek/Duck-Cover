using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DuckAndCover.Core;

/// <summary>
/// Units that are not players (WP14): they take weapons, abilities and statuses exactly as heroes
/// do, under unit ids from <see cref="UnitIds.First"/> up, and every starter kit works on them.
/// </summary>
static class UnitTests
{
    const string Drone = UnitLibrary.TrainingDrone;
    const int A1 = CombatHarness.A1, A2 = CombatHarness.A2, B1 = CombatHarness.B1;

    static CombatHarness Harness()
    {
        var r = CombatHarness.QuickRules();
        r.personalShield = 0f; // heroes and units compare health straight
        var h = new CombatHarness(2, r);
        h.Place(A1, 0f, 0f);
        h.Place(A2, -3f, 0f);
        h.Place(B1, 40f, 40f);
        h.Place(CombatHarness.B2, 44f, 40f);
        return h;
    }

    // ---- Being a unit ----

    [Test]
    static void SpawnedUnitsHaveIdsHealthAndATeam()
    {
        var h = Harness();
        int id = h.Combat.Units.Spawn(Drone, TeamId.B, new Vec3(0f, 0f, 10f), 180f);
        Check.True(UnitIds.IsUnit(id) && id == UnitIds.First, "first unit id: " + id);
        Check.False(UnitIds.IsUnit(CombatServer.FirstDummyId + 50), "dummies are players, not units");
        var e = h.Last<UnitSpawnedEvent>();
        Check.True(e != null && e.unitId == id && e.team == TeamId.B && e.typeId == Drone, "spawn event");
        Check.Near(200f, h.Health(id), "kind's health");
        Check.True(h.Combat.Damage.IsAlive(id), "alive");
        Check.Equal(TeamId.B, h.Combat.Damage.TeamOf(id), "team");
        Check.Equal(-1, h.Combat.Units.Spawn("no_such_kind", TeamId.A, Vec3.Zero), "unknown kind");
        Check.Equal(id + 1, h.Combat.Units.Spawn(Drone, TeamId.None, Vec3.Zero), "ids count up");
        Check.Equal(TeamId.None, h.Combat.Damage.TeamOf(id + 1), "neutral");

        var lib = UnitLibrary.From(new UnitDataFile { units = new[] { new UnitDef { id = "x", maxHealth = 0f, size = -1f } } });
        Check.True(lib.Validate().Count >= 2, "bad kinds are caught: " + string.Join("; ", lib.Validate()));
    }

    [Test]
    static void DestroyedUnitsLeaveCleanly()
    {
        var h = Harness();
        h.Arm(A1, "rifle");
        int id = h.Combat.Units.Spawn(Drone, TeamId.B, new Vec3(0f, 0f, 10f));
        h.Combat.Abilities.ApplyStatus(id, AbilityLibrary.SlowStatus, A1, 0.5f, 5f);
        h.Forget();
        h.Combat.Damage.Damage(A1, id, 500f, "test");
        var gone = h.Last<UnitDestroyedEvent>();
        Check.True(gone != null && gone.unitId == id && gone.killerId == A1 && gone.reason == "killed", "destroyed by A1");
        Check.True(h.Last<DamagedEvent>().eliminated, "the killing blow says so");
        Check.Equal(0, h.Of<PlayerEliminatedEvent>().Count, "no player was eliminated");
        Check.True(h.Of<StatusEndedEvent>().Any(s => s.targetId == id && s.reason == "eliminated"), "its statuses end as a hero's do");
        Check.False(h.Combat.Damage.IsAlive(id), "dead");
        Check.True(h.Combat.Damage.Unit(id) == null, "its fighting state is gone");
        Check.Near(0f, h.Combat.Damage.Damage(A1, id, 10f), "can't be hurt again");
        Check.False(h.Combat.Abilities.ApplyStatus(id, AbilityLibrary.SlowStatus, A1, 0.5f, 5f), "nor slowed");

        h.Forget();
        h.Tick();
        Check.Equal(0, h.FireAt(A1, new Vec3(0f, 1.2f, 10f)).IsAccepted ? h.Of<DamagedEvent>().Count : -1, "shots pass through the wreck");
        Check.True(h.Combat.History.Players().Contains(id), "kept in the history for the rewind window");
        h.Run(h.R.maxRewindSeconds + 0.2f);
        Check.False(h.Combat.History.Players().Contains(id), "then forgotten");
        Check.True(h.Combat.Units.Unit(id) == null, "and gone from the roster");

        int other = h.Combat.Units.Spawn(Drone, TeamId.A, Vec3.Zero);
        h.Forget();
        Check.True(h.Combat.Units.Remove(other), "removed by the rules");
        Check.Equal("removed", h.Last<UnitDestroyedEvent>().reason, "with no killer");
        Check.Equal(-1, h.Last<UnitDestroyedEvent>().killerId, "no killer");
    }

    [Test]
    static void UnitsCanAttackAndAreCreditedForKills()
    {
        var h = Harness();
        int id = h.Combat.Units.Spawn(Drone, TeamId.B, new Vec3(0f, 0f, 10f));
        h.Combat.Damage.AddModifiers(id, "test", new[] { new StatModifier { stat = StatIds.DamageDealt, op = ModOp.Multiply, value = 2f } });
        Check.Near(40f, h.Combat.Damage.Damage(id, A1, 20f, "claw"), "its damageDealt counts");
        Check.Equal(id, h.Last<DamagedEvent>().attackerId, "credited to the unit");
        Check.Near(0f, h.Combat.Damage.Damage(id, B1, 20f), "no damage to its own team");
        int neutral = h.Combat.Units.Spawn(Drone, TeamId.None, new Vec3(5f, 0f, 5f));
        Check.True(h.Combat.Damage.Damage(id, neutral, 20f) > 0f, "neutral units are everyone's enemy");
        Check.True(h.Combat.Damage.Damage(neutral, id, 20f) > 0f, "and fight back");
        h.Combat.Damage.Damage(id, A1, 1000f);
        var down = h.Last<PlayerEliminatedEvent>();
        Check.True(down != null && down.playerId == A1 && down.killerId == id, "a hero taken down by a unit names it");
    }

    // ---- Weapons ----

    /// <summary>One shot at whoever stands at the spot; the damage events, with the target's id written as T.</summary>
    static string ShotTrace(bool unit, string weapon, float aimHeight, float distance)
    {
        var h = Harness();
        h.Arm(A1, weapon);
        var spot = new Vec3(0f, 0f, distance);
        int target = B1;
        if (unit) target = h.Combat.Units.Spawn(Drone, TeamId.B, spot);
        else h.Place(B1, spot.x, spot.z);
        h.Tick();
        h.Forget();
        Check.True(h.FireAt(A1, spot + Vec3.Up * aimHeight, h.S.tick).IsAccepted, weapon + " fires");
        h.Run(1.5f); // projectiles land
        return Trace(h, target) + " health " + h.Health(target).ToString("0.###");
    }

    [Test]
    static void WeaponsHitAUnitExactlyAsAHero()
    {
        foreach (var w in new[] { "rifle", "smg", "shotgun", "pistol", "launcher" })
            foreach (var (height, what) in new[] { (1.2f, "body"), (1.62f, "head"), (0.5f, "legs") })
                foreach (float d in w == "launcher" ? new[] { 6f } : new[] { 6f, 25f })
                {
                    string hero = ShotTrace(false, w, height, d), unit = ShotTrace(true, w, height, d);
                    Check.True(hero.Contains("damaged"), w + " " + what + " at " + d + " m lands on the hero: " + hero);
                    Check.Equal(hero, unit, w + " " + what + " at " + d + " m");
                }
    }

    [Test]
    static void ShotsPassThroughOwnUnitsAndHitOnesAsTheyWere()
    {
        var h = Harness();
        h.Arm(A1, "rifle");
        int mine = h.Combat.Units.Spawn(Drone, TeamId.A, new Vec3(0f, 0f, 5f));
        int theirs = h.Combat.Units.Spawn(Drone, TeamId.B, new Vec3(0f, 0f, 10f));
        h.Tick();
        h.Forget();
        h.FireAt(A1, new Vec3(0f, 1.2f, 10f), h.S.tick);
        Check.Near(200f, h.Health(mine), "own unit untouched: the shot flies through it");
        Check.True(h.Health(theirs) < 200f, "and hits the enemy unit behind");

        // Lag compensation: the shooter saw the unit where it was a few ticks ago.
        int seen = h.S.tick;
        h.Tick();
        h.Combat.Units.Move(theirs, new Vec3(4f, 0f, 10f), 0f);
        h.Run(0.2f);
        float before = h.Health(theirs);
        Check.True(h.FireAt(A1, new Vec3(0f, 1.2f, 10f), seen).IsAccepted, "second shot accepted");
        Check.True(h.Health(theirs) < before, "a shot judged at the tick the shooter saw hits where the unit was");
    }

    // ---- Abilities and statuses ----

    [Test]
    static void StatusesWorkOnUnits()
    {
        var h = Harness();
        int id = h.Combat.Units.Spawn(Drone, TeamId.B, new Vec3(0f, 0f, 10f));
        Check.Near(4f, h.Combat.Units.Speed(id), "full speed");
        Check.True(h.Combat.Abilities.ApplyStatus(id, AbilityLibrary.SlowStatus, A1, 0.4f, 2f), "slowed");
        Check.Near(4f * 0.6f, h.Combat.Units.Speed(id), "40% slower");
        Check.True(h.Combat.Abilities.ApplyStatus(id, AbilityLibrary.StunStatus, A1, 1f, 1f), "stunned");
        Check.Near(0f, h.Combat.Units.Speed(id), "stunned units don't walk");
        h.Run(1.1f);
        Check.Near(4f * 0.6f, h.Combat.Units.Speed(id), "stun over, still slowed");
        Check.True(h.Combat.Abilities.Cleanse(id) >= 1, "cleansed");
        Check.Near(4f, h.Combat.Units.Speed(id), "full speed again");

        var snap = new MatchSnapshot();
        h.Combat.Abilities.ApplyStatus(id, AbilityLibrary.SlowStatus, A1, 0.4f, 2f);
        h.Combat.FillSnapshot(snap);
        var s = snap.Unit(id);
        Check.True(s != null && s.statuses.Contains(AbilityLibrary.SlowStatus), "statuses in the snapshot");
        Check.False(snap.abilities.units.Any(u => u.playerId == id), "units travel in their own compact list");
    }

    [Test]
    static void PushesMoveUnitsAndStopAtWalls()
    {
        var h = Harness();
        int id = h.Combat.Units.Spawn(Drone, TeamId.B, new Vec3(0f, 0f, 10f));
        h.Events.Publish(new PushedEvent { targetId = id, sourceId = A1, mode = PushDirections.Away, direction = Vec3.Forward, distance = 3f, seconds = 0.3f });
        h.Run(0.5f);
        Check.Near(13f, h.Combat.Units.Unit(id).position.z, "pushed 3 m over 0.3 s", 1e-3f);
        Check.True(h.Combat.History.PoseAt(id, h.S.tick, out var pose) && Math.Abs(pose.position.z - 13f) < 1e-3f, "and the hit history follows");

        h.Map.Wall(new Vec3(0f, 1f, 15f), new Vec3(4f, 2f, 0.4f));
        h.Events.Publish(new PushedEvent { targetId = id, sourceId = A1, mode = PushDirections.Away, direction = Vec3.Forward, distance = 10f, seconds = 0f });
        var at = h.Combat.Units.Unit(id).position;
        Check.True(at.z < 15f - 0.2f - h.R.torsoRadius + 1e-3f && at.z > 13f, "stopped short of the wall: " + at.z);
    }

    /// <summary>
    /// A hero's ability (slot, or -1 for the weapon) used on whoever stands 6 m ahead: the target's
    /// damage, healing, shields, statuses and pushes for 4 seconds, with the target written as T,
    /// plus its health and speed along the way.
    /// </summary>
    /// <param name="path">-1: level to the ultimate's form and take the first form. 0 or 1: level 20 with every left or right upgrade.</param>
    static string KitTrace(string heroId, int slot, bool ally, bool unit, int path = -1)
    {
        var h = HeroHarness.With(HeroHarness.A1, heroId);
        var hero = h.Heroes.Heroes.Resolve(heroId);
        if (path < 0)
        {
            h.LevelTo(HeroHarness.A1, h.Heroes.Progression.Rules.UltimateFormLevel());
            h.PickForm(HeroHarness.A1, hero.Level(h.Heroes.Progression.Rules.UltimateFormLevel()).options[0]);
        }
        else
        {
            h.LevelTo(HeroHarness.A1, 20);
            foreach (var level in hero.levels)
                if (level.options != null && level.options.Length > path)
                    Check.True(h.Heroes.Progression.Choose(HeroHarness.A1, level.level, path).IsAccepted, heroId + " takes " + level.options[path]);
        }
        h.Place(HeroHarness.A1, 0f, 0f);
        var spot = new Vec3(0f, 0f, 6f);
        int stand = ally ? HeroHarness.A2 : HeroHarness.B1;
        int target = stand;
        if (unit) target = h.Combat.Units.Spawn(Drone, ally ? TeamId.A : TeamId.B, spot);
        else h.Place(stand, spot.x, spot.z);
        if (ally) h.Combat.Damage.Damage(HeroHarness.B2, target, 120f, "setup"); // room to heal
        h.Tick();
        h.Forget();

        var chest = Hitboxes.Chest(h.R, new BodyPose(spot, false));
        var used = slot < 0 ? h.FireAt(HeroHarness.A1, chest) : h.Use(HeroHarness.A1, slot, chest - h.Eye(HeroHarness.A1));
        var sb = new StringBuilder(used.IsAccepted ? "used" : "refused " + used.reason);
        for (int i = 0; i < 8; i++)
        {
            h.Run(0.5f);
            var u = h.Combat.Damage.Unit(target);
            sb.Append(" | ").Append(!h.Combat.Damage.IsAlive(target) ? "down" : u.health.ToString("0.##") + "/" + u.Shield.ToString("0.##"))
              .Append(" speed ").Append(h.Combat.Abilities.MoveSpeed(target).ToString("0.###"));
        }
        return Trace(h, target) + " :: " + sb;
    }

    [Test]
    static void EveryStarterKitWorksOnUnitsAsOnHeroes()
    {
        int compared = 0, landed = 0;
        foreach (var heroId in HeroHarness.StarterIds)
        {
            var hero = ProgressionHarness.DataCatalog().Get(heroId);
            int slots = HeroServer.Slots(hero).Length;
            for (int slot = -1; slot < slots; slot++)
                foreach (bool ally in new[] { false, true })
                {
                    string what = heroId + (slot < 0 ? " weapon" : " slot " + slot) + (ally ? " on an ally" : " on an enemy");
                    string onHero = KitTrace(heroId, slot, ally, false), onUnit = KitTrace(heroId, slot, ally, true);
                    Check.Equal(onHero, onUnit, what);
                    if (Environment.GetEnvironmentVariable("UNIT_TRACE") == "1") Console.WriteLine("          " + what + ": " + (onHero.Length > 160 ? onHero.Substring(0, 160) : onHero));
                    compared++;
                    if (onHero.Contains("damaged") || onHero.Contains("healed") || onHero.Contains("status") || onHero.Contains("pushed") || onHero.Contains("shielded")) landed++;
                }
        }
        Check.True(compared >= 32, "compared every weapon and slot: " + compared);
        Check.True(landed >= 12, "and most of them reached the target: " + landed);
    }

    [Test]
    static void UpgradePathsWorkOnUnitsAsOnHeroes()
    {
        // Level 20 down each path: slows, burns, pushes, marks and life steal from upgrades land the same.
        int compared = 0;
        foreach (var heroId in HeroHarness.StarterIds)
        {
            var hero = ProgressionHarness.DataCatalog().Get(heroId);
            int slots = HeroServer.Slots(hero).Length;
            for (int path = 0; path < 2; path++)
                for (int slot = -1; slot < slots; slot++)
                    foreach (bool ally in new[] { false, true })
                    {
                        string what = heroId + (path == 0 ? " left" : " right") + " path, " + (slot < 0 ? "weapon" : "slot " + slot) + (ally ? " on an ally" : " on an enemy");
                        Check.Equal(KitTrace(heroId, slot, ally, false, path), KitTrace(heroId, slot, ally, true, path), what);
                        compared++;
                    }
        }
        Check.True(compared >= 64, "compared both paths of every kit: " + compared);
    }

    [Test]
    static void ObjectsAndAreasFindUnits()
    {
        var h = Harness();
        int id = h.Combat.Units.Spawn(Drone, TeamId.B, new Vec3(0f, 0f, 10f));
        var areas = h.Combat.World.PlayersInSphere(new Vec3(0f, 1f, 10f), 2f, h.S.tick);
        Check.True(areas.Contains(id), "area queries see units");
        Check.True(h.Combat.Units.InSphere(new Vec3(0f, 1f, 10f), 2f).Contains(id), "and so does the unit query");
        Check.True(h.Combat.Abilities.Matches(Affects.Enemies, A1, TeamId.A, id), "an enemy unit is an enemy");
        Check.False(h.Combat.Abilities.Matches(Affects.Allies, A1, TeamId.A, id), "not an ally");
        int mine = h.Combat.Units.Spawn(Drone, TeamId.A, new Vec3(0f, 0f, 12f));
        Check.True(h.Combat.Abilities.Matches(Affects.Allies, A1, TeamId.A, mine), "an own unit is an ally");

        // A unit can hurt a placed object, and only an enemy one.
        var def = new ObjectDef { id = "crate", health = 100f };
        var turret = h.Combat.Abilities.SpawnObject(def, A1, new Vec3(2f, 0f, 2f));
        Check.Near(0f, h.Combat.Abilities.DamageObject(turret.id, 30f, mine), "own team's unit can't hurt it");
        Check.Near(30f, h.Combat.Abilities.DamageObject(turret.id, 30f, id), "an enemy unit can");
    }

    // ---- The wire ----

    [Test]
    static void FortyUnitsFitTheSnapshot()
    {
        var h = Harness();
        for (int i = 0; i < 40; i++)
        {
            int id = h.Combat.Units.Spawn(Drone, i % 2 == 0 ? TeamId.A : TeamId.B, new Vec3(i, 0f, 20f + i % 5), i * 9f);
            if (i % 3 == 0) h.Combat.Abilities.ApplyStatus(id, AbilityLibrary.SlowStatus, A1, 0.3f, 2f);
        }
        h.Tick();
        var empty = new MatchSnapshot();
        var snap = new MatchSnapshot();
        h.Combat.FillSnapshot(snap);
        Check.Equal(40, snap.units.Count, "all forty");
        var bytes = WireCodec.Encode(new SnapshotMessage { snapshot = snap });
        var copy = ((SnapshotMessage)WireCodec.Decode(bytes)).snapshot;
        Check.Equal(40, copy.units.Count, "after the wire");
        var a = snap.units[7]; var b = copy.units[7];
        Check.True(a.id == b.id && a.team == b.team && a.position == b.position && a.health == b.health && a.typeId == b.typeId, "unit 7 unchanged");
        int perUnit = (bytes.Length - WireCodec.Encode(new SnapshotMessage { snapshot = Without(snap) }).Length) / 40;
        Console.WriteLine("          40 units add " + (bytes.Length - WireCodec.Encode(new SnapshotMessage { snapshot = Without(snap) }).Length) + " bytes to a snapshot (" + perUnit + " per unit)");
        Check.True(perUnit < 120, "compact: " + perUnit + " bytes a unit");
    }

    static MatchSnapshot Without(MatchSnapshot s)
    {
        var bytes = WireCodec.Encode(new SnapshotMessage { snapshot = s });
        var copy = ((SnapshotMessage)WireCodec.Decode(bytes)).snapshot;
        copy.units = new List<UnitSnapshot>();
        return copy;
    }

    // ---- Helpers ----

    /// <summary>Events about one target as text, with its id written as T so a hero and a unit compare.</summary>
    static string Trace(CombatHarness h, int target) => Trace(h.Events.History, target);

    static string Trace(HeroHarness h, int target) => Trace(h.Events.History, target);

    static string Trace(IEnumerable<GameEvent> events, int target)
    {
        var sb = new StringBuilder();
        foreach (var e in events)
        {
            switch (e)
            {
                case DamagedEvent d when d.targetId == target:
                    sb.Append($"[{d.tick} damaged by {d.attackerId} {d.amount:0.###} abs {d.absorbed:0.###} via {d.abilityId}/{d.objectId}{(d.eliminated ? " down" : "")}]");
                    break;
                case HealedEvent x when x.targetId == target:
                    sb.Append($"[{x.tick} healed by {x.healerId} {x.amount:0.###} via {x.abilityId}]");
                    break;
                case ShieldedEvent x when x.targetId == target:
                    sb.Append($"[{x.tick} shielded by {x.sourceId} {x.amount:0.###} for {x.seconds:0.###} via {x.abilityId}]");
                    break;
                case StatusAppliedEvent x when x.targetId == target:
                    sb.Append($"[{x.tick} status {x.statusId} from {x.sourceId} x{x.stacks} {x.strength:0.###} {x.seconds:0.###}]");
                    break;
                case StatusEndedEvent x when x.targetId == target:
                    sb.Append($"[{x.tick} status {x.statusId} ended {x.reason}]");
                    break;
                case PushedEvent x when x.targetId == target:
                    sb.Append($"[{x.tick} pushed {x.mode} {x.distance:0.###} {x.direction.x:0.##},{x.direction.z:0.##}]");
                    break;
                case ShotFiredEvent x:
                    sb.Append("[fired " + string.Join(",", x.hitPlayers.Select(p => p == target ? "T" : p.ToString())) + " " + string.Join(",", x.hitParts) + "]");
                    break;
                case AbilityUsedEvent x:
                    sb.Append("[used " + x.abilityId + " on " + string.Join(",", x.targets.Select(p => p == target ? "T" : p.ToString())) + "]");
                    break;
                case ObjectTriggeredEvent x:
                    sb.Append("[object " + x.defId + " on " + string.Join(",", x.targets.Select(p => p == target ? "T" : p.ToString())) + "]");
                    break;
            }
        }
        return sb.ToString();
    }
}
