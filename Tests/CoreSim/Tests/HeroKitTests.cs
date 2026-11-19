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
            // The passive is an upgrade pick, except for a hero who asked for one in the base kit (Skulk's
            // Jungle Stalker): then it takes F's place, and E and X are the abilities.
            if (h.abilities.Length == 2) Check.Equal("", h.passiveId, id + ": no passive in the base kit");
            else
            {
                Check.Equal(1, h.abilities.Length, id + ": E and X besides the passive");
                Check.True(lib.Ability(h.passiveId) != null && lib.Ability(h.passiveId).passive && !string.IsNullOrEmpty(lib.Ability(h.passiveId).description), id + ": its base passive is described");
            }
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
        lancer.Upgrade("lancer_twin_marks").abilityChanges[0].param = "effect.push.amount";
        var problems = HeroServer.Validate(catalog, lib, StarterWeapons.Library());
        Check.True(problems.Any(p => p.Contains("loadout should start with shotgun")), "a weapon that isn't the loadout's first");
        Check.True(problems.Any(p => p.Contains("lancer_twin_marks") && p.Contains("no such parameter")), "a talent changing something the ability lacks");
    }

    [Test]
    static void TheThirdAbilityMayUseACooldown()
    {
        var catalog = ProgressionHarness.DataCatalog();
        var lancer = catalog.Get("lancer");
        lancer.abilities = new[] { "lancer_slip" };
        lancer.ultimateId = "lancer_mark";
        var problems = HeroServer.Validate(catalog, DataLibrary(), StarterWeapons.Library());
        Check.False(problems.Any(p => p.Contains("lancer_mark")), "a cooldown ability is fine as the third: " + string.Join("; ", problems));
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
    static void EveryHeroHasThreeAbilitiesFromLevelOne()
    {
        foreach (var heroId in StarterIds)
        {
            var h = With(A1, heroId, B1, "bulwark");
            Check.Equal(1, h.S.Player(A1).level, heroId + " starts at level 1");
            var unit = h.Unit(A1);
            bool basePassive = !string.IsNullOrEmpty(unit.passiveId);
            Check.Equal(3, unit.slots.Count + (basePassive ? 1 : 0), heroId + " has three abilities (a base passive counts as one)");
            int x = AbilityKeys.SlotFor(AbilityKeys.X, unit.slots.Count, basePassive);
            Check.Equal(unit.slots.Count - 1, x, heroId + ": X is the last slot");
            Check.True(h.Use(A1, 0, Ahead).IsAccepted, heroId + ": E works at level 1");
            h.Run(2f);
            int f = AbilityKeys.SlotFor(AbilityKeys.F, unit.slots.Count, basePassive);
            if (basePassive) Check.Equal(-1, f, heroId + ": the base passive takes F's place");
            else
            {
                Check.True(h.Use(A1, f, Ahead).IsAccepted, heroId + ": F works at level 1");
                h.Run(2f);
            }
            if (h.Abilities.AbilityIn(A1, x).targeting.needsTarget)
            {
                // A buff for a teammate (Frenzy Cap) waits for one in its sights and spends nothing meanwhile.
                Check.Equal("no target in reach", h.Use(A1, x, Ahead).reason, heroId + ": X needs a teammate to aim at");
                h.Place(A1, 0f, 0f);
                h.Place(A2, 0f, 6f);
                h.Tick();
                Check.True(h.UseAt(A1, x, A2).IsAccepted, heroId + ": X works at level 1 on a teammate");
                continue;
            }
            Check.True(h.Use(A1, x, Ahead).IsAccepted, heroId + ": X runs on a cooldown and works at level 1");
        }
    }

    [Test]
    static void UltimateUpgradeAtLevelEightIsOptional()
    {
        var h = With(A1, "lancer", B1, "bulwark");
        h.LevelTo(A1, h.Heroes.Progression.Rules.UltimateFormLevel());
        Check.True(h.Use(A1, 2, Ahead).IsAccepted, "usable at level 8 before its upgrade is picked");
        h.Run(30f);
        Check.True(h.PickForm(A1, "lancer_thermite").IsAccepted, "pick Thermite");
        Check.True(h.Use(A1, 2, Ahead).IsAccepted, "and after");
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
        h.LevelTo(A1, 4);
        float boost = 1f + 0.02f * h.Heroes.Progression.Rules.BoostLevels(h.Heroes.Progression.Level(A1));
        Check.True(boost > 1f, "levels without a choice have passed");
        Check.Near(300f * boost, h.Combat.Damage.MaxHealth(A1), "each gives 2% max health by itself");
        Check.Near(boost, h.Stat(A1, StatIds.DamageDealt), "and 2% damage");
        Check.True(h.Heroes.Progression.Apply(new ChooseUpgradeCommand { playerId = A1, level = 2, option = 0 }).IsAccepted, "Thick Plating");
        Check.Near(325f * boost, h.Combat.Damage.MaxHealth(A1), "+25 max health");
        Check.Near(325f * boost, h.Health(A1), "given as health at once");
        Check.True(h.Heroes.Progression.Apply(new ChooseUpgradeCommand { playerId = A1, level = 4, option = 1 }).IsAccepted, "Spare Parts");
        Check.True(h.Unit(A1).changes.Any(c => c.ability == "bulwark_drone"), "the drone change is on");
    }

    [Test]
    static void FightingEarnsExperience()
    {
        var h = With(A1, "mender", B1, "lancer");
        h.Place(A1, 0f, 0f);
        h.Place(A2, 0f, 3f);
        h.Unit(A2).health = 100f;
        var before = h.Heroes.Progression.Progress(A1)?.xp ?? 0f;
        Check.True(h.Use(A1, 2, Ahead).IsAccepted, "Mending Rounds");
        Check.True(h.FireAt(A1, h.ChestOf(A2)).IsAccepted, "heal shot");
        Check.True(h.Heroes.Progression.Progress(A1).xp > before, "healing a teammate pays experience");
    }

    // ---- Lancer ----

    [Test]
    static void SlipDashes()
    {
        var h = With(A1, "lancer");
        Check.True(h.Use(A1, 0, Ahead).IsAccepted, "slip");
        Check.True(h.Of<PushedEvent>().Any(p => p.targetId == A1 && Math.Abs(p.distance - 6f) < 1e-3f), "6 m dash");
        Check.False(h.Use(A1, 0, Ahead).IsAccepted, "one charge");
        h.Run(13.1f);
        Check.True(h.Use(A1, 0, Ahead).IsAccepted, "ready again after 13 seconds");
    }

    [Test]
    static void TracerMarkHurtsRevealsAndExposes()
    {
        var h = With(A1, "lancer", B1, "bulwark");
        h.Place(A1, 0f, 0f);
        h.Place(B1, 0f, 20f);
        Check.True(h.UseAt(A1, 1, B1).IsAccepted, "mark");
        Check.Near(300f - 35f, h.Health(B1), "35 damage");
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
        Check.Equal("lancer_firebomb", h.Unit(A1).slots[2].abilityId, "the grenade is the third ability");
        Check.True(h.UseAt(A1, 2, B1).IsAccepted, "throw");
        Check.True(h.Health(B1) <= 300f - 25f + 1e-3f, "25 where it lands");
        h.Run(1f);
        float inFire = h.Health(B1);
        Check.True(inFire < 300f - 25f - 12f, "the fire burns while they stand in it (" + inFire + ")");
        Check.True(h.Unit(B1).HasStatus("lancer_burning"), "Clinging Fire sets them burning");

        h.Place(B1, 0f, 40f); // out of the fire
        h.Forget();
        h.Run(1.6f);
        Check.True(h.Health(B1) < inFire - 5f, "and they keep burning after they leave");
        var ticks = BurnTicks(h, B1);
        float dealt = h.Stat(A1, StatIds.DamageDealt); // the level boost
        Check.True(ticks.Length >= 2 && ticks.All(t => Math.Abs(t - 2f * dealt) < 1e-3f), "2 a tick, twice a second (" + string.Join(",", ticks) + ")");
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
        Check.True(h.Health(B1) <= 300f - 55f + 1e-3f && h.Health(B1) > 300f - 70f, "Thermite: 55 where it lands");
        Check.False(h.Unit(B1).HasStatus("lancer_burning"), "no lingering burn");
        h.Place(B1, 0f, 40f);
        float out_ = h.Health(B1);
        h.Run(2f);
        Check.Near(out_, h.Health(B1), "nothing once they step out");
    }

    [Test]
    static void InfernoBurnsWiderAndLonger()
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
        Check.True(ticks.Length >= 2 && ticks.All(t => Math.Abs(t - 2f * dealt) < 1e-3f), "the burn is no harder: it is an area, so wider and longer instead (" + string.Join(",", ticks) + ")");
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
        h.Run(13.1f);
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
    static void MenderSidearmOnlyHealsWithMendingRounds()
    {
        var h = With(A1, "mender", B1, "lancer");
        h.Place(A1, 0f, 0f);
        h.Place(A2, 0f, 5f);
        h.Place(B1, 0f, 10f);
        h.Unit(A2).health = 100f;
        Check.True(h.FireAt(A1, h.ChestOf(A2)).IsAccepted, "shoot through a teammate");
        Check.Near(100f, h.Health(A2), "a plain round doesn't heal");
        Check.True(h.Health(B1) < 200f, "it flies on and hurts the enemy behind");
        Check.True(h.Combat.Weapons.Weapons(A1).CurrentAmmo < 18, "spent a round");

        Check.True(h.Use(A1, 2, Ahead).IsAccepted, "Mending Rounds");
        var w = h.Combat.Weapons.Weapons(A1);
        Check.Equal(18, w.CurrentAmmo, "reloaded at once");
        Check.Equal(18, w.healRounds, "with a full magazine of healing rounds");
        h.Run(0.2f);
        Check.True(h.FireAt(A1, h.ChestOf(A2)).IsAccepted, "heal shot");
        Check.Near(116f, h.Health(A2), "healed 16");
        Check.Equal(17, w.healRounds, "one healing round used");
        float enemy = h.Health(B1);
        h.Place(A2, 5f, 5f);
        h.Run(0.2f);
        h.FireAt(A1, h.ChestOf(B1));
        Check.Near(enemy, h.Health(B1), "a healing round does no harm to an enemy");

        Check.True(h.Combat.Apply(new ReloadCommand { playerId = A1 }).IsAccepted, "a normal reload");
        h.Run(2f);
        Check.Equal(0, w.healRounds, "swaps back to plain rounds");
        Check.False(h.Use(A1, 2, Ahead).IsAccepted, "and Mending Rounds is on its 6 s cooldown");
        h.Run(4.1f);
        Check.True(h.Use(A1, 2, Ahead).IsAccepted, "ready again after 6 s");
    }

    [Test]
    static void SingleTargetPathHealsHarderAndRicochets()
    {
        var h = With(A1, "mender", B1, "lancer", 3);
        int A3 = HeroHarness.A1 + 2;
        h.Place(A1, 0f, 0f);
        h.Place(A2, 0f, 5f);
        h.Place(A3, 3f, 6f);
        h.Heroes.ApplyUpgrade(A1, "mender", "mender_steady_aim");
        h.Heroes.ApplyUpgrade(A1, "mender", "mender_potent_rounds");
        h.Unit(A2).health = 150f;
        h.Unit(A3).health = 100f;
        Check.True(h.Use(A1, 2, Ahead).IsAccepted, "Mending Rounds");
        h.FireAt(A1, h.ChestOf(A2));
        float one = 16f * 1.08f * 1.25f;
        Check.Near(150f + one, h.Health(A2), "Steady Aim and Potent Rounds: 8% and 25% more");
        Check.Near(100f, h.Health(A3), "no ricochet yet");

        h.Heroes.ApplyUpgrade(A1, "mender", "mender_triage");
        h.Unit(A2).health = 60f; // under half
        h.Run(0.2f);
        h.FireAt(A1, h.ChestOf(A2));
        Check.Near(60f + one * 1.3f, h.Health(A2), "Triage: 30% more on a low teammate");

        h.Heroes.ApplyUpgrade(A1, "mender", "mender_ricochet");
        h.Unit(A2).health = 150f;
        h.Run(0.2f);
        h.FireAt(A1, h.ChestOf(A2));
        Check.Near(100f + one * 0.6f * 1.3f, h.Health(A3), "Ricochet: the round jumps to the hurt teammate nearby for 60%");
        Check.True(h.Of<HealRoundBouncedEvent>().Any(e => e.fromId == A2 && e.toId == A3), "and says so");

        h.Abilities.ApplyStatus(A2, AbilityLibrary.SlowStatus, B1, 0.5f, 5f);
        h.Run(0.2f);
        h.FireAt(A1, h.ChestOf(A2));
        Check.True(h.Unit(A2).HasStatus(AbilityLibrary.SlowStatus), "a healing round leaves the slow");
        h.Heroes.ApplyUpgrade(A1, "mender", "mender_second_opinion");
        h.Run(0.2f);
        h.FireAt(A1, h.ChestOf(A2));
        Check.False(h.Unit(A2).HasStatus(AbilityLibrary.SlowStatus), "Second Opinion: the heal clears it");
    }

    [Test]
    static void LifelineRicochetsTwiceToTheNearestHurt()
    {
        var h = With(A1, "mender", B1, "lancer", 4);
        int A3 = HeroHarness.A1 + 2, A4 = HeroHarness.A1 + 3;
        h.Place(A1, 0f, 0f);
        h.Place(A2, 0f, 5f);
        h.Place(A3, 4f, 5f);
        h.Place(A4, 8f, 5f);
        h.Heroes.ApplyUpgrade(A1, "mender", "mender_ricochet");
        h.Heroes.ApplyUpgrade(A1, "mender", "mender_lifeline");
        h.Unit(A3).health = 100f;
        h.Unit(A4).health = 100f;
        Check.True(h.Use(A1, 2, Ahead).IsAccepted, "Mending Rounds");
        h.FireAt(A1, h.ChestOf(A2));
        Check.True(h.Health(A3) > 100f, "first jump to the next teammate");
        Check.True(h.Health(A4) > 100f, "Lifeline: a second jump on from there");
        Check.Equal(2, h.Of<HealRoundBouncedEvent>().Count(), "two jumps");
    }

    [Test]
    static void SplashRoundsHealAroundTheTeammateHit()
    {
        var h = With(A1, "mender", B1, "lancer", 3);
        int A3 = HeroHarness.A1 + 2;
        h.Place(A1, 0f, 0f);
        h.Place(A2, 0f, 6f);
        h.Place(A3, 1.5f, 6f);
        h.Unit(A2).health = 100f;
        h.Unit(A3).health = 100f;
        h.Heroes.ApplyUpgrade(A1, "mender", "mender_splash_rounds");
        Check.True(h.Use(A1, 2, Ahead).IsAccepted, "Mending Rounds");
        h.FireAt(A1, h.ChestOf(A2));
        Check.Near(116f, h.Health(A2), "the teammate hit");
        Check.Near(108f, h.Health(A3), "and half for the one beside them");
    }

    [Test]
    static void MendingCanisterGivesRegenThatLingers()
    {
        var h = With(A1, "mender");
        h.Place(A1, 0f, 0f);
        h.Place(A2, 0f, 3f);
        h.Unit(A2).health = 100f;
        Check.True(h.UseAt(A1, 0, A2).IsAccepted, "canister");
        h.Run(3.05f);
        Check.True(h.Unit(A2).HasStatus("mender_regen"), "regenerating inside");
        Check.Near(100f + 16f * 2.5f, h.Health(A2), "16 a second once it starts", 8.5f);
        h.Place(A2, 0f, 20f);
        float left = h.Health(A2);
        h.Run(1.05f);
        Check.True(h.Health(A2) > left, "keeps regenerating just after stepping out");
        h.Run(1f);
        Check.False(h.Unit(A2).HasStatus("mender_regen"), "then stops");
        float after = h.Health(A2);
        h.Run(1f);
        Check.Near(after, h.Health(A2), "no more healing outside");
    }

    [Test]
    static void TetheredCanisterFollowsATeammate()
    {
        var h = With(A1, "mender");
        h.Place(A1, 0f, 0f);
        h.Place(A2, 0f, 6f);
        h.Heroes.ApplyUpgrade(A1, "mender", "mender_tethered_canister");
        h.Unit(A2).health = 100f;
        Check.True(h.UseAt(A1, 0, A2).IsAccepted, "canister at the teammate");
        var can = h.Abilities.State.objects.First(o => o.defId == "mender_canister");
        Check.Equal(A2, can.followId, "latched onto the teammate");
        h.Place(A2, 10f, 15f);
        h.Run(1.05f);
        Check.Near(10f, can.position.x, "follows them", 0.01f);
        Check.Near(15f, can.position.z, "follows them", 0.01f);
        Check.True(h.Unit(A2).HasStatus("mender_regen"), "and keeps healing them there");

        var plain = With(A1, "mender");
        plain.Place(A1, 0f, 0f);
        plain.Place(A2, 0f, 6f);
        plain.UseAt(A1, 0, A2);
        Check.Equal(-1, plain.Abilities.State.objects.First(o => o.defId == "mender_canister").followId, "without the talent it stays where it lands");
    }

    [Test]
    static void PurgeCleansesEveryDebuff()
    {
        var h = With(A1, "mender", B1, "rigger");
        h.Place(A1, 0f, 0f);
        h.Place(A2, 0f, 3f);
        h.Abilities.ApplyStatus(A2, AbilityLibrary.SlowStatus, B1, 0.5f, 3f, 0f, "rigger_mine");
        h.Abilities.ApplyStatus(A2, AbilityLibrary.StunStatus, B1, 1f, 0.5f);
        h.Abilities.ApplyStatus(A1, AbilityLibrary.SlowStatus, B1, 0.5f, 3f);
        Check.True(h.Use(A1, 1, Ahead).IsAccepted, "purge");
        Check.False(h.Unit(A2).statuses.Any(s => h.Abilities.Library.Status(s.statusId).negative), "teammate cleared");
        Check.False(h.Unit(A1).HasStatus(AbilityLibrary.SlowStatus), "and Mender too");
        Check.False(h.Unit(A2).HasStatus("mender_ward"), "no ward without the talent");
    }

    [Test]
    static void WardingPurgeBlocksTheNextDebuff()
    {
        var h = With(A1, "mender", B1, "rigger");
        h.Place(A1, 0f, 0f);
        h.Place(A2, 0f, 3f);
        h.Heroes.ApplyUpgrade(A1, "mender", "mender_warding_purge");
        Check.True(h.Use(A1, 1, Ahead).IsAccepted, "purge");
        Check.True(h.Unit(A2).HasStatus("mender_ward"), "teammate warded");
        Check.False(h.Abilities.ApplyStatus(A2, AbilityLibrary.SlowStatus, B1, 0.5f, 3f), "the next slow is blocked");
        Check.False(h.Unit(A2).HasStatus(AbilityLibrary.SlowStatus), "not slowed");
        Check.False(h.Unit(A2).HasStatus("mender_ward"), "the ward is used up");
        Check.True(h.Of<StatusBlockedEvent>().Any(e => e.targetId == A2 && e.wardId == "mender_ward"), "and says so");
        Check.True(h.Abilities.ApplyStatus(A2, AbilityLibrary.SlowStatus, B1, 0.5f, 3f), "the one after lands");

        Check.True(h.Unit(A1).HasStatus("mender_ward"), "Mender is warded too");
        h.Run(8.1f);
        Check.False(h.Unit(A1).HasStatus("mender_ward"), "a ward lasts 8 seconds");
    }

    // ---- Rigger: RiggerKitTests ----

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
        for (int i = 0; i < 8; i++)
        {
            var c = h.Join();
            var s = new ScriptedPlayer(c, 8);
            scripts.Add(s);
            c.SnapshotReceived += s.Step;
        }
        h.RunTo(MatchPhase.MatchEnd, 60000);
        h.Ticks(4);
        var s0 = h.Match.State;
        var used = h.Events.History.OfType<AbilityUsedEvent>().ToList();
        Check.True(used.Select(e => e.abilityId).Distinct().Count() >= 8, "most abilities were used: " + string.Join(",", used.Select(e => e.abilityId).Distinct()));
        Check.True(used.Any(e => e.slot == 2), "a third (X) ability was used (levels " + string.Join(",", s0.players.Select(p => p.level))
            + ", time " + (int)s0.elapsed + ")");
        Check.True(h.Events.History.OfType<UpgradeChosenEvent>().Count() >= 20, "upgrades were chosen");
        Check.True(h.Events.History.OfType<ObjectSpawnedEvent>().Any(), "objects were placed");
        Check.True(s0.players.Any(p => p.level >= 5), "someone reached level 5");
        string truth = SessionDigest.Of(s0);
        for (int i = 0; i < h.Clients.Count; i++)
            Check.Equal(truth, SessionDigest.Of(h.Clients[i].State), "client " + (i + 1) + " agrees with the host");
        var snap = h.Clients[0].Snapshot;
        Check.True(snap.progression != null && snap.progression.players.Count == 8, "levels reach the clients");
        Check.True(snap.abilities.units.Any(u => u.changes.Count > 0), "talents reach the clients");
    }
}
