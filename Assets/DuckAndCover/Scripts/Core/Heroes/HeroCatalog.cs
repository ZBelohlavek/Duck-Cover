using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// The heroes a match can use, and the check that each one's data is complete: every choice level
    /// has its options, every option exists and is the right kind, and talents only touch the hero's
    /// own abilities. The hero data tests run the same check on Data/Resources/Heroes.
    /// </summary>
    public sealed class HeroCatalog
    {
        public readonly List<HeroDef> heroes = new List<HeroDef>();

        /// <summary>
        /// Used for a player whose hero id isn't in the catalog (tests, or a hero still being authored),
        /// so they still level and choose. Empty = such players level but get no options.
        /// </summary>
        public string fallbackHeroId = "";

        public HeroCatalog() { }
        public HeroCatalog(IEnumerable<HeroDef> heroes, string fallbackHeroId = "")
        {
            if (heroes != null) this.heroes.AddRange(heroes);
            this.fallbackHeroId = fallbackHeroId ?? "";
        }

        public HeroDef Get(string heroId)
        {
            if (string.IsNullOrEmpty(heroId)) return null;
            foreach (var h in heroes) if (h != null && h.id == heroId) return h;
            return null;
        }

        /// <summary>The hero, or the fallback hero, or null.</summary>
        public HeroDef Resolve(string heroId) => Get(heroId) ?? Get(fallbackHeroId);

        /// <summary>Problems in every hero against the leveling rules; empty when the catalog is usable.</summary>
        public List<string> Validate(ProgressionRuleset rules)
        {
            var errors = new List<string>();
            var ids = new HashSet<string>();
            foreach (var hero in heroes)
            {
                if (hero == null) { errors.Add("a hero is missing"); continue; }
                if (!ids.Add(hero.id ?? "")) errors.Add("hero '" + hero.id + "' is defined twice");
                errors.AddRange(ValidateHero(hero, rules));
            }
            if (!string.IsNullOrEmpty(fallbackHeroId) && Get(fallbackHeroId) == null)
                errors.Add("fallback hero '" + fallbackHeroId + "' is not in the catalog");
            return errors;
        }

        public static List<string> ValidateHero(HeroDef hero, ProgressionRuleset rules)
        {
            var errors = new List<string>();
            string who = "hero '" + hero.id + "'";
            if (string.IsNullOrEmpty(hero.id)) errors.Add("a hero needs an id");
            if (string.IsNullOrEmpty(hero.displayName)) errors.Add(who + " needs a displayName");
            if (string.IsNullOrEmpty(hero.role)) errors.Add(who + " needs a role");
            if (string.IsNullOrEmpty(hero.weaponId)) errors.Add(who + " needs a weaponId");
            if (hero.abilities == null || hero.abilities.Length != 2) errors.Add(who + " needs exactly two abilities");
            else foreach (var a in hero.abilities) if (string.IsNullOrEmpty(a)) errors.Add(who + " has an empty ability id");
            if (string.IsNullOrEmpty(hero.ultimateId)) errors.Add(who + " needs an ultimateId");
            if (hero.maxHealth < 0f) errors.Add(who + " has a negative maxHealth");

            // Upgrades: unique ids, a known kind, something to do, and only the hero's own abilities.
            var upgradeIds = new HashSet<string>();
            foreach (var u in hero.upgrades ?? new UpgradeDef[0])
            {
                if (u == null) { errors.Add(who + " has a missing upgrade"); continue; }
                string what = who + " upgrade '" + u.id + "'";
                if (string.IsNullOrEmpty(u.id)) errors.Add(who + " has an upgrade without an id");
                else if (!upgradeIds.Add(u.id)) errors.Add(what + " is defined twice");
                if (string.IsNullOrEmpty(u.displayName)) errors.Add(what + " needs a displayName");
                var kind = u.Kind;
                if (kind == UpgradeKind.None) errors.Add(what + " has kind '" + u.kind + "', not trait, talent, ultimate, passive or capstone");
                int stats = u.stats == null ? 0 : u.stats.Length;
                int changes = u.abilityChanges == null ? 0 : u.abilityChanges.Length;
                if (stats == 0 && changes == 0 && string.IsNullOrEmpty(u.scriptId)) errors.Add(what + " does nothing (no stats, abilityChanges or scriptId)");
                if (u.stats != null)
                    foreach (var s in u.stats)
                    {
                        if (s == null || string.IsNullOrEmpty(s.stat)) { errors.Add(what + " has a stat change without a stat"); continue; }
                        if (!ModOp.IsKnown(s.op) || s.op == ModOp.Set) errors.Add(what + " changes " + s.stat + " with op '" + s.op + "' (use add, percent or multiply)");
                        if (!StatConditions.IsKnown(s.when)) errors.Add(what + " has unknown condition '" + s.when + "'");
                    }
                if (u.abilityChanges != null)
                {
                    foreach (var c in u.abilityChanges)
                    {
                        if (c == null || string.IsNullOrEmpty(c.ability) || string.IsNullOrEmpty(c.param)) { errors.Add(what + " has an ability change without an ability or param"); continue; }
                        if (!ModOp.IsKnown(c.op)) errors.Add(what + " changes " + c.ability + "." + c.param + " with unknown op '" + c.op + "'");
                        if (!hero.OwnsAbility(c.ability)) errors.Add(what + " changes '" + c.ability + "', which is not one of " + hero.id + "'s abilities");
                        else if (kind == UpgradeKind.Ultimate && c.ability != hero.ultimateId) errors.Add(what + " is an ultimate form but changes '" + c.ability + "'");
                    }
                    if ((kind == UpgradeKind.Trait || kind == UpgradeKind.Passive) && changes > 0)
                        errors.Add(what + " is a " + u.kind + ": traits change stats (passives too), talents change abilities");
                }
                if (kind == UpgradeKind.Talent && changes == 0 && string.IsNullOrEmpty(u.scriptId))
                    errors.Add(what + " is a talent but changes no ability");
            }

            // Levels: exactly the choice levels, each with the right number of distinct options of the right kind.
            var offered = new HashSet<string>();
            var seenLevels = new HashSet<int>();
            foreach (var l in hero.levels ?? new HeroLevelChoice[0])
            {
                if (l == null) { errors.Add(who + " has a missing level entry"); continue; }
                if (!seenLevels.Add(l.level)) { errors.Add(who + " lists level " + l.level + " twice"); continue; }
                var kind = rules.ChoiceAt(l.level);
                if (l.level < 1 || l.level > rules.maxLevel || kind == UpgradeKind.None) { errors.Add(who + " offers options at level " + l.level + ", which has no choice"); continue; }
                int count = l.options == null ? 0 : l.options.Length;
                if (count != rules.optionsPerLevel) errors.Add(who + " level " + l.level + " has " + count + " options, needs " + rules.optionsPerLevel);
                if (l.options == null) continue;
                foreach (var id in l.options)
                {
                    var u = hero.Upgrade(id);
                    if (u == null) { errors.Add(who + " level " + l.level + " offers '" + id + "', which is not in its upgrades"); continue; }
                    if (!offered.Add(id)) errors.Add(who + " offers '" + id + "' at more than one level");
                    if (u.Kind != kind) errors.Add(who + " level " + l.level + " offers " + u.kind + " '" + id + "' where a " + KindName(kind) + " belongs");
                }
            }
            foreach (int level in rules.ChoiceLevels())
                if (!seenLevels.Contains(level)) errors.Add(who + " is missing its level " + level + " " + KindName(rules.ChoiceAt(level)) + " choice");
            foreach (var u in hero.upgrades ?? new UpgradeDef[0])
                if (u != null && !string.IsNullOrEmpty(u.id) && !offered.Contains(u.id)) errors.Add(who + " upgrade '" + u.id + "' is never offered");
            return errors;
        }

        static string KindName(UpgradeKind kind) => kind.ToString().ToLowerInvariant();
    }
}
