using System;
using System.Collections.Generic;
using System.Linq;
using DuckAndCover.Core;

/// <summary>
/// The game-wide ability rules (Zach, Nov 19 2026), checked against every hero in the data so a new
/// hero is covered the moment its files land:
/// 1. Every ability runs on a plain cooldown: there are no ultimates and no ultimate charge.
/// 2. No single ability can take a full-health hero to dead: one cast, with its damage over time and the
///    caster's upgrades, against the hero with the lowest max health in the roster.
/// 3. Most abilities recharge in 13 to 20 seconds (Ruleset.abilityCooldownFloor/Ceiling). An ability
///    may sit below that only with a written reason (AbilityDef.shortCooldownReason), and no upgrade may
///    take one below the floor.
/// 4. Area damage is lower than single-target damage: an ability that hits several enemies may deal
///    each of them at most <see cref="AreaShare"/> of the lowest max health, a single-target one at most
///    <see cref="SingleShare"/>; and an area weapon's damage a second is below every single-target
///    hero weapon's.
/// </summary>
static class AbilityRulesTests
{
    const int A1 = HeroHarness.A1, B1 = HeroHarness.B1, B2 = HeroHarness.B2;

    /// <summary>Most of the lowest max health one cast of a single-target ability may deal.</summary>
    public const float SingleShare = 0.75f;
    /// <summary>Most of the lowest max health one cast of an area ability may deal to each enemy it hits.</summary>
    public const float AreaShare = 0.5f;
    /// <summary>
    /// Seconds a target is taken to stand in a lingering area (a fire, a pulse field) before it walks out:
    /// every area here is a few metres across, so 3 seconds is generous. Statuses it left keep running.
    /// </summary>
    public const float ZoneExposure = 3f;

    static readonly float[] Distances = { 2f, 6f, 11.5f };

    static HeroCatalog Catalog() => ProgressionHarness.DataCatalog();

    /// <summary>Every playable hero (the test hero left out).</summary>
    public static List<HeroDef> Roster() => Catalog().heroes.Where(h => h != null && !h.id.StartsWith("test")).ToList();

    static float LowestMaxHealth(out string heroId)
    {
        var r = Ruleset.Default;
        var lowest = Roster().OrderBy(h => h.maxHealth > 0f ? h.maxHealth : r.defaultMaxHealth).ThenBy(h => h.id, StringComparer.Ordinal).First();
        heroId = lowest.id;
        return lowest.maxHealth > 0f ? lowest.maxHealth : r.defaultMaxHealth;
    }

    [Test]
    static void NoUltimatesAnywhere()
    {
        // The strict loader already refuses an "ultimate" key on an ability; upgrades can't lean on charge either.
        foreach (var hero in Roster())
        {
            Check.True(HeroServer.Slots(hero).Length + (string.IsNullOrEmpty(hero.passiveId) ? 0 : 1) == 3, hero.id + " has three abilities (a base passive counts as one)");
            foreach (var u in hero.upgrades)
                foreach (var s in u.stats)
                    Check.True(s.stat != "ultimateChargeRate", hero.id + " upgrade " + u.id + " doesn't change ultimate charge (there is none)");
        }
    }

    [Test]
    static void CooldownsSitBetween13And20Seconds()
    {
        var r = Ruleset.Default;
        var lib = HeroHarness.DataLibrary();
        var exceptions = new List<string>();
        foreach (var hero in Roster())
            foreach (var id in HeroServer.Slots(hero))
            {
                var a = lib.Ability(id);
                Check.True(a.cooldown <= r.abilityCooldownCeiling + 1e-3f, id + ": cooldown " + a.cooldown + " s is at most " + r.abilityCooldownCeiling + " s");
                if (!string.IsNullOrEmpty(a.shortCooldownReason)) { exceptions.Add(id + " (" + a.cooldown + " s): " + a.shortCooldownReason); continue; }
                Check.True(a.cooldown >= r.abilityCooldownFloor - 1e-3f, id + ": cooldown " + a.cooldown + " s is at least " + r.abilityCooldownFloor + " s, or it gives a shortCooldownReason");
                // Upgrades (talents that cut the cooldown, cooldown-rate traits) can't take it under the floor.
                foreach (var build in Builds(hero, id, cooldownOnly: true))
                {
                    var h = Setup(hero.id, build, out var label, null, u => Matters(u, id, true));
                    int slot = Array.IndexOf(HeroServer.Slots(hero), id);
                    float resolved = h.ResolvedDef(A1, id).def.cooldown;
                    float effective = h.Abilities.EffectiveCooldown(A1, slot);
                    Check.True(resolved >= r.abilityCooldownFloor - 1e-3f, id + " " + label + ": upgrades leave its cooldown at " + resolved + " s, under " + r.abilityCooldownFloor + " s");
                    Check.True(effective >= r.abilityCooldownFloor - 1e-3f, id + " " + label + ": comes round in " + effective + " s with cooldown-rate stats");
                }
            }
        Console.WriteLine("        short cooldowns: " + (exceptions.Count == 0 ? "none" : string.Join("; ", exceptions)));
    }

    [Test]
    static void NoAbilityKillsAFullHealthHero()
    {
        float lowest = LowestMaxHealth(out string target);
        foreach (var hero in Roster())
            foreach (var id in HeroServer.Slots(hero))
            {
                var worst = Worst(hero, id, target);
                bool area = worst.area;
                float cap = (area ? AreaShare : SingleShare) * lowest;
                string what = id + " (" + (area ? "area" : "single target") + ", " + worst.label + ")";
                Check.True(worst.damage < lowest, what + " deals " + worst.damage.ToString("0") + " in one cast: it must not kill a full " + lowest + "-health " + target);
                Check.True(worst.damage <= cap + 1e-3f, what + " deals " + worst.damage.ToString("0") + " to one enemy; the cap is " + cap.ToString("0"));
            }
    }

    [Test]
    static void AreaWeaponsDealLessASecondThanSingleTargetOnes()
    {
        var weapons = StarterWeapons.Library();
        var used = Roster().Select(h => weapons.Weapon(h.weaponId)).Where(w => w != null).Distinct().ToList();
        var single = used.Where(w => !IsArea(w)).ToList();
        Check.True(single.Count > 0, "some hero carries a single-target weapon");
        float slowest = single.Min(Dps);
        foreach (var w in used.Where(IsArea))
            Check.True(Dps(w) < slowest, w.id + " hits several enemies, so its " + Dps(w).ToString("0") + " damage a second is below every single-target hero weapon's (lowest " + slowest.ToString("0") + ")");
    }

    static bool IsArea(WeaponDef w) => w.splashRadius > 0f || (w.kind == WeaponKind.Melee && w.maxTargets != 1);
    static float Dps(WeaponDef w) => (w.damage * Math.Max(1, w.pellets) + w.splashDamage) / Math.Max(0.01f, w.fireInterval);

    /// <summary>
    /// Prints every hero's abilities: cooldown, whether they hit an area, and the most one cast deals to
    /// one enemy over the builds the damage test tries. dotnet run --project Tests/CoreSim -- report abilities
    /// </summary>
    public static int Report(string[] args)
    {
        var lib = HeroHarness.DataLibrary();
        float lowest = LowestMaxHealth(out string target);
        Console.WriteLine("Lowest max health: " + lowest + " (" + target + "). Caps: single target " + (SingleShare * lowest) + ", area " + (AreaShare * lowest) + " per enemy.");
        Console.WriteLine(string.Format("{0,-10} {1,-24} {2,6} {3,-6} {4,7}  {5}", "hero", "ability", "cd", "kind", "damage", "worst build"));
        foreach (var hero in Roster())
            foreach (var id in HeroServer.Slots(hero))
            {
                var a = lib.Ability(id);
                var w = Worst(hero, id, target);
                Console.WriteLine(string.Format("{0,-10} {1,-24} {2,6:0.#} {3,-6} {4,7:0}  {5}", hero.id, a.displayName, a.cooldown, w.damage <= 0f ? "-" : w.area ? "area" : "single", w.damage, w.damage <= 0f ? "" : w.label));
            }
        return 0;
    }

    // ---- How one cast is measured ----

    struct Result { public float damage; public bool area; public string label; }

    /// <summary>The most damage one cast deals to one enemy, over builds, distances and two ways of aiming.</summary>
    static Result Worst(HeroDef hero, string abilityId, string targetHero)
    {
        var worst = new Result { label = "no damage" };
        int slot = Array.IndexOf(HeroServer.Slots(hero), abilityId);
        foreach (var build in Builds(hero, abilityId, cooldownOnly: false))
            foreach (float d in Distances)
                foreach (bool atChest in new[] { true, false })
                {
                    var h = Setup(hero.id, build, out var label, targetHero, u => Matters(u, abilityId, false));
                    h.Place(A1, 0f, 0f);
                    h.Place(B1, 0f, d);
                    h.Place(B2, 1.2f, d);
                    h.NoShield(B1); h.NoShield(B2);
                    h.Forget();
                    var used = atChest ? h.UseAt(A1, slot, B1) : h.Use(A1, slot, HeroHarness.Ahead);
                    if (!used.IsAccepted) continue;
                    var def = h.ResolvedDef(A1, abilityId).def;
                    float delay = def.castTime + def.effects.Select(e => e.delay).DefaultIfEmpty(0f).Max();
                    h.Run(delay + ZoneExposure);
                    // They walk out of anything lingering; statuses already on them keep running.
                    h.Place(B1, 300f, 300f);
                    h.Place(B2, -300f, 300f);
                    h.Run(15f);
                    float onB1 = DamageTo(h, B1), onB2 = DamageTo(h, B2);
                    float most = Math.Max(onB1, onB2);
                    bool area = onB1 > 0f && onB2 > 0f;
                    if (most > worst.damage || (area && !worst.area && most >= worst.damage))
                        worst = new Result { damage = most, area = area || worst.area, label = label + ", " + d + " m, " + (atChest ? "aimed at them" : "aimed ahead") };
                    else if (area) worst.area = true;
                }
        return worst;
    }

    static float DamageTo(HeroHarness h, int target) =>
        h.Of<DamagedEvent>().Where(e => e.attackerId == A1 && e.targetId == target).Sum(e => e.amount);

    /// <summary>
    /// The builds worth trying for one ability: no upgrades, and level 20 where every level takes the
    /// option that matters to this ability (changes it, or its damage, area or status length). Where both
    /// options at a level matter, both are tried.
    /// </summary>
    static List<int[]> Builds(HeroDef hero, string abilityId, bool cooldownOnly)
    {
        var levels = hero.levels.OrderBy(l => l.level).ToList();
        var builds = new List<int[]> { null };
        var partial = new List<int[]> { new int[levels.Count] };
        for (int i = 0; i < levels.Count; i++)
        {
            var opts = levels[i].options;
            var relevant = Enumerable.Range(0, opts.Length).Where(o => Matters(hero.Upgrade(opts[o]), abilityId, cooldownOnly)).ToList();
            if (relevant.Count == 0) relevant.Add(0);
            var next = new List<int[]>();
            foreach (var p in partial)
                foreach (int o in relevant)
                {
                    var copy = (int[])p.Clone();
                    copy[i] = o;
                    next.Add(copy);
                }
            partial = next;
        }
        builds.AddRange(partial);
        return builds;
    }

    static readonly string[] DamageStats = { StatIds.DamageDealt, StatIds.StatusDuration, StatIds.AbilityRadius };

    static bool Matters(UpgradeDef u, string abilityId, bool cooldownOnly)
    {
        if (u == null) return false;
        if (cooldownOnly)
            return u.stats.Any(s => s.stat == StatIds.CooldownRate) ||
                   u.abilityChanges.Any(c => c.ability == abilityId && (c.param == "cooldown" || c.param == "charges"));
        return u.abilityChanges.Any(c => c.ability == abilityId) || u.stats.Any(s => DamageStats.Contains(s.stat));
    }

    /// <summary>A fresh duel: A1 plays the hero with the build's picks, B1 and B2 play the target hero.</summary>
    static HeroHarness Setup(string heroId, int[] build, out string label, string targetHero = null, Func<UpgradeDef, bool> show = null)
    {
        var picks = new Dictionary<int, string> { [A1] = heroId };
        if (targetHero != null) { picks[B1] = targetHero; picks[B2] = targetHero; }
        var h = new HeroHarness(picks);
        label = "level 1";
        if (build == null) return h;
        var hero = h.Heroes.Heroes.Resolve(heroId);
        h.LevelTo(A1, h.Heroes.Progression.Rules.maxLevel);
        var levels = hero.levels.OrderBy(l => l.level).ToList();
        var picked = new List<string>();
        for (int i = 0; i < levels.Count; i++)
        {
            var r = h.Heroes.Progression.Choose(A1, levels[i].level, build[i]);
            Check.True(r.IsAccepted, heroId + " picks option " + build[i] + " at level " + levels[i].level + ": " + r.reason);
            var u = hero.Upgrade(levels[i].options[build[i]]);
            if (show == null || show(u)) picked.Add(u.displayName);
        }
        h.Tick();
        label = "level 20" + (picked.Count == 0 ? "" : ": " + string.Join(", ", picked));
        return h;
    }
}
