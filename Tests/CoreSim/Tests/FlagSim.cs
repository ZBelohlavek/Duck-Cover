using System;
using System.Collections.Generic;
using System.Linq;
using DuckAndCover.Core;

/// <summary>
/// Flag Rush bot matches for pacing, in the style of <see cref="ProgressionSim"/>: the real Match and
/// Progression rules (score through <see cref="MatchRules.ScoreFlagCapture"/>, respawns, overtime, flag
/// experience from the ruleset) with simple bots on a line from base A (0) to base B (4), the flag
/// appearing in the middle (2, the same walk from both bases). Fights are dice weighted by level, as in
/// the progression sim. Each team keeps one bot home; the rest go for the flag, escort their carrier or
/// chase the enemy's. The carrier walks (no sprint). It checks match length and levels, not tactics:
///   dotnet run --project Tests/CoreSim -- report flagrush [matches] [seed]
/// </summary>
static class FlagSim
{
    public sealed class Options
    {
        public int perTeam = Ruleset.Default.maxPlayersPerTeam;
        public float dt = 0.25f;
        /// <summary>Seconds to walk one unit of the line (base to middle is two).</summary>
        public float legSeconds = 7f;
        /// <summary>The carrier's speed as a share of a sprinting bot's.</summary>
        public float carrierSpeed = 0.7f;
        /// <summary>Bots this close on the line are in a fight.</summary>
        public float fightRange = 0.35f;
        /// <summary>Chance per second that one level 1 bot eliminates an enemy it is fighting.</summary>
        public float killRate = 0.055f;
        public float strengthAtMaxLevel = 0.30f;
        public float healPerSecond = 12f;
        public Ruleset rules = Ruleset.Default;
        public ModeDef mode = ModeDef.FlagRush();
    }

    public sealed class Result
    {
        public float length;
        public TeamId winner;
        public MatchEndReason reason;
        public int scoreA, scoreB, eliminations, drops;
        public float averageFinalLevel;
        public List<float> captureTimes = new List<float>();
        public List<float> topLevelAt = new List<float>();
    }

    sealed class Bot
    {
        public int id;
        public TeamId team;
        public bool home, support, wasAlive;
        public float pos;
    }

    sealed class Rng
    {
        uint s;
        public Rng(int seed) { s = (uint)seed * 2246822519u + 7u; if (s == 0) s = 1; }
        public float Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFFFF) / (float)0x1000000; }
        public int Next(int n) => Math.Min(n - 1, (int)(Next() * n));
    }

    const float Middle = 2f;

    public static Result Play(int seed, Options o = null)
    {
        o = o ?? new Options();
        var rng = new Rng(seed);
        var rules = o.rules.Clone();
        rules.maxPlayersPerTeam = Math.Max(rules.maxPlayersPerTeam, o.perTeam);
        var world = new FakeWorld();
        var events = new GameEventBus { Record = true };
        var match = new MatchRules(rules, o.mode, MatchHarness.TestMap(), world, events);
        var prog = new ProgressionRules(match, ProgressionHarness.DataCatalog(), world, events);
        var S = match.State;
        var bots = new List<Bot>();
        for (int i = 1; i <= o.perTeam; i++)
        {
            bots.Add(new Bot { id = i, team = TeamId.A, support = i == 1, home = i == 2 });
            bots.Add(new Bot { id = 100 + i, team = TeamId.B, support = i == 1, home = i == 2 });
        }
        foreach (var b in bots) match.AddPlayer(b.id, b.team);
        match.StartMatch();
        while (S.phase != MatchPhase.Play) match.Tick(o.dt);

        int maxLevel = prog.Rules.maxLevel;
        float perLevel = o.strengthAtMaxLevel / Math.Max(1, maxLevel - 1);
        float Strength(Bot b) => 1f + perLevel * (prog.Level(b.id) - 1);
        float Home(TeamId t) => t == TeamId.A ? 0f : 4f;
        float Goal(TeamId t) => t == TeamId.A ? 4f : 0f;

        // The flag: waiting to appear (spawnAt), on the ground (at), or carried.
        float spawnAt = S.elapsed + rules.flagFirstSpawnSeconds;
        bool onGround = false;
        float flagAt = Middle, dropTimer = 0f;
        Bot carrier = null;
        var res = new Result();
        var top = new Dictionary<int, float>();

        int limit = (int)(3600f / o.dt);
        for (int step = 0; step < limit && S.phase != MatchPhase.MatchEnd; step++)
        {
            float dt = o.dt;
            if (!onGround && carrier == null && S.elapsed >= spawnAt) { onGround = true; flagAt = Middle; dropTimer = 0f; }

            // Movement.
            foreach (var b in bots)
            {
                if (!S.IsAlive(b.id)) { b.wasAlive = false; if (carrier == b) carrier = null; continue; }
                if (!b.wasAlive) { b.wasAlive = true; b.pos = Home(b.team); }
                float target;
                if (carrier == b) target = Goal(b.team);
                else if (carrier != null && carrier.team == b.team) target = carrier.pos + (Goal(b.team) - carrier.pos) * 0.1f; // escort, a step ahead
                else if (carrier != null) target = b.home ? Home(b.team) + (Middle - Home(b.team)) * 0.4f : carrier.pos; // chase; home bot waits by the base
                else if (onGround) target = b.home ? Home(b.team) + (Middle - Home(b.team)) * 0.4f : flagAt;
                else target = b.home ? Home(b.team) + (Middle - Home(b.team)) * 0.4f : Middle;
                if (carrier != null && carrier.team != b.team && b.home && Math.Abs(carrier.pos - Home(b.team)) < 1.5f) target = carrier.pos; // the home bot steps in late
                float speed = dt / o.legSeconds * (carrier == b ? o.carrierSpeed : 1f);
                b.pos = Math.Abs(target - b.pos) <= speed ? target : b.pos + Math.Sign(target - b.pos) * speed;
            }

            // Pick up by touch (whoever got there; a tie goes to a coin flip).
            if (onGround)
            {
                var touching = bots.Where(b => S.IsAlive(b.id) && Math.Abs(b.pos - flagAt) < 0.05f).ToList();
                if (touching.Count > 0)
                {
                    carrier = touching[rng.Next(touching.Count)];
                    onGround = false;
                    prog.Grant(carrier.id, rules.xpFlagPickup, ExperienceSource.Flag);
                }
                else if ((dropTimer += dt) >= rules.flagDropResetSeconds && flagAt != Middle)
                {
                    flagAt = Middle; // untouched too long: it reappears at a spot (the middle again here)
                    dropTimer = 0f;
                }
            }

            // Fights between bots close on the line.
            foreach (var bot in bots)
            {
                if (!S.IsAlive(bot.id)) continue;
                var foes = bots.Where(f => f.team != bot.team && S.IsAlive(f.id) && Math.Abs(f.pos - bot.pos) <= o.fightRange).ToList();
                if (foes.Count == 0) continue;
                if (bot.support)
                    foreach (var m in bots)
                        if (m != bot && m.team == bot.team && S.IsAlive(m.id) && Math.Abs(m.pos - bot.pos) <= o.fightRange)
                            prog.RecordSupport(bot.id, m.id, o.healPerSecond * dt);
                if (rng.Next() < 0.6f * dt) prog.RecordDamage(bot.id, foes[rng.Next(foes.Count)].id);
                if (rng.Next() >= o.killRate * Strength(bot) * dt) continue;
                var victim = carrier != null && foes.Contains(carrier) && rng.Next() < 0.5f ? carrier : foes[rng.Next(foes.Count)];
                prog.RecordDamage(bot.id, victim.id);
                if (victim == carrier)
                {
                    carrier = null;
                    onGround = true;
                    flagAt = victim.pos;
                    dropTimer = 0f;
                    res.drops++;
                    prog.Grant(bot.id, rules.xpFlagStopCarrier, ExperienceSource.Flag);
                }
                match.EliminatePlayer(victim.id, bot.id);
            }

            // Carrying and scoring.
            if (carrier != null)
            {
                prog.Grant(carrier.id, rules.xpFlagCarryPerSecond * dt, ExperienceSource.Flag);
                if (Math.Abs(carrier.pos - Goal(carrier.team)) < 0.01f)
                {
                    prog.Grant(carrier.id, rules.xpFlagCapture, ExperienceSource.Flag);
                    foreach (var m in bots)
                        if (m != carrier && m.team == carrier.team && S.IsAlive(m.id) && Math.Abs(m.pos - carrier.pos) <= 0.5f)
                            prog.Grant(m.id, rules.xpFlagCaptureAssist, ExperienceSource.Flag);
                    res.captureTimes.Add(S.elapsed);
                    match.ScoreFlagCapture(carrier.team);
                    carrier = null;
                    spawnAt = S.elapsed + rules.flagAfterCaptureSeconds;
                }
            }

            match.Tick(dt);
            prog.Tick();
            foreach (var b in bots)
            {
                if (rng.Next() > 0.5f * dt) continue;
                var pending = prog.PendingLevels(b.id);
                if (pending.Count > 0) prog.Choose(b.id, pending[rng.Next(pending.Count)], rng.Next(2));
                if (prog.Level(b.id) >= maxLevel && !top.ContainsKey(b.id)) top[b.id] = S.elapsed;
            }
        }

        res.length = S.elapsed;
        res.winner = S.winner;
        res.reason = S.endReason;
        res.scoreA = S.scoreA;
        res.scoreB = S.scoreB;
        res.eliminations = events.History.OfType<PlayerEliminatedEvent>().Count();
        res.averageFinalLevel = (float)bots.Average(b => prog.Level(b.id));
        foreach (var b in bots) res.topLevelAt.Add(top.TryGetValue(b.id, out var t) ? t : -1f);
        prog.Dispose();
        return res;
    }

    public sealed class Summary
    {
        public float medianLength, p10Length, p90Length, timeCapShare, averageFinalLevel, reachedTop, medianTop, eliminationsPerMinute, dropsPerMatch;
        public float averageLoserScore;
    }

    public static Summary Run(int matches, int seed, Options o = null)
    {
        var all = new List<Result>();
        for (int i = 0; i < matches; i++) all.Add(Play(seed + i, o));
        var lengths = all.Select(r => r.length).OrderBy(x => x).ToList();
        var tops = all.SelectMany(r => r.topLevelAt).ToList();
        var reached = tops.Where(t => t >= 0f).OrderBy(x => x).ToList();
        return new Summary
        {
            medianLength = Pct(lengths, 0.5f), p10Length = Pct(lengths, 0.1f), p90Length = Pct(lengths, 0.9f),
            timeCapShare = (float)all.Count(r => r.reason == MatchEndReason.TimeCap) / matches,
            averageFinalLevel = all.Average(r => r.averageFinalLevel),
            reachedTop = tops.Count == 0 ? 0f : (float)reached.Count / tops.Count,
            medianTop = Pct(reached, 0.5f),
            eliminationsPerMinute = all.Sum(r => r.eliminations) / (all.Sum(r => r.length) / 60f),
            dropsPerMatch = (float)all.Average(r => r.drops),
            averageLoserScore = (float)all.Average(r => Math.Min(r.scoreA, r.scoreB)),
        };
    }

    static float Pct(List<float> sorted, float p) => sorted.Count == 0 ? -1f : sorted[Math.Min(sorted.Count - 1, (int)(p * sorted.Count))];
    static string Min(float seconds) => seconds < 0f ? "never" : ((int)seconds / 60) + ":" + ((int)seconds % 60).ToString("00");

    /// <summary>dotnet run --project Tests/CoreSim -- report flagrush [matches] [seed]</summary>
    public static int Report(string[] args)
    {
        int matches = args.Length > 0 ? int.Parse(args[0]) : 300;
        int seed = args.Length > 1 ? int.Parse(args[1]) : 1;
        Console.WriteLine("Flag Rush bot matches (dice fights on a line, one bot kept home per team): " + matches + " per row, first to " +
                          ModeDef.FlagRush().scoreToWin + ", seed " + seed);
        Console.WriteLine();
        Console.WriteLine("{0,-14}{1,8}{2,14}{3,10}{4,9}{5,9}{6,9}{7,10}{8,8}{9,10}", "", "length", "(10%-90%)", "time cap", "top lvl", "hit top", "end lvl", "elims/min", "drops", "loser");
        foreach (int n in new[] { 5, 4 })
        {
            var s = Run(matches, seed, new Options { perTeam = n });
            Console.WriteLine("{0,-14}{1,8}{2,14}{3,10}{4,9}{5,9}{6,9}{7,10}{8,8}{9,10}", n + "v" + n, Min(s.medianLength), Min(s.p10Length) + "-" + Min(s.p90Length),
                (s.timeCapShare * 100f).ToString("F0") + "%", Min(s.medianTop), (s.reachedTop * 100f).ToString("F0") + "%", s.averageFinalLevel.ToString("F1"),
                s.eliminationsPerMinute.ToString("F1"), s.dropsPerMatch.ToString("F1"), s.averageLoserScore.ToString("F1"));
        }
        Console.WriteLine();
        Console.WriteLine("drops      carriers brought down per match; loser = the losing team's captures, averaged");
        return 0;
    }
}
