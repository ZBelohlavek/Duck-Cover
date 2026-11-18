using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DuckAndCover.Core;
using static HeroHarness;

/// <summary>
/// The four starter heroes (WP11): their data passes every check, a pick gives the kit, every
/// ability does what its description says, and every one of the 72 upgrades changes the hero.
/// </summary>
static class HeroKitTests
{
    // ---- Data ----

    [Test]
    static void StarterHeroesAreCompleteAndOriginalToTheirRoles()
    {
        var catalog = ProgressionHarness.DataCatalog();
        Check.Equal(0, catalog.Validate(new ProgressionRuleset()).Count, "leveling check: " + string.Join("; ", catalog.Validate(new ProgressionRuleset())));
        var lib = DataLibrary();
        Check.Equal(0, lib.Validate().Count, "ability check: " + string.Join("; ", lib.Validate()));
        var problems = HeroServer.Validate(catalog, lib, StarterWeapons.Library());
        Check.Equal(0, problems.Count, "hero kit check: " + string.Join("; ", problems));

        var roles = new HashSet<string>();
        foreach (var id in StarterIds)
        {
            var h = catalog.Get(id);
            Check.True(h != null, id + " loads");
            if (h == null) continue;
            roles.Add(h.role);
            Check.Equal(10, h.levels.Length, id + ": ten choices");
            Check.Equal(20, h.upgrades.Length, id + ": twenty options");
            Check.Equal(6, h.upgrades.Count(u => u.Kind == UpgradeKind.Trait), id + ": six traits");
            Check.Equal(2, h.upgrades.Count(u => u.Kind == UpgradeKind.Passive), id + ": two passives");
            Check.Equal("", h.passiveId, id + ": no passive in the base kit");
            Check.Equal(8, h.upgrades.Count(u => u.Kind == UpgradeKind.Talent), id + ": eight talents");
            Check.Equal(2, h.upgrades.Count(u => u.Kind == UpgradeKind.Ultimate), id + ": two ultimate forms");
            Check.Equal(2, h.upgrades.Count(u => u.Kind == UpgradeKind.Capstone), id + ": two capstones");
            foreach (var u in h.upgrades) Check.True(!string.IsNullOrEmpty(u.description), id + "." + u.id + " has a description");
            foreach (var a in HeroServer.Slots(h))
                Check.True(!string.IsNullOrEmpty(lib.Ability(a)?.description), a + " has a description");
        }
        Check.Equal("controller,striker,support,vanguard", string.Join(",", roles.OrderBy(r => r)), "one hero per role");
    }

    [Test]
    static void HeroValidationCatchesBrokenKits()
    {
        var catalog = ProgressionHarness.DataCatalog();
        var lib = DataLibrary();
        var lancer = catalog.Get("lancer");
        lancer.weaponId = "shotgun";
        lancer.abilities = new[] { "lancer_slip", "lancer_firebomb" };
        lancer.Upgrade("lancer_twin_marks").abilityChanges[0].param = "effect.push.amount";
        var problems = HeroServer.Validate(catalog, lib, StarterWeapons.Library());
        Check.True(problems.Any(p => p.Contains("loadout should start with shotgun")), "a weapon that isn't the loadout's first");
        Check.True(problems.Any(p => p.Contains("marked ultimate but sits in an ability slot")), "an ultimate in an ability slot");
        Check.True(problems.Any(p => p.Contains("lancer_twin_marks") && p.Contains("no such parameter")), "a talent changing something the ability lacks");
    }

    [Test]
    static void AbilityFilesHaveOnlyKnownKeys()
    {
        // The strict loader refuses unknown keys, so loading every file is the check.
        foreach (var f in Directory.GetFiles(AbilityHarness.DataDir, "*.json"))
        {
            try { AbilityHarness.Parse(File.ReadAllText(f)); }
            catch (JsonException e) { Check.Fail(Path.GetFileName(f) + ": " + e.Message); }
        }
    }

    // ---- Picking a hero ----

    [Test]
    static void APickGivesTheWholeKit()
    {
        var picks = new Dictionary<int, string> { [A1] = "bulwark", [A2] = "lancer", [B1] = "mender", [B2] = "rigger" };
        var h = new HeroHarness(picks);
        var catalog = h.Heroes.Heroes;
        foreach (var kv in picks)
        {
            var hero = catalog.Get(kv.Value);
            var u = h.Unit(kv.Key);
            Check.Equal(string.Join(",", HeroServer.Slots(hero)), string.Join(",", u.slots.Select(s => s.abilityId)), kv.Value + ": abilities then ultimate");
            Check.Equal(hero.passiveId, u.passiveId, kv.Value + ": passive");
            Check.Near(hero.maxHealth, h.Combat.Damage.MaxHealth(kv.Key), kv.Value + ": max health", 0.01f);
            Check.Equal(hero.weaponId, h.Combat.Weapons.Weapons(kv.Key).weapons[0], kv.Value + ": weapon in hand");
        }
        Check.Near(1f, h.Stat(A2, StatIds.ReloadSpeed), "no passive at level 1: Lancer reloads at the base rate");
        Check.Near(0f, h.Stat(B1, StatIds.HealthRegen), "Mender regains nothing on her own");
        Check.Near(1f, h.Stat(B2, StatIds.CooldownRate), "Rigger's cooldowns at the base rate");
        Check.False(h.Unit(A1).stats.modifiers.Any(), "Bulwark has no stat changes");
        Check.Equal(1, h.Unit(A1).slots[0].charges, "abilities start ready");
    }

    [Test]
    static void UltimatesWaitForLevelFive()
    {
        var h = With(A1, "lancer", B1, "bulwark");
        h.Unit(A1).ultimateCharge = 100f;
        Check.False(h.Use(A1, 2, Ahead).IsAccepted, "locked at level 1");
        h.LevelTo(A1, h.Heroes.Progression.Rules.UltimateFormLevel());
        var held = h.Use(A1, 2, Ahead);
        Check.False(held.IsAccepted, "held at level 5 until a form is picked");
        Check.True(held.reason.Contains("form"), "and says why: " + held.reason);
        Check.True(h.PickForm(A1, "lancer_thermite").IsAccepted, "pick Thermite");
        Check.True(h.Use(A1, 2, Ahead).IsAccepted, "open once the form is picked");
    }

    // ---- Every upgrade ----

    [Test]
    static void EveryUpgradeChangesTheHero()
    {
        var catalog = ProgressionHarness.DataCatalog();
        foreach (var heroId in StarterIds)
        {
            var hero = catalog.Get(heroId);
            foreach (var u in hero.upgrades)
            {
                var h = With(A1, heroId);
                string what = heroId + "." + u.id;
                var unit = h.Unit(A1);
                var statsBefore = u.stats.Select(s => unit.stats.Get(s.stat, _ => true)).ToArray();
                var abilitiesBefore = u.abilityChanges.Select(c => c.ability).Distinct().ToDictionary(a => a, a => h.Resolved(A1, a));

                Check.True(h.Heroes.ApplyUpgrade(A1, heroId, u.id).IsAccepted, what + " applies");

                for (int i = 0; i < u.stats.Length; i++)
                    Check.True(Math.Abs(unit.stats.Get(u.stats[i].stat, _ => true) - statsBefore[i]) > 1e-4f, what + " changes " + u.stats[i].stat);
                foreach (var kv in abilitiesBefore)
                    Check.True(h.Resolved(A1, kv.Key) != kv.Value, what + " changes " + kv.Key);
                Check.True(u.stats.Length + u.abilityChanges.Length > 0, what + " does something");
            }
        }
    }

    [Test]
    static void ChoosingThroughLevelingAppliesTheUpgrade()
    {
        var h = With(A1, "bulwark");
        Check.Near(300f, h.Combat.Damage.MaxHealth(A1), "base");
        Check.False(h.Heroes.Progression.Apply(new ChooseUpgradeCommand { playerId = A1, level = 2, option = 0 }).IsAccepted, "not level 2 yet");
        h.LevelTo(A1, 4, false);
        float boost = 1f + 0.02f * h.Heroes.Progression.Rules.BoostLevels(h.Heroes.Progression.Level(A1));
        Check.True(boost > 1f, "levels without a choice have passed");
        Check.Near(300f * boost, h.Combat.Damage.MaxHealth(A1), "each gives 2% max health by itself");
        Check.Near(boost, h.Stat(A1, StatIds.DamageDealt), "and 2% damage");
        Check.True(h.Heroes.Progression.Apply(new ChooseUpgradeCommand { playerId = A1, level = 2, option = 0 }).IsAccepted, "Thick Hide");
        Check.Near(350f * boost, h.Combat.Damage.MaxHealth(A1), "+50 max health");
        Check.Near(350f * boost, h.Health(A1), "given as health at once");
        Check.True(h.Heroes.Progression.Apply(new ChooseUpgradeCommand { playerId = A1, level = 4, option = 1 }).IsAccepted, "Double Rush");
        Check.Equal(2, h.Unit(A1).slots[1].charges, "the extra charge comes at once");
    }

    [Test]
    static void FightingEarnsExperience()
    {
        var h = With(A1, "mender", B1, "lancer");
        h.Place(A1, 0f, 0f);
        h.Place(A2, 0f, 3f);
        h.Unit(A2).health = 100f;
        var before = h.Heroes.Progression.Progress(A1)?.xp ?? 0f;
        Check.True(h.FireAt(A1, h.ChestOf(A2)).IsAccepted, "heal shot");
        Check.True(h.Heroes.Progression.Progress(A1).xp > before, "healing a teammate pays experience");
    }

    // ---- Bulwark ----

    [Test]
    static void BastionPanelStopsEnemyShotsButNotTeammates()
    {
        var h = new HeroHarness(new Dictionary<int, string> { [A1] = "bulwark", [A2] = "lancer", [B1] = "lancer" });
        h.Map.Wall(new Vec3(0f, -0.5f, 0f), new Vec3(200f, 1f, 200f)); // a floor
        h.Place(A1, 0f, 0f);
        h.Place(A2, 0.5f, -1f);
        h.Place(B1, 0f, 15f);
        Check.True(h.Use(A1, 0, new Vec3(0f, -0.4f, 1f)).IsAccepted, "panel");
        var spawned = h.Last<ObjectSpawnedEvent>();
        Check.True(spawned != null && spawned.blocksShots && spawned.width == 3f && spawned.height == 2.2f, "a 3 m solid panel");
        Check.Near(0f, spawned.position.y, "standing on the floor", 0.01f);
        Check.Near(1f, spawned.facing.z, "facing the aim", 0.01f);
        h.Run(0.2f);

        float before = h.Health(A1);
        Check.True(h.FireAt(B1, h.ChestOf(A1)).IsAccepted, "enemy fires");
        Check.Near(before, h.Health(A1), "the panel took it");
        Check.True(h.Last<ObjectDamagedEvent>() != null, "the panel is hurt");
        float b1 = h.Health(B1);
        Check.True(h.FireAt(A2, h.ChestOf(B1)).IsAccepted, "teammate fires through");
        Check.True(h.Health(B1) < b1, "teammates' shots pass through");
        Check.True(h.UseAt(B1, 1, A1).IsAccepted, "enemy Tracer Mark");
        Check.False(h.Unit(A1).HasStatus("lancer_marked"), "enemy ability rays stop on it too");
        Check.True(h.UseAt(A2, 1, B1).IsAccepted, "teammate Tracer Mark");
        Check.True(h.Unit(B1).HasStatus("lancer_marked"), "teammates' ability rays pass through");
        Vec3 behind = h.ChestOf(A1) + new Vec3(0f, 0f, -0.5f);
        Check.True(h.Combat.World.HasLineOfSightFor(A2, behind, B1, h.S.tick), "a teammate's sentry sees through it");
        Check.False(h.Combat.World.HasLineOfSightFor(B1, h.ChestOf(B1) + new Vec3(0f, 0f, 0.5f), A1, h.S.tick), "an enemy's doesn't");

        // Shot down, it's gone.
        h.Abilities.DamageObject(spawned.objectId, 10000f, B1);
        Check.Equal(0, h.Abilities.State.objects.Count, "destroyed");
        h.Run(0.2f);
        Check.True(h.FireAt(B1, h.ChestOf(A1)).IsAccepted, "fires again");
        Check.True(h.Health(A1) < before, "now it hits");
    }

    [Test]
    static void BreachRushChargesAndHitsWhereYouLand()
    {
        var h = With(A1, "bulwark", B1, "lancer");
        h.Place(A1, 0f, 0f);
        h.Place(B1, 0f, 7f);
        Check.True(h.Use(A1, 1, Ahead).IsAccepted, "rush");
        var dash = h.Of<PushedEvent>().FirstOrDefault(p => p.targetId == A1);
        Check.True(dash != null && Math.Abs(dash.distance - 7f) < 1e-3f && dash.mode == PushDirections.Aim, "a 7 m charge along the aim");
        h.Run(0.4f);
        Check.Near(200f - 35f, h.Health(B1), "35 damage where you land");
        Check.True(h.Of<PushedEvent>().Any(p => p.targetId == B1 && p.mode == PushDirections.Away), "and a knockback");
    }

    [Test]
    static void TheServerLetsADashThrough()
    {
        var h = With(A1, "bulwark");
        h.Place(A1, 0f, 0f);
        h.Run(1f); // a full movement allowance
        var m = h.M(A1);
        Check.True(h.Use(A1, 1, Ahead).IsAccepted, "rush");
        // The client plays the 7 m charge out over 0.35 s, one move a tick.
        for (int i = 1; i <= 7; i++)
        {
            var to = new Vec3(0f, 0f, i * 1f);
            var r = h.Combat.Apply(new MoveCommand { playerId = A1, position = to, yaw = 0f, placement = m.placement });
            Check.True(r.IsAccepted, "dash step " + i + " accepted");
            h.Tick();
        }
        Check.Near(7f, h.M(A1).position.z, "arrived");
        h.Run(3f);
        var jump = h.Combat.Apply(new MoveCommand { playerId = A1, position = new Vec3(0f, 0f, 7f + 9f), yaw = 0f, placement = m.placement });
        Check.False(jump.IsAccepted, "the dash's extra metres don't last");
    }

    [Test]
    static void HoldTheLineLocksDownTheArea()
    {
        var h = With(A1, "bulwark", B1, "lancer");
        h.Place(A1, 0f, 0f);
        h.Place(B1, 0f, 5f);
        h.LevelTo(A1, h.Heroes.Progression.Rules.UltimateFormLevel());
        h.PickForm(A1, "bulwark_iron_ring");
        Check.True(h.Use(A1, 2, Ahead).IsAccepted, "ultimate");
        Check.True(h.Shield(A1) >= 100f - 1e-3f, "a 100 shield");
        h.Run(0.6f);
        Check.True(h.Unit(B1).HasStatus(AbilityLibrary.SlowStatus), "enemies inside are slowed");
        Check.True(h.Health(B1) < 200f, "and worn down");
        Check.False(h.Unit(B1).HasStatus(AbilityLibrary.RootStatus), "no root without Lockdown");
    }

    [Test]
    static void LockdownRootsWhenTheFieldGoesUp()
    {
        var h = With(A1, "bulwark", B1, "lancer");
        h.Place(A1, 0f, 0f);
        h.Place(B1, 0f, 5f);
        h.LevelTo(A1, h.Heroes.Progression.Rules.UltimateFormLevel());
        Check.True(h.PickForm(A1, "bulwark_lockdown").IsAccepted, "pick Lockdown");
        Check.True(h.Use(A1, 2, Ahead).IsAccepted, "ultimate");
        Check.True(h.Unit(B1).HasStatus(AbilityLibrary.RootStatus), "rooted");
    }

    // ---- Lancer ----

    [Test]
    static void SlipDashes()
    {
        var h = With(A1, "lancer");
        Check.True(h.Use(A1, 0, Ahead).IsAccepted, "slip");
        Check.True(h.Of<PushedEvent>().Any(p => p.targetId == A1 && Math.Abs(p.distance - 6f) < 1e-3f), "6 m dash");
        Check.False(h.Use(A1, 0, Ahead).IsAccepted, "one charge");
        h.Run(6.1f);
        Check.True(h.Use(A1, 0, Ahead).IsAccepted, "ready again after 6 seconds");
    }

    [Test]
    static void TracerMarkHurtsRevealsAndExposes()
    {
        var h = With(A1, "lancer", B1, "bulwark");
        h.Place(A1, 0f, 0f);
        h.Place(B1, 0f, 20f);
        Check.True(h.UseAt(A1, 1, B1).IsAccepted, "mark");
        Check.Near(300f - 25f, h.Health(B1), "25 damage");
        Check.True(h.Unit(B1).HasStatus("lancer_marked"), "marked");
        Check.True(h.Abilities.IsRevealed(B1), "shown through walls");
        Check.Near(1.15f, h.Stat(B1, StatIds.DamageTaken), "takes 15% more");
        h.Run(5.1f);
        Check.False(h.Unit(B1).HasStatus("lancer_marked"), "for 5 seconds");
    }

    static HeroHarness Grenadier(string form, params string[] more)
    {
        var h = With(A1, "lancer", B1, "bulwark");
        h.Place(A1, 0f, 0f);
        h.Place(B1, 0f, 15f);
        h.LevelTo(A1, h.Heroes.Progression.Rules.UltimateFormLevel());
        Check.True(h.PickForm(A1, form).IsAccepted, "pick " + form);
        foreach (var u in more) Check.True(h.Heroes.ApplyUpgrade(A1, "lancer", u).IsAccepted, u);
        h.NoShield(B1);
        return h;
    }

    static float[] BurnTicks(HeroHarness h, int target) =>
        h.Of<DamagedEvent>().Where(d => d.targetId == target && d.abilityId.Contains("lancer_burning")).Select(d => d.amount).ToArray();

    [Test]
    static void IncendiaryGrenadeBurnsTheGround()
    {
        var h = Grenadier("lancer_clinging_fire");
        Check.Equal("lancer_firebomb", h.Unit(A1).slots[2].abilityId, "the grenade is the ultimate");
        Check.True(h.UseAt(A1, 2, B1).IsAccepted, "throw");
        Check.True(h.Health(B1) <= 300f - 40f + 1e-3f, "40 where it lands");
        h.Run(1f);
        float inFire = h.Health(B1);
        Check.True(inFire < 300f - 40f - 15f, "the fire burns while they stand in it (" + inFire + ")");
        Check.True(h.Unit(B1).HasStatus("lancer_burning"), "Clinging Fire sets them burning");

        h.Place(B1, 0f, 40f); // out of the fire
        h.Forget();
        h.Run(1.6f);
        Check.True(h.Health(B1) < inFire - 10f, "and they keep burning after they leave");
        var ticks = BurnTicks(h, B1);
        float dealt = h.Stat(A1, StatIds.DamageDealt); // the level boost
        Check.True(ticks.Length >= 2 && ticks.All(t => Math.Abs(t - 5f * dealt) < 1e-3f), "5 a tick, twice a second (" + string.Join(",", ticks) + ")");
        h.Run(2f);
        float after = h.Health(B1);
        h.Run(1f);
        Check.Near(after, h.Health(B1), "for 3 seconds, then it stops");
    }

    [Test]
    static void WithoutClingingFireTheBurnStaysInTheFire()
    {
        var h = Grenadier("lancer_thermite");
        Check.True(h.UseAt(A1, 2, B1).IsAccepted, "throw");
        Check.True(h.Health(B1) <= 300f - 120f + 1e-3f, "Thermite: 120 where it lands");
        Check.False(h.Unit(B1).HasStatus("lancer_burning"), "no lingering burn");
        h.Place(B1, 0f, 40f);
        float out_ = h.Health(B1);
        h.Run(2f);
        Check.Near(out_, h.Health(B1), "nothing once they step out");
    }

    [Test]
    static void InfernoBurnsHarderAndLonger()
    {
        var h = Grenadier("lancer_clinging_fire", "lancer_inferno");
        var fire = h.ResolvedDef(A1, "lancer_firebomb").objects["lancer_fire"];
        Check.Near(4f + 1f + 1.5f, fire.radius, "Clinging Fire and Inferno widen the fire");
        Check.Near(8f, fire.lifetime, "Inferno: 3 seconds longer");
        h.UseAt(A1, 2, B1);
        h.Run(0.6f);
        h.Place(B1, 0f, 40f);
        h.Forget();
        h.Run(1.6f);
        var ticks = BurnTicks(h, B1);
        float dealt = h.Stat(A1, StatIds.DamageDealt);
        Check.True(ticks.Length >= 2 && ticks.All(t => Math.Abs(t - 6.5f * dealt) < 1e-3f), "the burn is 30% stronger (" + string.Join(",", ticks) + ")");
    }

    [Test]
    static void SlipTalentsGuardTheGroundYouLeave()
    {
        var h = With(A1, "lancer", B1, "bulwark");
        h.Place(A1, 0f, 0f);
        h.Place(B1, 0f, 3f);
        h.NoShield(B1);
        Check.True(h.Use(A1, 0, Ahead).IsAccepted, "plain slip");
        Check.False(h.Of<PushedEvent>().Any(p => p.targetId == B1), "a plain Slip leaves enemies alone");

        h.Heroes.ApplyUpgrade(A1, "lancer", "lancer_repulse");
        h.Heroes.ApplyUpgrade(A1, "lancer", "lancer_firewalk");
        h.Run(6.1f);
        h.Place(A1, 0f, 0f);
        h.Place(B1, 0f, 3f);
        h.Forget();
        Check.True(h.Use(A1, 0, new Vec3(0f, 0f, -1f)).IsAccepted, "slip away from the enemy");
        Check.True(h.Of<PushedEvent>().Any(p => p.targetId == A1 && Math.Abs(p.distance - 6f) < 1e-3f), "Lancer still dashes");
        Check.True(h.Of<PushedEvent>().Any(p => p.targetId == B1 && Math.Abs(p.distance - 4f) < 1e-3f), "Repulse: the close enemy is blasted 4 m");
        Check.Near(0.6f, h.Combat.Abilities.MoveSpeed(B1), "and slowed 40%");
        h.Place(B1, 0f, 1f); // chasing back through the spot Lancer left
        float before = h.Health(B1);
        h.Run(1f);
        Check.True(h.Health(B1) < before, "Firewalk: the fire left behind burns them");
    }

    [Test]
    static void MarkTalentsSlowAndExpose()
    {
        var h = With(A1, "lancer", B1, "bulwark");
        h.Place(A1, 0f, 0f);
        h.Place(B1, 0f, 20f);
        h.Heroes.ApplyUpgrade(A1, "lancer", "lancer_pinning_mark");
        h.Heroes.ApplyUpgrade(A1, "lancer", "lancer_hunters_mark");
        Check.True(h.UseAt(A1, 1, B1).IsAccepted, "mark");
        Check.Near(0.7f, h.Combat.Abilities.MoveSpeed(B1), "Pinning Mark: 30% slower");
        Check.Near(1.25f, h.Stat(B1, StatIds.DamageTaken), "Hunter's Mark: 25% more damage taken");
        h.Heroes.ApplyUpgrade(A1, "lancer", "lancer_twin_marks");
        Check.True(h.UseAt(A1, 1, B1).IsAccepted, "Twin Marks: a second mark right away");
    }

    // ---- Mender ----

    [Test]
    static void MendingSidearmHealsTeammatesAndHurtsEnemies()
    {
        var h = With(A1, "mender", B1, "lancer");
        h.Place(A1, 0f, 0f);
        h.Place(A2, 0f, 5f);
        h.Place(B1, 3f, 10f);
        h.Unit(A2).health = 100f;
        Check.True(h.FireAt(A1, h.ChestOf(A2)).IsAccepted, "shoot a teammate");
        Check.Near(114f, h.Health(A2), "healed 14");
        h.Run(0.2f);
        Check.True(h.FireAt(A1, h.ChestOf(B1)).IsAccepted, "shoot an enemy");
        Check.Near(185f, h.Health(B1), "15 damage");
    }

    [Test]
    static void MendingFieldHealsOverTime()
    {
        var h = With(A1, "mender");
        h.Place(A1, 0f, 0f);
        h.Place(A2, 0f, 3f);
        h.Unit(A2).health = 100f;
        Check.True(h.UseAt(A1, 0, A2).IsAccepted, "field");
        h.Run(3.05f);
        Check.Near(160f, h.Health(A2), "20 a second for three seconds", 0.5f);
    }

    [Test]
    static void PurgeCleansesAndHeals()
    {
        var h = With(A1, "mender", B1, "rigger");
        h.Place(A1, 0f, 0f);
        h.Place(A2, 0f, 3f);
        h.Unit(A2).health = 100f;
        h.Abilities.ApplyStatus(A2, AbilityLibrary.SlowStatus, B1, 0.5f, 3f, 0f, "rigger_mine");
        Check.True(h.Use(A1, 1, Ahead).IsAccepted, "purge");
        Check.False(h.Unit(A2).HasStatus(AbilityLibrary.SlowStatus), "slow cleared");
        Check.Near(130f, h.Health(A2), "healed 30");
        Check.False(h.Unit(A2).HasStatus("mender_quicken"), "no speed without the talent");
    }

    [Test]
    static void AegisPulseShieldsTheTeam()
    {
        var h = With(A1, "mender");
        h.Place(A1, 0f, 0f);
        h.Place(A2, 0f, 10f);
        h.LevelTo(A1, h.Heroes.Progression.Rules.UltimateFormLevel());
        h.PickForm(A1, "mender_broad_aegis");
        Check.True(h.Use(A1, 2, Ahead).IsAccepted, "aegis");
        Check.Near(100f, h.Shield(A1), "self");
        Check.Near(100f, h.Shield(A2), "teammate 10 m away");
        h.Run(5.1f);
        Check.Near(0f, h.Shield(A2), "for 5 seconds", 0.01f);
        Check.False(h.Unit(A2).shields.Any(s => s.abilityId == "mender_aegis"), "the shield is gone");
    }

    // ---- Rigger ----

    [Test]
    static void SnareMineCatchesTheFirstEnemy()
    {
        var h = With(A1, "rigger", B1, "lancer");
        h.Place(A1, 0f, 0f);
        Check.True(h.Use(A1, 0, new Vec3(0f, -0.1f, 1f)).IsAccepted, "mine");
        Check.True(h.Use(A1, 0, new Vec3(1f, -0.1f, 1f)).IsAccepted, "second charge, somewhere else");
        Check.False(h.Use(A1, 0, Ahead).IsAccepted, "two charges");
        var mine = h.Abilities.State.objects[0];
        h.Run(1.1f);
        h.Place(B1, mine.position.x, mine.position.z + 1f);
        h.Run(0.1f);
        Check.Near(170f, h.Health(B1), "30 damage", 0.5f);
        Check.True(h.Unit(B1).HasStatus(AbilityLibrary.SlowStatus), "and slowed");
        Check.Equal(1, h.Abilities.State.objects.Count, "used up");
    }

    [Test]
    static void SentryShootsWhatItSees()
    {
        var h = With(A1, "rigger", B1, "lancer");
        h.Place(A1, 0f, 0f);
        h.Place(B1, 0f, 10f);
        Check.True(h.Use(A1, 1, new Vec3(0f, -0.2f, 1f)).IsAccepted, "sentry");
        h.Run(2.05f);
        Check.True(h.Health(B1) < 200f, "it fires once armed");
        var sentry = h.Abilities.State.objects[0];
        h.Map.Wall(new Vec3(0f, 1f, (sentry.position.z + 10f) * 0.5f), new Vec3(4f, 4f, 0.5f));
        h.Unit(B1).health = 200f;
        h.Run(1f);
        Check.Near(200f, h.Health(B1), "not through a wall");
    }

    [Test]
    static void LockoutGateClosesTheRoute()
    {
        var h = With(A1, "rigger", B1, "lancer");
        h.Map.Wall(new Vec3(0f, -0.5f, 0f), new Vec3(200f, 1f, 200f));
        h.Place(A1, 0f, 0f);
        h.Place(B1, 2f, 20f);
        h.LevelTo(A1, h.Heroes.Progression.Rules.UltimateFormLevel());
        h.PickForm(A1, "rigger_snaring_gate");
        Check.True(h.Use(A1, 2, new Vec3(0f, -0.1f, 1f)).IsAccepted, "gate");
        var gate = h.Last<ObjectSpawnedEvent>();
        Check.True(gate != null && gate.blocksMovement && gate.width == 8f && gate.height == 4f, "an 8 by 4 m gate");
        float before = h.Health(A1);
        Check.True(h.FireAt(B1, h.ChestOf(A1)).IsAccepted, "fire at the Rigger through the gate");
        Check.Near(before, h.Health(A1), "the gate takes it");
        h.Run(6.1f);
        Check.Equal(0, h.Abilities.State.objects.Count, "gone after 6 seconds");
    }

    // ---- Stats the weapons read ----

    [Test]
    static void WeaponStatsChangeTheGuns()
    {
        var h = With(A1, "lancer", B1, "bulwark");
        h.Place(A1, 0f, 0f);
        h.Place(B1, 0f, 10f);
        Check.True(h.FireAt(A1, h.ChestOf(B1)).IsAccepted, "plain shot");
        Check.Near(300f - 18f, h.Health(B1), "18 a body shot");
        h.Unit(A1).health = 100f;
        h.Heroes.ApplyUpgrade(A1, "lancer", "lancer_bloodthirst");
        h.Heroes.ApplyUpgrade(A1, "lancer", "lancer_stopping_power");
        h.Run(0.2f);
        Check.True(h.FireAt(A1, h.ChestOf(B1)).IsAccepted, "upgraded shot");
        Check.Near(100f + 1.8f, h.Health(A1), "Bloodthirst: heal 10% of it");
        Check.True(h.Of<PushedEvent>().Any(p => p.targetId == B1 && p.sourceId == A1), "Stopping Power: the shot pushes");

        var rifle = h.Combat.Weapons.Library.Weapon("rifle");
        Check.Near(rifle.reloadSeconds, h.Combat.Weapons.ReloadSeconds(A1, rifle), "no passive reload bonus any more");
        h.Heroes.ApplyUpgrade(A1, "lancer", "lancer_extended_mag");
        Check.Equal((int)Math.Round(rifle.magazine * 1.25f), h.Combat.Weapons.Magazine(A1, rifle), "Extended Mag: a quarter more rounds");

        // Long Barrel: full damage 30% further out.
        h.Place(B1, 0f, rifle.falloffStart * 1.2f);
        h.NoShield(B1);
        h.Run(0.2f);
        float hp = h.Health(B1);
        h.FireAt(A1, h.ChestOf(B1));
        float plain = hp - h.Health(B1);
        h.Heroes.ApplyUpgrade(A1, "lancer", "lancer_long_barrel");
        h.NoShield(B1);
        h.Run(0.2f);
        hp = h.Health(B1);
        h.FireAt(A1, h.ChestOf(B1));
        Check.Near(rifle.damage * 1.08f, hp - h.Health(B1), "Long Barrel: no falloff at 1.2x the old start, and 8% more", 0.01f);
        Check.True(plain < rifle.damage, "where it fell off before (" + plain + ")");
    }

    // ---- Over the network ----

    [Test]
    static void ScriptedMatchWithHeroesAgreesEverywhere()
    {
        var h = new SessionHarness(ModeDef.CaptureAndHold(), heroes: true);
        var scripts = new List<ScriptedPlayer>();
        for (int i = 0; i < 10; i++)
        {
            var c = h.Join();
            var s = new ScriptedPlayer(c, 10);
            scripts.Add(s);
            c.SnapshotReceived += s.Step;
        }
        h.RunTo(MatchPhase.MatchEnd, 60000);
        h.Ticks(4);
        var s0 = h.Match.State;
        var used = h.Events.History.OfType<AbilityUsedEvent>().ToList();
        Check.True(used.Select(e => e.abilityId).Distinct().Count() >= 8, "most abilities were used: " + string.Join(",", used.Select(e => e.abilityId).Distinct()));
        Check.True(used.Any(e => e.slot == 2), "an ultimate was used (levels " + string.Join(",", s0.players.Select(p => p.level))
            + ", charge " + string.Join(",", h.Clients[0].Snapshot.abilities.units.Select(u => (int)u.ultimateCharge)) + ", time " + (int)s0.elapsed + ")");
        Check.True(h.Events.History.OfType<UpgradeChosenEvent>().Count() >= 20, "upgrades were chosen");
        Check.True(h.Events.History.OfType<ObjectSpawnedEvent>().Any(), "objects were placed");
        Check.True(s0.players.Any(p => p.level >= 5), "someone reached level 5");
        string truth = SessionDigest.Of(s0);
        for (int i = 0; i < h.Clients.Count; i++)
            Check.Equal(truth, SessionDigest.Of(h.Clients[i].State), "client " + (i + 1) + " agrees with the host");
        var snap = h.Clients[0].Snapshot;
        Check.True(snap.progression != null && snap.progression.players.Count == 10, "levels reach the clients");
        Check.True(snap.abilities.units.Any(u => u.changes.Count > 0), "talents reach the clients");
    }
}
