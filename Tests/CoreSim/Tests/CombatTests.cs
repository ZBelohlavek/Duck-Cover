using System.Linq;
using DuckAndCover.Core;
using static AbilityHarness;

/// <summary>Health, shields, damage and healing (the part of Combat the ability rules need, WP7).</summary>
static class CombatTests
{
    [Test]
    static void DamageScalesAndEliminates()
    {
        var h = new AbilityHarness();
        Check.Near(200f, h.Health(B1), "starts at the default max health");
        Check.Near(50f, h.Combat.Damage(A1, B1, 50f, "x"), "plain damage");
        var e = h.Last<DamagedEvent>();
        Check.Equal(A1, e.attackerId, "attacker");
        Check.Near(150f, e.healthLeft, "health left in the event");
        Check.Equal("x", e.abilityId, "ability id");

        h.Abilities.AddModifiers(A1, "t", new[] { new StatModifier(StatIds.DamageDealt, ModOp.Percent, 0.5f) });
        h.Abilities.AddModifiers(B1, "t", new[] { new StatModifier(StatIds.DamageTaken, ModOp.Multiply, 0.5f) });
        Check.Near(30f, h.Combat.Damage(A1, B1, 40f), "40 * 1.5 dealt * 0.5 taken");
        Check.Near(0f, h.Combat.Damage(A1, A2, 40f), "no friendly fire");
        Check.Near(200f, h.Health(A2), "teammate untouched");
        Check.Near(10f, h.Combat.Damage(-1, A2, 10f), "the world can hurt anyone");

        h.Forget();
        Check.Near(120f, h.Combat.Damage(A2, B1, 999f), "overkill is capped at the health left");
        Check.False(h.S.IsAlive(B1), "eliminated at zero");
        Check.Equal(1, h.S.Player(A2).eliminations, "the last hit is credited");
        var names = h.Events.History.Select(x => x.GetType().Name).ToList();
        Check.True(names.IndexOf(nameof(DamagedEvent)) < names.IndexOf(nameof(PlayerEliminatedEvent)), "damage is published before the elimination");
        Check.True(h.Last<DamagedEvent>().eliminated, "the damage event says it was the final blow");
        Check.Near(0f, h.Combat.Damage(A1, B1, 10f), "can't hurt someone who is down");
        Check.Near(0f, h.Combat.Heal(B2, B1, 10f), "or heal them");

        h.Run(12f);
        Check.True(h.S.IsAlive(B1), "respawned");
        Check.Near(200f, h.Health(B1), "at full health");
    }

    [Test]
    static void FriendlyFireIsARulesetSwitch()
    {
        var r = Ruleset.Default;
        r.friendlyFire = true;
        var h = new AbilityHarness(rules: r);
        Check.Near(40f, h.Combat.Damage(A1, A2, 40f), "teammates take damage when it is on");
    }

    [Test]
    static void ShieldsAbsorbFirstAndExpire()
    {
        var h = new AbilityHarness();
        Check.Near(60f, h.Combat.AddShield(B2, B1, 60f, 4f, "long"), "shield added");
        Check.Near(30f, h.Combat.AddShield(B3, B1, 30f, 1f, "short"), "a second source stacks");
        var s = h.Last<ShieldedEvent>();
        Check.Near(90f, s.shieldNow, "total shield in the event");

        Check.Near(40f, h.Combat.Damage(A1, B1, 40f), "damage taken");
        Check.Near(200f, h.Health(B1), "all of it by shields");
        Check.Near(40f, h.Last<DamagedEvent>().absorbed, "absorbed in the event");
        Check.Near(50f, h.Unit(B1).Shield, "the shield about to expire went first (30), then 10 of the long one");
        Check.Equal(1, h.Unit(B1).shields.Count, "the spent shield is gone");

        Check.Near(70f, h.Combat.Damage(A1, B1, 70f), "spills over into health");
        Check.Near(180f, h.Health(B1), "50 shield, 20 health");

        h.Combat.AddShield(B2, B1, 60f, 4f, "long");
        h.Combat.AddShield(B2, B1, 40f, 4f, "long");
        Check.Near(40f, h.Unit(B1).Shield, "the same source and ability replaces, not stacks");
        Check.Near(110f, h.Combat.AddShield(B3, B1, 500f, 4f, "big"), "capped at Ruleset.maxShield (150) in total");
        h.Run(3.9f);
        Check.True(h.Unit(B1).Shield > 0f, "still up before 4 s");
        h.Run(0.2f);
        Check.Near(0f, h.Unit(B1).Shield, "expired");

        h.Abilities.AddModifiers(B2, "t", new[] { new StatModifier(StatIds.ShieldStrength, ModOp.Percent, 0.5f) });
        Check.Near(30f, h.Combat.AddShield(B2, B1, 20f, 0f, "x"), "shieldStrength scales it");
        Check.Near(4f, h.Last<ShieldedEvent>().seconds, "no duration means the ruleset's default");
    }

    [Test]
    static void HealingClampsAndScales()
    {
        var h = new AbilityHarness();
        Check.Near(0f, h.Combat.Heal(A2, A1, 50f), "nothing to heal at full health");
        h.Combat.Damage(B1, A1, 100f);
        h.Forget();
        Check.Near(30f, h.Combat.Heal(A2, A1, 30f, "patch"), "plain heal");
        Check.Near(130f, h.Last<HealedEvent>().healthNow, "health in the event");
        h.Abilities.AddModifiers(A2, "t", new[] { new StatModifier(StatIds.HealingDone, ModOp.Percent, 0.5f) });
        h.Abilities.AddModifiers(A1, "t", new[] { new StatModifier(StatIds.HealingTaken, ModOp.Multiply, 2f) });
        Check.Near(60f, h.Combat.Heal(A2, A1, 20f), "20 * 1.5 done * 2 taken");
        Check.Near(10f, h.Combat.Heal(A2, A1, 500f), "the event counts only what was restored");
        Check.Near(10f, h.Last<HealedEvent>().amount, "no overheal reported");
    }

    [Test]
    static void MaxHealthChangesAndRegen()
    {
        var h = new AbilityHarness();
        h.Combat.Damage(B1, A1, 50f);
        h.Abilities.AddModifiers(A1, "sturdy", new[] { new StatModifier(StatIds.MaxHealth, ModOp.Add, 25f) });
        Check.Near(225f, h.Combat.MaxHealth(A1), "max health up");
        Check.Near(175f, h.Health(A1), "the extra health is given at once");
        h.Abilities.AddModifiers(A1, "sturdy", new[] { new StatModifier(StatIds.MaxHealth, ModOp.Add, 25f) });
        Check.Near(225f, h.Combat.MaxHealth(A1), "the same source again replaces, it doesn't double");
        h.Combat.Heal(A2, A1, 999f);
        h.Abilities.RemoveModifiers(A1, "sturdy");
        Check.Near(200f, h.Health(A1), "health is clamped when max health drops");

        h.Abilities.AddModifiers(A1, "regen", new[] { new StatModifier(StatIds.HealthRegen, ModOp.Add, 10f) });
        h.Combat.Damage(B1, A1, 100f);
        h.Forget();
        h.Run(4.5f);
        Check.Near(100f, h.Health(A1), "no regen for 5 s after damage");
        h.Run(2.5f);
        Check.Near(120f, h.Health(A1), "then 10 a second", 1.1f);
        Check.Equal(0, h.Of<HealedEvent>().Count, "passive regen publishes no heal events");
        h.Run(30f);
        Check.Near(200f, h.Health(A1), "regen stops at max");

        h.Abilities.SetLoadout(A2, new string[0], 300f);
        Check.Near(300f, h.Health(A2), "a hero's own max health from the loadout");
    }
}
