using System.Linq;
using DuckAndCover.Core;
using static AbilityHarness;

/// <summary>Talent changes to ability parameters, and passives (WP7).</summary>
static class AbilityParamTests
{
    static AbilityParamChange C(string ability, string param, string op, float value, string text = "") =>
        new AbilityParamChange(ability, param, op, value, text);

    static ResolvedAbility Resolve(AbilityLibrary lib, string id, params AbilityParamChange[] changes) =>
        AbilityParams.Resolve(lib.Ability(id), changes, lib, 0.5f);

    [Test]
    static void OpsCombineInAFixedOrder()
    {
        var lib = AbilityLibrary.From(LoadFile());
        var r = Resolve(lib, "test_grenade",
            C("test_grenade", "cooldown", ModOp.Multiply, 0.5f),
            C("test_grenade", "cooldown", ModOp.Add, 2f),
            C("test_grenade", "cooldown", ModOp.Percent, 0.5f),
            C("test_dash", "cooldown", ModOp.Set, 99f));
        Check.Near((10f + 2f) * 1.5f * 0.5f, r.def.cooldown, "(base + add) * (1 + percent) * multiply, whatever the pick order");
        Check.Near(10f, lib.Ability("test_grenade").cooldown, "the library's definition is never changed");

        r = Resolve(lib, "test_grenade", C("test_grenade", "radius", ModOp.Add, 1f), C("test_grenade", "radius", ModOp.Set, 8f), C("test_grenade", "radius", ModOp.Set, 7f));
        Check.Near(7f, r.def.targeting.radius, "a set wins, the last one taken");

        r = Resolve(lib, "test_grenade", C("test_grenade", "count", ModOp.Add, 1.6f), C("test_grenade", "cooldown", ModOp.Multiply, 0.001f));
        Check.Equal(3, r.def.targeting.count, "whole-number parameters are rounded");
        Check.Near(0.5f, r.def.cooldown, "talents can't take a cooldown below the ruleset's floor");
        r = Resolve(lib, "test_grenade", C("test_grenade", "count", ModOp.Add, -5f));
        Check.Equal(1, r.def.targeting.count, "and never fewer than one");
    }

    [Test]
    static void EffectObjectAndTextParams()
    {
        var lib = AbilityLibrary.From(LoadFile());
        var r = Resolve(lib, "test_grenade",
            C("test_grenade", "effect.damage.amount", ModOp.Percent, 0.15f),
            C("test_grenade", "effect.slow.duration", ModOp.Add, 1f),
            C("test_grenade", "effect.damage.delay", ModOp.Set, 0f),
            C("test_grenade", "targeting.affects", ModOp.Set, 0f, Affects.All));
        Check.Near(69f, r.def.effects[0].amount, "effect.damage.amount");
        Check.Near(0f, r.def.effects[0].delay, "effect.damage.delay");
        Check.Near(3f, r.def.effects[1].duration, "effect.slow.duration");
        Check.Near(0.3f, r.def.effects[1].amount, "other fields untouched");
        Check.Equal(Affects.All, r.def.targeting.affects, "text parameter");

        r = Resolve(lib, "test_burn_shot", C("test_burn_shot", "effect.test_burn.duration", ModOp.Set, 5f));
        Check.Near(5f, r.def.effects[0].duration, "an effect can be named by its status id");

        r = Resolve(lib, "test_mine",
            C("test_mine", "object.radius", ModOp.Add, 1f),
            C("test_mine", "object.maxPerOwner", ModOp.Add, 1f),
            C("test_mine", "object.effect.damage.amount", ModOp.Multiply, 2f),
            C("test_mine", "object.affects", ModOp.Set, 0f, Affects.All));
        var mine = r.objects["test_mine"];
        Check.Near(4f, mine.radius, "object.radius");
        Check.Equal(3, mine.maxPerOwner, "object.maxPerOwner");
        Check.Near(80f, mine.effects[0].amount, "object.effect.damage.amount");
        Check.Equal(Affects.All, mine.affects, "object.affects");
        Check.Near(3f, lib.Object("test_mine").radius, "the library's object is never changed");
    }

    [Test]
    static void ChecksCatchBadChanges()
    {
        var lib = AbilityLibrary.From(LoadFile());
        Check.Equal(null, AbilityParams.Check(C("test_grenade", "cooldown", ModOp.Multiply, 0.8f), lib), "a good change");
        Check.Equal(null, AbilityParams.Check(C("test_dash", "enable", ModOp.Set, 0f, "healTrail"), lib), "a tag that exists");
        Check.Equal(null, AbilityParams.Check(C("test_regen", "stat.healthRegen", ModOp.Add, 2f), lib), "a passive's stat");
        Check.Equal(null, AbilityParams.Check(C("test_overdrive", "cooldown", ModOp.Add, -2f), lib), "cooldown on the third ability");
        Check.Equal(null, AbilityParams.Check(C("test_overdrive", "duration", ModOp.Add, 3f), lib), "duration");
        foreach (var bad in new[]
        {
            C("nope", "cooldown", ModOp.Add, 1f),
            C("test_grenade", "cooldwn", ModOp.Add, 1f),
            C("test_grenade", "cooldown", "times", 2f),
            C("test_grenade", "cooldown", ModOp.Add, float.NaN),
            C("test_grenade", "effect.heal.amount", ModOp.Add, 1f),
            C("test_grenade", "object.radius", ModOp.Add, 1f),
            C("test_grenade", "ultimateCost", ModOp.Add, 1f),
            C("test_grenade", "enable", ModOp.Set, 0f, "noSuchTag"),
            C("test_grenade", "targeting.shape", ModOp.Set, 0f, "cube"),
            C("test_grenade", "targeting.shape", ModOp.Add, 1f, "ray"),
            C("test_regen", "stat.moveSpeed", ModOp.Add, 1f),
            null,
        })
            Check.True(AbilityParams.Check(bad, lib) != null, "rejected: " + bad);
    }

    [Test]
    static void TalentsChangeALiveHero()
    {
        var h = new AbilityHarness();
        h.Give(A1, "test_dash", "test_grenade");
        h.Give(A2, "test_dash", "test_grenade");
        h.Use(A1);
        Check.Equal(0, h.Unit(A1).slots[0].charges, "dash used");
        Check.True(h.Abilities.ApplyParamChanges(A1, new[] { C("test_dash", "charges", ModOp.Add, 1f), C("test_grenade", "effect.damage.amount", ModOp.Percent, 0.5f) }).IsAccepted, "talent taken");
        Check.Equal(1, h.Unit(A1).slots[0].charges, "the extra charge is there at once");
        Check.Equal(2, h.Abilities.AbilityIn(A1, 0).charges, "the hero's version has two");
        Check.Equal(1, h.Abilities.AbilityIn(A2, 0).charges, "another hero with the same ability is unaffected");

        h.Place(A1, 0); h.Place(B1, 20);
        h.Use(A1, 1);
        h.Run(1.1f);
        Check.Near(110f, h.Health(B1), "the grenade now does 90");

        var bad = h.Abilities.ApplyParamChanges(A1, new[] { C("test_dash", "cooldown", ModOp.Multiply, 0.5f), C("test_dash", "nonsense", ModOp.Add, 1f) });
        Check.False(bad.IsAccepted, "one bad change refuses the whole set");
        Check.Near(6f, h.Abilities.AbilityIn(A1, 0).cooldown, "so nothing was applied");
        Check.Equal(2, h.Unit(A1).changes.Count, "only the good set is recorded");
    }

    [Test]
    static void SpawnedObjectsKeepTheNumbersTheyWerePlacedWith()
    {
        var h = new AbilityHarness();
        h.Give(A1, "test_mine");
        h.Place(A1, 0);
        h.Use(A1);
        h.Abilities.ApplyParamChanges(A1, new[] { C("test_mine", "object.effect.damage.amount", ModOp.Set, 100f), C("test_mine", "object.radius", ModOp.Set, 6f) });
        h.Use(A1, 0, new Vec3(0, 0, 1));
        var mines = h.Abilities.State.objects;
        Check.Near(3f, mines[0].radius, "the mine placed before the talent keeps its radius");
        Check.Near(6f, mines[1].radius, "the one placed after has the new one");
        Check.Near(100f, mines[1].def.effects[0].amount, "and the new damage");
    }

    [Test]
    static void PassivesAreStatsThatTalentsCanChange()
    {
        var h = new AbilityHarness();
        Check.True(h.Abilities.SetLoadout(A1, new[] { "test_dash" }, 250f, "test_regen").IsAccepted, "loadout with a passive");
        Check.Equal("test_regen", h.Last<LoadoutSetEvent>().passiveId, "loadout event");
        Check.Near(250f, h.Health(A1), "hero max health");
        Check.Near(4f, h.Abilities.Stat(A1, StatIds.HealthRegen), "the passive's stat is on");
        h.Abilities.ApplyParamChanges(A1, new[] { C("test_regen", "stat.healthRegen", ModOp.Add, 2f) });
        Check.Near(6f, h.Abilities.Stat(A1, StatIds.HealthRegen), "a talent changed the passive");
        h.Combat.Damage(-1, A1, 100f);
        h.Run(7f);
        Check.Near(162f, h.Health(A1), "regen at 6 a second after the 5 s delay", 0.7f);

        Check.Equal("'test_dash' is not a passive", h.Abilities.SetLoadout(A1, new string[0], 0f, "test_dash").reason, "an active ability can't be the passive");
        h.Abilities.SetLoadout(A1, new[] { "test_dash" });
        Check.Near(0f, h.Abilities.Stat(A1, StatIds.HealthRegen), "a new loadout without the passive takes it off");
    }
}
