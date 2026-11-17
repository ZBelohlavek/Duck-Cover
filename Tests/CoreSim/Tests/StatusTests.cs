using System.Linq;
using DuckAndCover.Core;
using static AbilityHarness;

/// <summary>Status effects: slow, stun, root, silence, stacking, ticking, cleanse and expiry (WP7).</summary>
static class StatusTests
{
    [Test]
    static void SlowKeepsTheStrongestAndHasAFloor()
    {
        var h = new AbilityHarness();
        Check.True(h.Abilities.ApplyStatus(B1, AbilityLibrary.SlowStatus, A1, 0.3f, 2f, abilityId: "x"), "slowed");
        Check.Near(0.7f, h.Abilities.MoveSpeed(B1), "30% slower");
        var e = h.Last<StatusAppliedEvent>();
        Check.Equal("slow", e.statusId, "applied event");
        Check.Near(0.3f, e.strength, "strength in the event");
        Check.Near(2f, e.seconds, "seconds in the event");

        Check.False(h.Abilities.ApplyStatus(B1, AbilityLibrary.SlowStatus, A2, 0.2f, 10f), "a weaker slow is ignored");
        Check.Near(0.7f, h.Abilities.MoveSpeed(B1), "still 30%");
        Check.True(h.Abilities.ApplyStatus(B1, AbilityLibrary.SlowStatus, A2, 0.5f, 1f), "a stronger one takes over");
        Check.Near(0.5f, h.Abilities.MoveSpeed(B1), "now 50%, not 80%: slows don't add up");
        Check.Near(2f, h.Unit(B1).Status(AbilityLibrary.SlowStatus).timeLeft, "and never shortens the time left");

        h.Abilities.ApplyStatus(B1, AbilityLibrary.SlowStatus, A2, 0.95f, 1f);
        Check.Near(0.3f, h.Abilities.MoveSpeed(B1), "never below Ruleset.minMoveSpeed");

        h.Forget();
        h.Run(1.9f);
        Check.True(h.Unit(B1).HasStatus(AbilityLibrary.SlowStatus), "still slowed just before the end");
        h.Run(0.2f);
        Check.Near(1f, h.Abilities.MoveSpeed(B1), "full speed again");
        Check.Equal("expired", h.Last<StatusEndedEvent>().reason, "ended event");
        Check.False(h.Abilities.ApplyStatus(B1, "no_such_status"), "unknown status");
    }

    [Test]
    static void StunSilenceAndRoot()
    {
        var h = new AbilityHarness();
        h.Give(B1, "test_guard");
        h.Abilities.ApplyStatus(B1, AbilityLibrary.StunStatus, A1, 1f, 10f);
        Check.Near(2f, h.Unit(B1).Status(AbilityLibrary.StunStatus).timeLeft, "stuns are capped at Ruleset.maxStunSeconds");
        Check.True(h.Abilities.IsStunned(B1) && h.Abilities.IsSilenced(B1) && h.Abilities.IsRooted(B1), "a stun stops everything");
        Check.Near(0f, h.Abilities.MoveSpeed(B1), "can't move");
        Check.Equal("stunned", h.Use(B1).reason, "can't use abilities");
        h.Run(2.1f);
        Check.False(h.Abilities.IsStunned(B1), "over");
        Check.True(h.Use(B1).IsAccepted, "can act again");

        h.Abilities.ApplyStatus(B2, AbilityLibrary.SilenceStatus, A1);
        h.Give(B2, "test_guard");
        Check.Equal("silenced", h.Use(B2).reason, "silence blocks abilities");
        Check.False(h.Abilities.IsRooted(B2), "but not movement");
        Check.Near(1f, h.Abilities.MoveSpeed(B2), "full speed while silenced");

        h.Abilities.ApplyStatus(B3, AbilityLibrary.RootStatus, A1);
        h.Give(B3, "test_guard");
        Check.Near(0f, h.Abilities.MoveSpeed(B3), "root blocks movement");
        Check.True(h.Use(B3).IsAccepted, "but not abilities");
        Check.False(h.Abilities.IsRevealed(B3), "not revealed");
    }

    [Test]
    static void BurnStacksTicksAndCreditsItsSource()
    {
        var h = new AbilityHarness();
        h.Give(A1, "test_burn_shot"); // 5 damage every 0.5 s for 3 s, stacks to 3
        h.Place(A1, 0); h.Place(B1, 10);
        h.Use(A1);
        h.Run(1.05f);
        Check.Near(190f, h.Health(B1), "two ticks of 5 in the first second");
        h.Use(A1);
        Check.Equal(2, h.Unit(B1).Status("test_burn").stacks, "second hit stacks");
        Check.Near(3f, h.Unit(B1).Status("test_burn").timeLeft, "and refreshes the time");
        h.Run(1.05f); h.Use(A1);
        h.Run(1.05f); h.Use(A1);
        Check.Equal(3, h.Unit(B1).Status("test_burn").stacks, "capped at three stacks");
        float before = h.Health(B1);
        h.Run(0.5f);
        Check.Near(before - 15f, h.Health(B1), "three stacks tick for 15");
        Check.Equal("status:test_burn", h.Last<DamagedEvent>().abilityId, "the damage names the status");
        Check.Equal(A1, h.Last<DamagedEvent>().attackerId, "and credits who applied it");

        h.Run(5f);
        Check.False(h.Unit(B1).HasStatus("test_burn"), "burns out 3 s after the last hit");
        float after = h.Health(B1);
        h.Run(2f);
        Check.Near(after, h.Health(B1), "and stops ticking");

        h.Combat.Damage(-1, B1, h.Health(B1) - 4f);
        h.Use(A1);
        h.Run(0.55f);
        Check.False(h.S.IsAlive(B1), "a tick can eliminate");
        Check.Equal(1, h.S.Player(A1).eliminations, "credited to the burner");
        Check.Equal(0, h.Unit(B1).statuses.Count, "statuses are cleared on elimination");
        h.Run(15f);
        Check.Equal(0, h.Unit(B1).statuses.Count, "and don't come back on respawn");
    }

    [Test]
    static void StackingModes()
    {
        var h = new AbilityHarness(extra: lib =>
        {
            lib.Add(new StatusDef { id = "once", duration = 5f, stacking = Stacking.Ignore, stats = new[] { new StatModifier(StatIds.DamageTaken, ModOp.Multiply, 0.5f) } });
            lib.Add(new StatusDef { id = "ramp", duration = 5f, stacking = Stacking.Stack, maxStacks = 3, stats = new[] { new StatModifier(StatIds.DamageDealt, ModOp.Percent, 0.1f), new StatModifier(StatIds.DamageTaken, ModOp.Multiply, 0.5f) } });
            lib.Add(new StatusDef { id = "fresh", duration = 5f, stacking = Stacking.Refresh, scaleWithStrength = true, stats = new[] { new StatModifier(StatIds.MaxHealth, ModOp.Add, 1f) } });
        });
        Check.True(h.Abilities.ApplyStatus(A1, "once"), "first");
        h.Run(2f);
        Check.False(h.Abilities.ApplyStatus(A1, "once"), "'ignore' refuses a repeat");
        Check.Near(3f, h.Unit(A1).Status("once").timeLeft, "and the timer keeps running", 0.01f);
        h.Abilities.RemoveStatus(A1, "once");

        h.Abilities.ApplyStatus(A1, "ramp");
        h.Abilities.ApplyStatus(A1, "ramp");
        Check.Near(1.2f, h.Abilities.Stat(A1, StatIds.DamageDealt), "percent stats scale with stacks");
        Check.Near(0.25f, h.Abilities.Stat(A1, StatIds.DamageTaken), "multipliers compound per stack");
        h.Abilities.RemoveStatus(A1, "ramp");
        Check.Near(1f, h.Abilities.Stat(A1, StatIds.DamageDealt), "gone with the status");

        h.Abilities.ApplyStatus(A1, "fresh", A2, 50f);
        Check.Near(250f, h.Combat.MaxHealth(A1), "strength scales the stat");
        Check.Near(250f, h.Health(A1), "a max health status grants the health");
        h.Run(2f);
        h.Abilities.ApplyStatus(A1, "fresh", A2, 20f);
        Check.Near(220f, h.Combat.MaxHealth(A1), "'refresh' replaces strength, even with a weaker one");
        Check.Near(5f, h.Unit(A1).Status("fresh").timeLeft, "and restarts the timer");
        h.Run(5.1f);
        Check.Near(200f, h.Health(A1), "health is clamped when it ends");
    }

    [Test]
    static void StatusDurationStatAndHaste()
    {
        var h = new AbilityHarness();
        h.Abilities.AddModifiers(A1, "t", new[] { new StatModifier(StatIds.StatusDuration, ModOp.Percent, 0.5f) });
        h.Abilities.ApplyStatus(B1, AbilityLibrary.SlowStatus, A1, 0.3f, 2f);
        Check.Near(3f, h.Unit(B1).Status(AbilityLibrary.SlowStatus).timeLeft, "the source's statusDuration stretches it");

        h.Give(A2, "test_guard");
        h.Use(A2);
        h.Abilities.ApplyStatus(A2, "test_haste", A2);
        Check.Near(1.25f, h.Abilities.MoveSpeed(A2), "haste speeds movement");
        h.Run(6.05f);
        Check.False(h.Unit(A2).HasStatus("test_haste"), "haste over after 6 s");
        Check.Near(3f, h.Unit(A2).slots[0].rechargeLeft, "6 s at 1.5x recovered 9 of the 12 s cooldown", 0.1f);
        Check.Equal(0, h.Abilities.Cleanse(B2), "nothing to cleanse");
    }
}
