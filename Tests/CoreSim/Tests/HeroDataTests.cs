using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DuckAndCover.Core;

/// <summary>Hero and upgrade data files load and pass the completeness check (WP8).</summary>
static class HeroDataTests
{
    [Test]
    static void HeroFilesAreComplete()
    {
        var files = Directory.GetFiles(ProgressionHarness.HeroesDir, "*.json");
        Check.True(files.Length >= 1, "at least the test hero exists");
        var catalog = ProgressionHarness.DataCatalog();
        var errors = catalog.Validate(new ProgressionRuleset());
        Check.Equal(0, errors.Count, "hero data is complete: " + string.Join("; ", errors));

        var test = catalog.Get("test_hero");
        Check.True(test != null, "test hero loads");
        if (test == null) return;
        Check.Equal(10, test.levels.Length, "ten choice levels");
        Check.Equal(20, test.upgrades.Length, "twenty options");
        Check.Equal(UpgradeKind.Talent, test.Upgrade("cluster_grenade").Kind, "kinds parse");
        Check.Equal("onHeldPoint", test.Upgrade("dug_in").stats[0].when, "conditions load");
        Check.Equal("healTrail", test.Upgrade("trail_dash").abilityChanges[0].text, "text params load");
    }

    [Test]
    static void HeroFilesHaveOnlyKnownKeys()
    {
        // JsonUtility silently drops unknown keys; a typo would quietly lose data.
        foreach (var file in Directory.GetFiles(ProgressionHarness.HeroesDir, "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var name = Path.GetFileName(file);
            CheckKeys(doc.RootElement, typeof(HeroDef), name);
            foreach (var l in doc.RootElement.GetProperty("levels").EnumerateArray()) CheckKeys(l, typeof(HeroLevelChoice), name + " level");
            foreach (var u in doc.RootElement.GetProperty("upgrades").EnumerateArray())
            {
                CheckKeys(u, typeof(UpgradeDef), name + " upgrade");
                foreach (var s in u.GetProperty("stats").EnumerateArray()) CheckKeys(s, typeof(StatModifier), name + " stat");
                foreach (var c in u.GetProperty("abilityChanges").EnumerateArray()) CheckKeys(c, typeof(AbilityParamChange), name + " ability change");
            }
        }
    }

    static void CheckKeys(JsonElement e, System.Type type, string what)
    {
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name).ToHashSet();
        foreach (var p in e.EnumerateObject()) Check.True(fields.Contains(p.Name), what + ": '" + p.Name + "' is not a field of " + type.Name);
    }

    static HeroDef Good() => ProgressionHarness.DataCatalog().Get("test_hero");

    static bool Finds(HeroDef hero, string text) =>
        HeroCatalog.ValidateHero(hero, new ProgressionRuleset()).Any(e => e.Contains(text));

    [Test]
    static void ValidationCatchesGaps()
    {
        var h = Good();
        h.levels = h.levels.Where(l => l.level != 12).ToArray();
        Check.True(Finds(h, "missing its level 12 talent"), "a missing level");

        h = Good();
        h.levels[0].options = new[] { "sturdy" };
        Check.True(Finds(h, "has 1 options, needs 2"), "a missing option");

        h = Good();
        h.levels[0].options = new[] { "sturdy", "cluster_grenade" };
        Check.True(Finds(h, "where a trait belongs"), "wrong kind at a level");

        h = Good();
        h.levels[0].options = new[] { "sturdy", "nope" };
        Check.True(Finds(h, "'nope', which is not in its upgrades"), "an unknown option");

        h = Good();
        h.upgrades[0].abilityChanges = new[] { new AbilityParamChange("test_dash", "cooldown", ModOp.Multiply, 0.9f) };
        Check.True(Finds(h, "traits change stats"), "a trait changing an ability");

        h = Good();
        h.Upgrade("cluster_grenade").abilityChanges[0].ability = "someone_elses_grenade";
        Check.True(Finds(h, "not one of test_hero's abilities"), "a talent on another hero's ability");

        h = Good();
        h.Upgrade("long_overdrive").abilityChanges[0].ability = "test_dash";
        Check.True(Finds(h, "is an ultimate form but changes"), "an ultimate form that changes a basic ability");

        h = Good();
        h.Upgrade("sturdy").stats[0].op = "double";
        Check.True(Finds(h, "op 'double'"), "an unknown op");

        h = Good();
        h.Upgrade("sturdy").stats = new StatModifier[0];
        Check.True(Finds(h, "does nothing"), "an empty upgrade");

        h = Good();
        h.upgrades = h.upgrades.Append(new UpgradeDef { id = "spare", displayName = "Spare", kind = "trait", stats = new[] { new StatModifier("maxHealth", ModOp.Add, 1) } }).ToArray();
        Check.True(Finds(h, "'spare' is never offered"), "an unused upgrade");

        h = Good();
        h.abilities = new string[0];
        Check.True(Finds(h, "one or two abilities"), "a hero with a base passive needs E (and may have F)");
        h = Good();
        h.abilities = new[] { "test_dash" };
        Check.True(!Finds(h, "two abilities"), "E, a base passive and X is a whole kit");
        h.passiveId = "";
        Check.True(Finds(h, "exactly two abilities"), "without a base passive it needs E and F");

        var twice = new HeroCatalog(new[] { Good(), Good() });
        Check.True(twice.Validate(new ProgressionRuleset()).Any(e => e.Contains("defined twice")), "duplicate hero ids");
    }
}
