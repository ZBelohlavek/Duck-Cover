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
        /// <summary>The source name of the automatic boost from levels that offer no choice.</summary>
        public const string LevelBoostSource = "levelBoost";

        public HeroServer(CombatServer combat, HeroCatalog heroes, IWorldQuery world, IGameEvents events)
        {
            Combat = combat ?? throw new ArgumentNullException(nameof(combat));
            this.events = events ?? throw new ArgumentNullException(nameof(events));
            var problems = Validate(heroes ?? throw new ArgumentNullException(nameof(heroes)), combat.Abilities.Library, combat.Weapons.Library);
            if (problems.Count > 0) throw new ArgumentException("Can't run heroes: " + string.Join("; ", problems));
            Progression = new ProgressionRules(combat.Match, heroes, world ?? throw new ArgumentNullException(nameof(world)), events);
            events.Subscribe(OnEvent);
        }

        public void Dispose()
        {
            events.Unsubscribe(OnEvent);
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
                case LevelUpEvent up:
                    ApplyLevelBoost(up.playerId, up.level);
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

        /// <summary>The hero's abilities (E, F, then X), passive and max health. Unknown heroes get the catalog's fallback.</summary>
        public CommandResult GiveKit(int playerId, string heroId)
        {
            var hero = Heroes.Resolve(heroId);
            if (hero == null) return CommandResult.Reject("no hero data for '" + heroId + "'");
            return Combat.Abilities.SetLoadout(playerId, Slots(hero), hero.maxHealth, hero.passiveId);
        }

        /// <summary>Ability slots in order: the two abilities, then the third (X).</summary>
        public static string[] Slots(HeroDef hero)
        {
            var slots = new List<string>(hero.abilities ?? new string[0]);
            if (!string.IsNullOrEmpty(hero.ultimateId)) slots.Add(hero.ultimateId);
            return slots.ToArray();
        }

        /// <summary>
        /// The automatic boost for levels without a choice: a little max health and damage for each one
        /// reached. Replaces the earlier boost, so it is the same however the levels arrived.
        /// </summary>
        public void ApplyLevelBoost(int playerId, int level)
        {
            var r = Progression.Rules;
            int n = r.BoostLevels(level);
            if (n <= 0) return;
            Combat.Abilities.AddModifiers(playerId, LevelBoostSource, new[]
            {
                new StatModifier(StatIds.MaxHealth, ModOp.Percent, r.levelHealthBonus * n),
                new StatModifier(StatIds.DamageDealt, ModOp.Percent, r.levelDamageBonus * n),
            });
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
        /// third ability and passive exist and are the right kind, each talent's parameter change fits its
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
                if (!string.IsNullOrEmpty(hero.passiveId)) CheckAbility(abilities, hero.passiveId, who, false, true, errors);
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

        static void CheckAbility(AbilityLibrary lib, string id, string who, bool thirdSlot, bool passive, List<string> errors)
        {
            var a = lib.Ability(id);
            if (a == null) { errors.Add(who + " uses unknown ability '" + id + "'"); return; }
            if (a.passive != passive) errors.Add(who + ": '" + id + "' " + (passive ? "is its passive but isn't marked passive" : "is a passive, not something to cast"));
        }
    }
}
