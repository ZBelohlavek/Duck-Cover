using System.IO;
using System.Linq;
using DuckAndCover.Core;
using static AbilityHarness;

/// <summary>
/// The ability data file loads the way JsonUtility will in Unity (public fields only), validates,
/// and every ability in it runs from data alone. Also what the validator catches (WP7).
/// </summary>
static class AbilityDataTests
{
    [Test]
    static void TestFileLoadsAndValidates()
    {
        var file = LoadFile(); // strict: a key that is not a field fails the load
        Check.Equal(11, file.abilities.Length, "abilities in the file");
        var lib = AbilityLibrary.From(file);
        var errors = lib.Validate();
        Check.True(errors.Count == 0, "the file is valid: " + string.Join("; ", errors));
        Check.True(lib.Status(AbilityLibrary.SlowStatus) != null && lib.Status(AbilityLibrary.StunStatus) != null, "built-in statuses are always there");
        foreach (var kind in EffectKinds.All)
            Check.True(
                file.abilities.Any(a => a.effects.Any(e => e.kind == kind)) || file.objects.Any(o => o.effects.Any(e => e.kind == kind)) || file.statuses.Any(s => s.tickEffects.Any(e => e.kind == kind)),
                "the test file uses effect kind '" + kind + "'");
        foreach (var path in Directory.GetFiles(DataDir, "*.json"))
            Check.True(File.Exists(path + ".meta"), Path.GetFileName(path) + " has a .meta beside it");
    }

    [Test]
    static void EveryAbilityInTheFileRunsFromDataAlone()
    {
        var file = LoadFile();
        foreach (var def in file.abilities.Where(a => !a.passive))
        {
            var h = new AbilityHarness();
            h.Give(A1, def.id);
            h.Place(A1, 0); h.Place(B1, 4); h.Place(A2, 0, 2);
            h.Combat.Damage(-1, A2, 50f);
            h.Forget();
            Check.True(h.Use(A1, 0, def.targeting.affects == Affects.Allies ? new Vec3(0, 0, 1) : Vec3.Right).IsAccepted, def.id + " is accepted");
            h.Run(2f);
            Check.True(h.Of<AbilityUsedEvent>().Any(e => e.abilityId == def.id), def.id + " landed");
            bool didSomething = h.Events.History.Any(e => e is DamagedEvent || e is HealedEvent || e is ShieldedEvent || e is StatusAppliedEvent || e is PushedEvent || e is ObjectSpawnedEvent);
            Check.True(didSomething, def.id + " had an effect");
        }
    }

    [Test]
    static void AnAbilityIsJustData()
    {
        // The package's "done when": a new ability defined in data alone, no code.
        var file = Parse(@"{
          ""abilities"": [{
            ""id"": ""shock_ring"", ""cooldown"": 5,
            ""targeting"": { ""shape"": ""sphere"", ""radius"": 6, ""affects"": ""all"" },
            ""effects"": [
              { ""kind"": ""damage"", ""amount"": 25, ""affects"": ""enemies"" },
              { ""kind"": ""status"", ""status"": ""marked"", ""affects"": ""enemies"" },
              { ""kind"": ""shield"", ""amount"": 15, ""duration"": 3, ""affects"": ""alliesAndSelf"" }
            ]
          }],
          ""statuses"": [{ ""id"": ""marked"", ""negative"": true, ""duration"": 4, ""reveal"": true,
            ""stats"": [{ ""stat"": ""damageTaken"", ""op"": ""percent"", ""value"": 0.2 }] }]
        }");
        var h = new AbilityHarness(extra: lib => lib.Add(file));
        h.Give(A1, "shock_ring");
        h.Place(A1, 0); h.Place(A2, 3); h.Place(B1, 5); h.Place(B2, 7);
        Check.True(h.Use(A1).IsAccepted, "used");
        Check.Near(175f, h.Health(B1), "enemy in the ring damaged");
        Check.Near(200f, h.Health(B2), "enemy outside it is not");
        Check.True(h.Abilities.IsRevealed(B1), "marked: revealed");
        Check.Near(15f, h.Unit(A1).Shield, "caster shielded");
        Check.Near(15f, h.Unit(A2).Shield, "teammate shielded");
        Check.Near(0f, h.Unit(B1).Shield, "enemy not shielded");
        Check.Near(12f, h.Combat.Damage(A2, B1, 10f), "marked enemies take 20% more");
    }

    [Test]
    static void UnknownKeysFailTheLoad()
    {
        bool threw = false;
        try { Parse(@"{ ""abilities"": [{ ""id"": ""x"", ""cooldwn"": 5 }] }"); }
        catch (System.Text.Json.JsonException) { threw = true; }
        Check.True(threw, "a misspelt key is an error in the test loader, not a silent default");
    }

    [Test]
    static void ValidatorCatchesBrokenData()
    {
        void Bad(string what, AbilityDataFile file, string expect)
        {
            var errors = AbilityLibrary.From(file).Validate();
            Check.True(errors.Any(e => e.Contains(expect)), what + ": expected an error containing '" + expect + "', got: " + string.Join("; ", errors));
        }
        AbilityDataFile One(AbilityDef a) => new AbilityDataFile { abilities = new[] { a } };
        EffectDef Dmg() => new EffectDef { kind = EffectKinds.Damage, amount = 10f };

        Bad("no cooldown", One(new AbilityDef { id = "a", effects = new[] { Dmg() } }), "needs a cooldown");
        Bad("no effects", One(new AbilityDef { id = "a", cooldown = 1f }), "no effects and no script");
        Bad("no id", One(new AbilityDef { cooldown = 1f, effects = new[] { Dmg() } }), "has no id");
        Bad("twice", new AbilityDataFile { abilities = new[] { new AbilityDef { id = "a", cooldown = 1f, effects = new[] { Dmg() } }, new AbilityDef { id = "a", cooldown = 1f, effects = new[] { Dmg() } } } }, "defined twice");
        Bad("shape", One(new AbilityDef { id = "a", cooldown = 1f, effects = new[] { Dmg() }, targeting = new TargetingDef { shape = "cone" } }), "unknown targeting shape");
        Bad("ray range", One(new AbilityDef { id = "a", cooldown = 1f, effects = new[] { Dmg() }, targeting = new TargetingDef { shape = Shapes.Ray } }), "a ray needs a range");
        Bad("sphere radius", One(new AbilityDef { id = "a", cooldown = 1f, effects = new[] { Dmg() }, targeting = new TargetingDef { shape = Shapes.Sphere } }), "a sphere needs a radius");
        Bad("kind", One(new AbilityDef { id = "a", cooldown = 1f, effects = new[] { new EffectDef { kind = "zap" } } }), "unknown effect kind");
        Bad("amount", One(new AbilityDef { id = "a", cooldown = 1f, effects = new[] { new EffectDef { kind = EffectKinds.Heal } } }), "needs an amount");
        Bad("slow share", One(new AbilityDef { id = "a", cooldown = 1f, effects = new[] { new EffectDef { kind = EffectKinds.Slow, amount = 30f } } }), "between 0 and 1");
        Bad("status", One(new AbilityDef { id = "a", cooldown = 1f, effects = new[] { new EffectDef { kind = EffectKinds.Status, status = "ghost" } } }), "unknown status");
        Bad("object", One(new AbilityDef { id = "a", cooldown = 1f, effects = new[] { new EffectDef { kind = EffectKinds.Spawn, objectId = "ghost" } } }), "unknown object");
        Bad("no cooldown", One(new AbilityDef { id = "a", effects = new[] { Dmg() } }), "needs a cooldown");
        Bad("passive", One(new AbilityDef { id = "a", passive = true }), "a passive needs stats");
        Bad("forever", new AbilityDataFile { objects = new[] { new ObjectDef { id = "o" } } }, "would last forever");
        Bad("pulse", new AbilityDataFile { objects = new[] { new ObjectDef { id = "o", lifetime = 5f, trigger = Triggers.Pulse } } }, "needs a radius");
        Bad("nested spawn", new AbilityDataFile { objects = new[] { new ObjectDef { id = "o", lifetime = 5f, radius = 2f, trigger = Triggers.Pulse, effects = new[] { new EffectDef { kind = EffectKinds.Spawn, objectId = "o" } } } } }, "can't spawn objects");
        Bad("tick", new AbilityDataFile { statuses = new[] { new StatusDef { id = "s", tickEffects = new[] { Dmg() } } } }, "tick effects need an interval");
        Bad("stat op", new AbilityDataFile { statuses = new[] { new StatusDef { id = "s", stats = new[] { new StatModifier(StatIds.MoveSpeed, ModOp.Set, 1f) } } } }, "must be add, percent or multiply");

        bool threw = false;
        try { new AbilityHarness(extra: lib => lib.Add(new AbilityDef { id = "broken" })); }
        catch (System.ArgumentException) { threw = true; }
        Check.True(threw, "the runner refuses to start on broken data");
        Check.True(new Ruleset { maxStunSeconds = 0f }.ValidateAbilities().Count > 0, "ruleset numbers are checked too");
        Check.Equal(0, Ruleset.Default.ValidateAbilities().Count, "the default ruleset is fine");
    }
}
