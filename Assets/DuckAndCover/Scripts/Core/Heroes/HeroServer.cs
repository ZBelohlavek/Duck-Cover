using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// Puts heroes into a match on the server (WP11): it joins the hero data (<see cref="HeroCatalog"/>)
    /// to the combat rules and the leveling rules, which don't know about each other.
    ///
    /// - A hero pick gives the player that hero's abilities, ultimate, passive and max health
    ///   (weapons come from the weapon table's loadout for the hero id, which WeaponRules equips).
    /// - Leveling runs beside the match: experience every tick, choices by command, levels in snapshots.
    /// - A choice puts the upgrade's stat changes on the hero and its parameter changes on their abilities.
    /// - Damage, healing and shields given to teammates are reported to leveling for experience.
    ///
    /// The host builds one after its <see cref="CombatServer"/> and calls <see cref="Attach"/>.
    /// Tests drive it directly with <see cref="Tick"/>.
    /// </summary>
    public sealed class HeroServer
    {
        public CombatServer Combat { get; }
        public ProgressionRules Progression { get; }
        public HeroCatalog Heroes => Progression.Heroes;

        readonly IGameEvents events;

        /// <summary>The source name an upgrade's stat changes go under on the hero's stat stack.</summary>
        public static string UpgradeSource(string upgradeId) => "upgrade:" + upgradeId;

        public HeroServer(CombatServer combat, HeroCatalog heroes, IWorldQuery world, IGameEvents events)
        {
            Combat = combat ?? throw new ArgumentNullException(nameof(combat));
            this.events = events ?? throw new ArgumentNullException(nameof(events));
            var problems = Validate(heroes ?? throw new ArgumentNullException(nameof(heroes)), combat.Abilities.Library, combat.Weapons.Library);
            if (problems.Count > 0) throw new ArgumentException("Can't run heroes: " + string.Join("; ", problems));
            Progression = new ProgressionRules(combat.Match, heroes, world ?? throw new ArgumentNullException(nameof(world)), events);
            combat.Abilities.UseCheck = UltimateHeld;
            events.Subscribe(OnEvent);
        }

        public void Dispose()
        {
            events.Unsubscribe(OnEvent);
            if (Combat.Abilities.UseCheck == (Func<int, string, string>)UltimateHeld) Combat.Abilities.UseCheck = null;
            Progression.Dispose();
        }

        /// <summary>Hook into a session: upgrade choices, experience each tick, levels in snapshots.</summary>
        public void Attach(SessionHost host)
        {
            host.AddCommandHandler(Progression.Apply);
            host.AddTickHandler(Tick);
            host.AddSnapshotFiller(Progression.AddTo);
        }

        /// <summary>One server tick, after the match and combat ticks.</summary>
        public void Tick(float dt) => Progression.Tick();

        void OnEvent(GameEvent e)
        {
            switch (e)
            {
                case HeroPickedEvent picked:
                    GiveKit(picked.playerId, picked.heroId);
                    break;
                case UpgradeChosenEvent chosen:
                    ApplyUpgrade(chosen.playerId, chosen.heroId, chosen.upgradeId);
                    break;
                case DamagedEvent hit:
                    if (hit.attackerId >= 0 && hit.attackerId != hit.targetId) Progression.RecordDamage(hit.attackerId, hit.targetId);
                    break;
                case HealedEvent heal:
                    Progression.RecordSupport(heal.healerId, heal.targetId, heal.amount);
                    break;
                case ShieldedEvent shield:
                    Progression.RecordSupport(shield.sourceId, shield.targetId, shield.amount);
                    break;
            }
        }

        /// <summary>
        /// Why the ultimate can't be used yet: the hero offers two forms of it and the player hasn't
        /// picked one. Null for everything else (and for heroes with no forms on offer).
        /// </summary>
        public string UltimateHeld(int playerId, string abilityId)
        {
            var p = Combat.Match.State.Player(playerId);
            var hero = p == null ? null : Heroes.Resolve(p.heroId);
            if (hero == null || abilityId != hero.ultimateId) return null;
            if (!OffersUltimateForm(hero, Progression.Rules)) return null;
            return Progression.UltimateForm(playerId) == null ? "pick the ultimate's form first" : null;
        }

        /// <summary>Whether leveling offers this hero a choice of ultimate form (so the ultimate waits for it).</summary>
        public static bool OffersUltimateForm(HeroDef hero, ProgressionRuleset rules)
        {
            int level = rules == null ? 0 : rules.UltimateFormLevel();
            var offer = level > 0 && hero != null ? hero.Level(level) : null;
            return offer != null && offer.options != null && offer.options.Length > 0;
        }

        /// <summary>The hero's abilities (ultimate last), passive and max health. Unknown heroes get the catalog's fallback.</summary>
        public CommandResult GiveKit(int playerId, string heroId)
        {
            var hero = Heroes.Resolve(heroId);
            if (hero == null) return CommandResult.Reject("no hero data for '" + heroId + "'");
            return Combat.Abilities.SetLoadout(playerId, Slots(hero), hero.maxHealth, hero.passiveId);
        }

        /// <summary>Ability slots in order: the two abilities, then the ultimate.</summary>
        public static string[] Slots(HeroDef hero)
        {
            var slots = new List<string>(hero.abilities ?? new string[0]);
            if (!string.IsNullOrEmpty(hero.ultimateId)) slots.Add(hero.ultimateId);
            return slots.ToArray();
        }

        /// <summary>Put an upgrade on a hero: its stats on the stat stack, its changes on their abilities.</summary>
        public CommandResult ApplyUpgrade(int playerId, string heroId, string upgradeId)
        {
            var hero = Heroes.Resolve(heroId);
            var upgrade = hero == null ? null : hero.Upgrade(upgradeId);
            if (upgrade == null) return CommandResult.Reject("hero '" + heroId + "' has no upgrade '" + upgradeId + "'");
            if (upgrade.stats != null && upgrade.stats.Length > 0)
                Combat.Abilities.AddModifiers(playerId, UpgradeSource(upgrade.id), upgrade.stats);
            if (upgrade.abilityChanges != null && upgrade.abilityChanges.Length > 0)
                return Combat.Abilities.ApplyParamChanges(playerId, upgrade.abilityChanges);
            return CommandResult.Accepted;
        }

        /// <summary>
        /// Everything about the heroes that the leveling check alone can't see: their abilities,
        /// ultimate and passive exist and are the right kind, each talent's parameter change fits its
        /// ability, and each hero's weapon is the first in its loadout. Empty when the heroes can run.
        /// </summary>
        public static List<string> Validate(HeroCatalog heroes, AbilityLibrary abilities, WeaponLibrary weapons)
        {
            var errors = new List<string>();
            foreach (var hero in heroes.heroes)
            {
                if (hero == null) continue;
                string who = "hero '" + hero.id + "'";
                foreach (var id in hero.abilities ?? new string[0]) CheckAbility(abilities, id, who, false, false, errors);
                CheckAbility(abilities, hero.ultimateId, who, true, false, errors);
                CheckAbility(abilities, hero.passiveId, who, false, true, errors);
                foreach (var u in hero.upgrades ?? new UpgradeDef[0])
                {
                    if (u == null || u.abilityChanges == null) continue;
                    foreach (var c in u.abilityChanges)
                    {
                        string problem = AbilityParams.Check(c, abilities);
                        if (problem != null) errors.Add(who + " upgrade '" + u.id + "': " + problem);
                    }
                }
                if (weapons != null)
                {
                    if (weapons.Weapon(hero.weaponId) == null) errors.Add(who + " carries unknown weapon '" + hero.weaponId + "'");
                    var loadout = weapons.LoadoutFor(hero.id);
                    if (loadout.Length == 0 || loadout[0] != hero.weaponId)
                        errors.Add(who + "'s weapon loadout should start with " + hero.weaponId + " (it is: " + string.Join(", ", loadout) + ")");
                }
            }
            return errors;
        }

        static void CheckAbility(AbilityLibrary lib, string id, string who, bool ultimate, bool passive, List<string> errors)
        {
            var a = lib.Ability(id);
            if (a == null) { errors.Add(who + " uses unknown ability '" + id + "'"); return; }
            if (a.passive != passive) errors.Add(who + ": '" + id + "' " + (passive ? "is its passive but isn't marked passive" : "is a passive, not something to cast"));
            if (!passive && a.ultimate != ultimate) errors.Add(who + ": '" + id + "' " + (ultimate ? "is its ultimate but isn't marked ultimate" : "is marked ultimate but sits in an ability slot"));
        }
    }
}
