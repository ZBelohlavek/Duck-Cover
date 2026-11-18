using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DuckAndCover.Core;

/// <summary>
/// The mode data files in Assets/DuckAndCover/Data/Modes load into ModeDef the way JsonUtility will in
/// Unity (public fields only), match the built-in presets, and run a match (WP1).
/// </summary>
static class ModeDataTests
{
    static string ModesDir => Path.Combine(Repo.Root, "Assets", "DuckAndCover", "Data", "Modes");

    static ModeDef Load(string file) =>
        JsonSerializer.Deserialize<ModeDef>(File.ReadAllText(Path.Combine(ModesDir, file)), new JsonSerializerOptions { IncludeFields = true });

    [Test]
    static void ModeFilesMatchPresets()
    {
        foreach (var (file, preset) in new[] { ("capture_hold.json", ModeDef.CaptureAndHold()), ("king_of_the_hill.json", ModeDef.KingOfTheHill()), ("flag_rush.json", ModeDef.FlagRush()), ("overrun.json", ModeDef.Overrun()) })
        {
            var loaded = Load(file);
            foreach (var f in typeof(ModeDef).GetFields(BindingFlags.Public | BindingFlags.Instance))
                Check.Equal(f.GetValue(preset)?.ToString(), f.GetValue(loaded)?.ToString(), file + " " + f.Name);

            // Every key is a ModeDef field and every field has a key, so JsonUtility misses nothing.
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(ModesDir, file)));
            var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
            var fields = typeof(ModeDef).GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name).OrderBy(n => n).ToArray();
            Check.Equal(string.Join(",", fields), string.Join(",", keys), file + " keys");
            Check.Equal(0, loaded.Validate(MatchHarness.TestMap()).Count, file + " is valid on the test map");
        }
    }

    [Test]
    static void BothModesRunFromDataFiles()
    {
        foreach (var file in new[] { "capture_hold.json", "king_of_the_hill.json" })
        {
            var h = new MatchHarness(Load(file));
            Check.Equal(Load(file).id, h.S.modeId, file + " mode id in the state");
            var first = h.S.zones.First(z => z.active).id;
            h.Take(first, TeamId.A);
            h.Run(5f);
            Check.True(h.S.scoreA >= 3, file + " scores from data");
        }
    }
}
