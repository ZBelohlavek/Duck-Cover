using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DuckAndCover.Core;

/// <summary>
/// A <see cref="MatchHarness"/> match with <see cref="ProgressionRules"/> on the same event stream,
/// ticked the way the server will tick them: match first, then progression. Shared by the WP8 tests.
/// </summary>
sealed class ProgressionHarness
{
    public readonly MatchHarness H;
    public readonly ProgressionRules P;

    public MatchRules Match => H.Match;
    public MatchState S => H.S;
    public FakeWorld World => H.World;
    public int[] A => H.A;
    public int[] B => H.B;

    public static string HeroesDir => Path.Combine(Repo.Root, "Assets", "DuckAndCover", "Data", "Heroes");

    /// <summary>Loads a hero file the way JsonUtility will in Unity: public fields only.</summary>
    public static HeroDef LoadHero(string path) =>
        JsonSerializer.Deserialize<HeroDef>(File.ReadAllText(path), new JsonSerializerOptions { IncludeFields = true });

    /// <summary>Every hero in Data/Heroes, with the test hero as the fallback for unknown hero ids.</summary>
    public static HeroCatalog DataCatalog() =>
        new HeroCatalog(Directory.GetFiles(HeroesDir, "*.json").OrderBy(f => f, StringComparer.Ordinal).Select(LoadHero), "test_hero");

    public ProgressionHarness(int perTeam = 5, Ruleset rules = null, HeroCatalog heroes = null, bool toPlay = true, ModeDef mode = null)
    {
        H = new MatchHarness(mode, perTeam, rules, toPlay: false);
        P = new ProgressionRules(H.Match, heroes ?? DataCatalog(), H.World, H.Events);
        if (!toPlay) return;
        Check.True(Match.StartMatch().IsAccepted, "match starts");
        RunTo(MatchPhase.Play);
    }

    public void Tick()
    {
        Match.Tick(MatchHarness.Dt);
        P.Tick();
    }

    public void Run(float seconds)
    {
        int ticks = (int)Math.Round(seconds / MatchHarness.Dt);
        for (int i = 0; i < ticks; i++) Tick();
    }

    public float RunUntil(Func<bool> done, float limitSeconds = 1200f, string what = "condition")
    {
        int limit = (int)(limitSeconds / MatchHarness.Dt);
        for (int i = 0; i < limit; i++)
        {
            if (done()) return i * MatchHarness.Dt;
            Tick();
        }
        Check.True(done(), what + " within " + limitSeconds + " s");
        return limitSeconds;
    }

    public void RunTo(MatchPhase phase, float limitSeconds = 1200f) => RunUntil(() => S.phase == phase, limitSeconds, "reach " + phase);

    public float Xp(int id) => P.Progress(id)?.xp ?? 0f;
    public int Level(int id) => P.Level(id);

    /// <summary>Give a player experience directly (catch-up applies if they trail the match average).</summary>
    public void Give(int id, float xp) => P.Grant(id, xp, ExperienceSource.Support);

    /// <summary>Raise a player to a level exactly at its threshold.</summary>
    public void RaiseTo(int id, int level)
    {
        float need = P.Rules.xpToReach[level - 1] - Xp(id);
        if (need > 0f) Give(id, need + 0.01f);
    }

    public List<T> Of<T>() where T : GameEvent => H.Of<T>();
    public string Log() => H.Log();
}
