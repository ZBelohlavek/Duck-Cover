using DuckAndCover.Core;

/// <summary>The stat modifier stack (WP7).</summary>
static class StatTests
{
    [Test]
    static void FormulaIsAddThenPercentThenMultiply()
    {
        var s = new StatBlock();
        s.SetBase(StatIds.MaxHealth, 200f);
        Check.Near(200f, s.Get(StatIds.MaxHealth), "base");
        s.Add("a", new StatModifier(StatIds.MaxHealth, ModOp.Add, 50f));
        s.Add("b", new StatModifier(StatIds.MaxHealth, ModOp.Percent, 0.1f));
        s.Add("c", new StatModifier(StatIds.MaxHealth, ModOp.Percent, 0.1f));
        s.Add("d", new StatModifier(StatIds.MaxHealth, ModOp.Multiply, 0.5f));
        Check.Near((200f + 50f) * 1.2f * 0.5f, s.Get(StatIds.MaxHealth), "(base + add) * (1 + percents) * multipliers");
        Check.Near(1f, s.Get(StatIds.MoveSpeed), "multiplier stats start at 1");
        Check.Near(0f, s.Get("somethingNew"), "unknown stats start at 0");
        s.Add("e", new StatModifier(StatIds.MoveSpeed, ModOp.Percent, -3f));
        Check.Near(0f, s.Get(StatIds.MoveSpeed), "percents can't go below -100%");
    }

    [Test]
    static void SourcesAndConditions()
    {
        var s = new StatBlock();
        s.Add("trait", new[] { new StatModifier(StatIds.MoveSpeed, ModOp.Percent, 0.1f), new StatModifier(StatIds.DamageTaken, ModOp.Multiply, 0.9f, StatConditions.OnHeldPoint) });
        Check.True(s.HasSource("trait"), "source listed");
        Check.Near(1.1f, s.Get(StatIds.MoveSpeed), "trait applies");
        Check.Near(1f, s.Get(StatIds.DamageTaken), "conditional modifier is off without a judge");
        Check.Near(0.9f, s.Get(StatIds.DamageTaken, when => when == StatConditions.OnHeldPoint), "on when the condition holds");
        Check.Near(1f, s.Get(StatIds.DamageTaken, when => false), "off when it doesn't");

        var copy = s.Clone();
        Check.Equal(2, s.Remove("trait"), "both modifiers of the source removed");
        Check.Near(1f, s.Get(StatIds.MoveSpeed), "back to base");
        Check.Near(1.1f, copy.Get(StatIds.MoveSpeed), "a clone is independent");
        Check.Near(6f, ModOp.Apply(4f, ModOp.Percent, 0.5f), "ModOp.Apply percent");
        Check.Near(3f, ModOp.Apply(4f, ModOp.Set, 3f), "ModOp.Apply set");
    }

    [Test]
    static void ConditionsAreJudgedAgainstTheMatch()
    {
        var h = new AbilityHarness();
        int a = AbilityHarness.A1;
        h.Abilities.AddModifiers(a, "dug_in", new[] { new StatModifier(StatIds.DamageTaken, ModOp.Multiply, 0.5f, StatConditions.OnHeldPoint) });
        h.Abilities.AddModifiers(a, "last_stand", new[] { new StatModifier(StatIds.MoveSpeed, ModOp.Percent, 0.2f, StatConditions.LowHealth) });
        h.Abilities.AddModifiers(a, "braced", new[] { new StatModifier(StatIds.DamageDealt, ModOp.Percent, 0.1f, StatConditions.Shielded) });
        h.Abilities.AddModifiers(a, "anchor", new[] { new StatModifier(StatIds.CaptureSpeed, ModOp.Percent, 0.3f, StatConditions.OnPoint) });

        Check.Near(1f, h.Abilities.Stat(a, StatIds.DamageTaken), "not on a point");
        h.World.OnZone[a] = MatchHarness.Mid;
        Check.Near(1.3f, h.Abilities.Stat(a, StatIds.CaptureSpeed), "on a point, whoever holds it");
        Check.Near(1f, h.Abilities.Stat(a, StatIds.DamageTaken), "the point isn't held yet");
        h.S.Zone(MatchHarness.Mid).owner = TeamId.A;
        Check.Near(0.5f, h.Abilities.Stat(a, StatIds.DamageTaken), "on a held point");
        Check.Near(100f, h.Combat.Damage(AbilityHarness.B1, a, 200f), "half damage while dug in");

        Check.Near(1.2f, h.Abilities.Stat(a, StatIds.MoveSpeed), "at half health: low health bonus");
        Check.Near(1f, h.Abilities.Stat(a, StatIds.DamageDealt), "no shield");
        h.Combat.AddShield(a, a, 10f, 5f);
        Check.Near(1.1f, h.Abilities.Stat(a, StatIds.DamageDealt), "shielded");
    }
}
