using System;
using System.Collections.Generic;
using System.Linq;
using DuckAndCover.Core;

/// <summary>
/// Bot matches for tuning progression (WP8). The real Match, Zone, Spawn and Progression rules run
/// unchanged; only the players are simple: each bot walks between points on a line (base A, point 1,
/// middle, point 3, base B), fights whoever it meets there, and picks its upgrades at random.
/// It measures pacing (when levels arrive, how long matches run) and snowballing (how often the team
/// ahead early goes on to win). It is not a model of aim or abilities: fights are dice weighted by
/// level, using the plan's "a level 10 hero is about 30% stronger than a level 1 hero".
///
///   dotnet run --project Tests/CoreSim -- report progression [matches] [seed]
/// </summary>
static class ProgressionSim
{
    public sealed class Options
    {
        public int perTeam = 5;
        public float dt = 0.25f;
        /// <summary>Seconds to walk between neighbouring points (and from base to the nearest point).</summary>
        public float legSeconds = 7f;
        /// <summary>Chance per second that one level 1 bot eliminates an enemy on the same point.</summary>
        public float killRate = 0.055f;
        /// <summary>Extra strength at the top level over level 1 (the plan: about 30%).</summary>
        public float strengthAtMaxLevel = 0.30f;
        /// <summary>Health per second the support bot restores to each teammate fighting beside it.</summary>
        public float healPerSecond = 12f;
        /// <summary>Team A's fighting strength multiplier (1 = even teams).</summary>
        public float teamASkill = 1f;
        /// <summary>The early-lead check looks at who is ahead at this match time.</summary>
        public float earlyLeadAt = 240f;
        public Ruleset rules = Ruleset.Default;
        public ModeDef mode = ModeDef.CaptureAndHold();
    }

    public sealed class Result
    {
        public float length;
        public TeamId winner;
        public MatchEndReason reason;
        /// <summary>Match time each bot reached level 5 and level 10 (-1 if never).</summary>
        public List<float> level5At = new List<float>(), level10At = new List<float>();
        public List<int> finalLevels = new List<int>();
        public float supportXp, othersXp; // average final experience: support bots vs the rest
        public TeamId xpLeaderEarly = TeamId.None, scoreLeaderEarly = TeamId.None;
        public int eliminations, captures, picks, pendingAtEnd, maxLevelGap;
        public string log;
    }

    sealed class Bot
    {
        public int id;
        public TeamId team;
        public bool support;
        public float pos;        // 0 = base A .. 4 = base B
        public int target;       // 1..3
        public float decideAt;
        public bool wasAlive;
    }

    /// <summary>xorshift: the same numbers on every machine and runtime.</summary>
    sealed class Rng
    {
        uint s;
        public Rng(int seed) { s = (uint)seed * 2654435761u + 1u; if (s == 0) s = 1; }
        public float Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFFFF) / (float)0x1000000; }
        public int Next(int n) => Math.Min(n - 1, (int)(Next() * n));
    }

    public static Result Play(int seed, Options o = null, bool keepLog = false)
    {
        o = o ?? new Options();
        var rng = new Rng(seed);
        var world = new FakeWorld();
        var events = new GameEventBus { Record = true };
        var match = new MatchRules(o.rules, o.mode, MatchHarness.TestMap(), world, events);
        var prog = new ProgressionRules(match, ProgressionHarness.DataCatalog(), world, events);
        var S = match.State;
        var bots = new List<Bot>();
        for (int i = 1; i <= o.perTeam; i++)
        {
            bots.Add(new Bot { id = i, team = TeamId.A, support = i == 1 });
            bots.Add(new Bot { id = 100 + i, team = TeamId.B, support = i == 1 });
        }
        foreach (var b in bots) match.AddPlayer(b.id, b.team);
        match.StartMatch();
        while (S.phase != MatchPhase.Play) match.Tick(o.dt);

        var res = new Result();
        var l5 = new Dictionary<int, float>();
        var l10 = new Dictionary<int, float>();
        int seen = 0;
        int maxLevel = prog.Rules.maxLevel;
        float perLevel = o.strengthAtMaxLevel / Math.Max(1, maxLevel - 1);
        bool earlyTaken = false;

        float Strength(Bot b) => (1f + perLevel * (prog.Level(b.id) - 1)) * (b.team == TeamId.A ? o.teamASkill : 1f);

        int limit = (int)(3600f / o.dt);
        for (int step = 0; step < limit && S.phase != MatchPhase.MatchEnd; step++)
        {
            // Move and place the bots.
            world.ClearAll();
            foreach (var b in bots)
            {
                var p = S.Player(b.id);
                if (!p.alive) { b.wasAlive = false; continue; }
                if (!b.wasAlive)
                {
                    b.wasAlive = true;
                    b.pos = b.team == TeamId.A ? 0f : 4f;
                    b.decideAt = 0f;
                }
                if (S.elapsed >= b.decideAt)
                {
                    b.target = PickTarget(b, S, rng);
                    b.decideAt = S.elapsed + 12f + 10f * rng.Next();
                }
                float stepSize = o.dt / o.legSeconds;
                if (Math.Abs(b.target - b.pos) <= stepSize) b.pos = b.target;
                else b.pos += Math.Sign(b.target - b.pos) * stepSize;
                if (b.pos == b.target) world.Put(b.id, new ZoneId(b.target));
            }

            // Fights on each point.
            for (int zone = 1; zone <= 3; zone++)
            {
                var here = bots.Where(b => S.Player(b.id).alive && b.pos == zone).ToList();
                var a = here.Where(b => b.team == TeamId.A).ToList();
                var bb = here.Where(b => b.team == TeamId.B).ToList();
                if (a.Count == 0 || bb.Count == 0) continue;
                foreach (var bot in here)
                {
                    var foes = bot.team == TeamId.A ? bb : a;
                    var mates = bot.team == TeamId.A ? a : bb;
                    if (bot.support)
                        foreach (var m in mates) if (m != bot) prog.RecordSupport(bot.id, m.id, o.healPerSecond * o.dt);
                    if (rng.Next() < 0.6f * o.dt) prog.RecordDamage(bot.id, foes[rng.Next(foes.Count)].id);
                    if (rng.Next() < o.killRate * Strength(bot) * o.dt)
                    {
                        var living = foes.Where(f => S.Player(f.id).alive).ToList();
                        if (living.Count == 0) continue;
                        var victim = living[rng.Next(living.Count)];
                        prog.RecordDamage(bot.id, victim.id);
                        match.EliminatePlayer(victim.id, bot.id);
                    }
                }
            }

            match.Tick(o.dt);
            prog.Tick();

            // Bots take a waiting choice now and then, alive or not.
            foreach (var b in bots)
            {
                if (rng.Next() > 0.5f * o.dt) continue;
                var pending = prog.PendingLevels(b.id);
                if (pending.Count > 0) prog.Choose(b.id, pending[rng.Next(pending.Count)], rng.Next(2));
            }

            for (; seen < events.History.Count; seen++)
            {
                if (!(events.History[seen] is LevelUpEvent up)) continue;
                if (up.level == 5) l5[up.playerId] = S.elapsed;
                if (up.level == maxLevel) l10[up.playerId] = S.elapsed;
            }

            int lo = bots.Min(b => prog.Level(b.id)), hi = bots.Max(b => prog.Level(b.id));
            res.maxLevelGap = Math.Max(res.maxLevelGap, hi - lo);

            if (!earlyTaken && S.elapsed >= o.earlyLeadAt)
            {
                earlyTaken = true;
                float xa = bots.Where(b => b.team == TeamId.A).Sum(b => prog.Progress(b.id).xp);
                float xb = bots.Where(b => b.team == TeamId.B).Sum(b => prog.Progress(b.id).xp);
                res.xpLeaderEarly = xa > xb ? TeamId.A : xb > xa ? TeamId.B : TeamId.None;
                res.scoreLeaderEarly = S.Leader;
            }
        }

        res.length = S.elapsed;
        res.winner = S.winner;
        res.reason = S.endReason;
        foreach (var b in bots)
        {
            res.level5At.Add(l5.TryGetValue(b.id, out var t5) ? t5 : -1f);
            res.level10At.Add(l10.TryGetValue(b.id, out var t10) ? t10 : -1f);
            res.finalLevels.Add(prog.Level(b.id));
            res.pendingAtEnd += prog.PendingLevels(b.id).Count;
        }
        res.supportXp = bots.Where(b => b.support).Average(b => prog.Progress(b.id).xp);
        res.othersXp = bots.Where(b => !b.support).Average(b => prog.Progress(b.id).xp);
        res.eliminations = events.History.OfType<PlayerEliminatedEvent>().Count();
        res.captures = events.History.OfType<ZoneCapturedEvent>().Count();
        res.picks = events.History.OfType<UpgradeChosenEvent>().Count();
        if (keepLog) res.log = string.Join("\n", events.History.Select(e => e.tick + " " + e.GetType().Name));
        prog.Dispose();
        return res;
    }

    /// <summary>Where a bot heads next: points its team doesn't hold pull harder, the far point least.</summary>
    static int PickTarget(Bot b, MatchState S, Rng rng)
    {
        var weights = new float[3];
        for (int i = 0; i < 3; i++)
        {
            int zone = i + 1;
            var z = S.Zone(new ZoneId(zone));
            int own = b.team == TeamId.A ? 1 : 3;
            bool mine = z.owner == b.team;
            float w;
            if (zone == own) w = mine ? (z.Count(b.team.Other()) > 0 ? 3f : 0.4f) : 4f;
            else if (zone == 2) w = mine ? 2f : 4f;
            else w = S.Zone(new ZoneId(2)).owner == b.team ? 2f : 0.6f;
            weights[i] = w;
        }
        float roll = rng.Next() * weights.Sum();
        for (int i = 0; i < 3; i++)
        {
            roll -= weights[i];
            if (roll <= 0f) return i + 1;
        }
        return 2;
    }

    // ---- Report ----

    public sealed class Summary
    {
        public int matches;
        public float medianLength, p10Length, p90Length;
        public float medianLevel5, medianLevel10, p10Level10, p90Level10;
        public float reachedLevel10;      // share of bots
        public float averageFinalLevel;
        public float supportVsOthers;     // support bot experience / everyone else's
        public float xpLeaderWins, scoreLeaderWins;
        public float averageMaxLevelGap;
        public float eliminationsPerMinute;
        public float timeCapShare;
    }

    public static Summary Run(int matches, int seed, Options o = null)
    {
        var results = new List<Result>();
        for (int i = 0; i < matches; i++) results.Add(Play(seed + i, o));
        var lengths = results.Select(r => r.length).OrderBy(x => x).ToList();
        var l5 = results.SelectMany(r => r.level5At).Where(t => t >= 0f).OrderBy(x => x).ToList();
        var all10 = results.SelectMany(r => r.level10At).ToList();
        var l10 = all10.Where(t => t >= 0f).OrderBy(x => x).ToList();
        var xpLed = results.Where(r => r.xpLeaderEarly != TeamId.None).ToList();
        var scoreLed = results.Where(r => r.scoreLeaderEarly != TeamId.None).ToList();
        return new Summary
        {
            matches = matches,
            medianLength = Pct(lengths, 0.5f), p10Length = Pct(lengths, 0.1f), p90Length = Pct(lengths, 0.9f),
            medianLevel5 = Pct(l5, 0.5f),
            medianLevel10 = Pct(l10, 0.5f), p10Level10 = Pct(l10, 0.1f), p90Level10 = Pct(l10, 0.9f),
            reachedLevel10 = all10.Count == 0 ? 0f : (float)l10.Count / all10.Count,
            averageFinalLevel = (float)results.SelectMany(r => r.finalLevels).Average(),
            supportVsOthers = results.Average(r => r.supportXp) / results.Average(r => r.othersXp),
            xpLeaderWins = xpLed.Count == 0 ? 0f : (float)xpLed.Count(r => r.winner == r.xpLeaderEarly) / xpLed.Count,
            scoreLeaderWins = scoreLed.Count == 0 ? 0f : (float)scoreLed.Count(r => r.winner == r.scoreLeaderEarly) / scoreLed.Count,
            averageMaxLevelGap = (float)results.Average(r => r.maxLevelGap),
            eliminationsPerMinute = results.Sum(r => r.eliminations) / (results.Sum(r => r.length) / 60f),
            timeCapShare = (float)results.Count(r => r.reason == MatchEndReason.TimeCap) / matches,
        };
    }

    static float Pct(List<float> sorted, float p) => sorted.Count == 0 ? -1f : sorted[Math.Min(sorted.Count - 1, (int)(p * sorted.Count))];

    static string Min(float seconds) => seconds < 0f ? "never" : ((int)seconds / 60) + ":" + ((int)seconds % 60).ToString("00");
    static string Share(float f) => (f * 100f).ToString("F0") + "%";

    public static int Report(string[] args)
    {
        int matches = args.Length > 0 ? int.Parse(args[0]) : 2000;
        int seed = args.Length > 1 ? int.Parse(args[1]) : 1;
        Console.WriteLine("Progression simulation: " + matches + " bot matches per row, 5v5 capture and hold, even teams, seed " + seed);
        Console.WriteLine();

        var rows = new List<(string name, Options o)>
        {
            ("Ruleset as shipped", new Options()),
            ("No catch-up, no bounty", Change(r => { r.catchUpBonus = 0f; r.bountyPerLevel = 0f; })),
            ("Levels give no strength", new Options { strengthAtMaxLevel = 0f }),
            ("Levels double strength", new Options { strengthAtMaxLevel = 1f }),
            ("Team A 15% stronger", new Options { teamASkill = 1.15f }),
            ("1 point a second (old)", new Options { mode = Rate(1f) }),
        };
        Console.WriteLine("{0,-26}{1,8}{2,14}{3,9}{4,9}{5,16}{6,9}{7,9}{8,10}{9,10}{10,8}{11,8}",
            "", "length", "(10%-90%)", "level 5", "level 10", "(10%-90%)", "hit 10", "end lvl", "xp lead", "score ld", "gap", "support");
        foreach (var (name, o) in rows)
        {
            var s = Run(matches, seed, o);
            Console.WriteLine("{0,-26}{1,8}{2,14}{3,9}{4,9}{5,16}{6,9}{7,9}{8,10}{9,10}{10,8}{11,8}",
                name, Min(s.medianLength), Min(s.p10Length) + "-" + Min(s.p90Length), Min(s.medianLevel5), Min(s.medianLevel10),
                Min(s.p10Level10) + "-" + Min(s.p90Level10), Share(s.reachedLevel10), s.averageFinalLevel.ToString("F1"),
                Share(s.xpLeaderWins), Share(s.scoreLeaderWins), s.averageMaxLevelGap.ToString("F1"), Share(s.supportVsOthers));
        }
        Console.WriteLine();
        Console.WriteLine("length     median match length, play and overtime");
        Console.WriteLine("level 5/10 median match time a player reaches the level (plan: near 5:00 and 12:00)");
        Console.WriteLine("hit 10     share of players who reach level 10 before the match ends");
        Console.WriteLine("xp lead    how often the team with more experience at 4:00 wins (50% = an early lead means nothing)");
        Console.WriteLine("score ld   the same for the team ahead on score at 4:00");
        Console.WriteLine("gap        largest level difference between any two players, averaged over matches");
        Console.WriteLine("support    the support bot's experience as a share of the other bots'");
        return 0;
    }

    /// <summary>
    /// Match length and levels at different point rates, first to 1000:
    ///   dotnet run --project Tests/CoreSim -- report scorerate [matches] [seed] [rate ...]
    /// </summary>
    public static int RateReport(string[] args)
    {
        int matches = args.Length > 0 ? int.Parse(args[0]) : 1000;
        int seed = args.Length > 1 ? int.Parse(args[1]) : 1;
        var rates = new List<float>();
        for (int i = 2; i < args.Length; i++) rates.Add(float.Parse(args[i], System.Globalization.CultureInfo.InvariantCulture));
        if (rates.Count == 0) rates.AddRange(new[] { 1f, 0.9f, 0.85f, 0.8f, 0.75f, 0.7f });
        Console.WriteLine("Point rate: " + matches + " bot matches per row, 5v5 capture and hold, first to " + ModeDef.CaptureAndHold().scoreToWin + ", seed " + seed);
        Console.WriteLine();
        Console.WriteLine("{0,-22}{1,8}{2,14}{3,10}{4,9}{5,9}{6,9}", "points per second", "length", "(10%-90%)", "time cap", "level 10", "hit 10", "end lvl");
        foreach (float rate in rates)
        {
            var mode = ModeDef.CaptureAndHold();
            mode.scorePerSecond = rate;
            var s = Run(matches, seed, new Options { mode = mode });
            Console.WriteLine("{0,-22}{1,8}{2,14}{3,10}{4,9}{5,9}{6,9}", rate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                Min(s.medianLength), Min(s.p10Length) + "-" + Min(s.p90Length), Share(s.timeCapShare), Min(s.medianLevel10),
                Share(s.reachedLevel10), s.averageFinalLevel.ToString("F1"));
        }
        Console.WriteLine();
        Console.WriteLine("time cap   share of matches that end on the time cap instead of the score");
        return 0;
    }

    static ModeDef Rate(float scorePerSecond)
    {
        var m = ModeDef.CaptureAndHold();
        m.scorePerSecond = scorePerSecond;
        return m;
    }

    static Options Change(Action<ProgressionRuleset> edit)
    {
        var o = new Options();
        edit(o.rules.progression);
        return o;
    }
}
