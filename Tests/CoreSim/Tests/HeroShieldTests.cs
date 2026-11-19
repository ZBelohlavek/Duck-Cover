using System.IO;
using System.Linq;
using DuckAndCover.Core;

/// <summary>
/// Hero shields are out of the game (the owner, Nov 19, 2026: "Removed entirely for right now"): no hero
/// starts with a personal shield and nothing in hero data gives one. Bastion shields (Overrun) and Wardens'
/// shields on minions are the modes' own rules and stay. The shield rules themselves still work (see
/// WeaponTests.ShieldsTakeDamageFirstAndComeBack), so shields can come back by data alone.
/// </summary>
static class HeroShieldTests
{
    [Test]
    static void HeroesHaveNoPersonalShield()
    {
        Check.Near(0f, Ruleset.Default.personalShield, "the default rules give heroes no shield of their own");
        var h = new CombatHarness();
        h.Run(5f);
        Check.Near(0f, h.Shield(CombatHarness.A1), "a hero carries no shield");
    }

    [Test]
    static void NoHeroAbilityOrUpgradeGivesAShield()
    {
        foreach (var path in Directory.GetFiles(AbilityHarness.DataDir, "*.json").Where(p => !p.EndsWith("test_abilities.json")))
        {
            var file = AbilityHarness.LoadFile(Path.GetFileName(path));
            foreach (var a in file.abilities)
                Check.False(a.effects.Any(e => e != null && e.kind == EffectKinds.Shield), Path.GetFileName(path) + ": " + a.id + " gives a shield");
            foreach (var o in file.objects)
                Check.False(o.effects.Any(e => e != null && e.kind == EffectKinds.Shield), Path.GetFileName(path) + ": " + o.id + " gives a shield");
        }
        foreach (var path in Directory.GetFiles(ProgressionHarness.HeroesDir, "*.json"))
        {
            var hero = ProgressionHarness.LoadHero(path);
            foreach (var u in hero.upgrades)
                Check.False(u.stats.Any(s => s != null && s.stat == StatIds.ShieldStrength), hero.id + ": " + u.id + " changes shield strength");
        }
    }
}
