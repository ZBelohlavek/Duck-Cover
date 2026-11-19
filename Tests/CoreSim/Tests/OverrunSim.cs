using System;
using System.Collections.Generic;
using System.Linq;
using DuckAndCover.Core;

/// <summary>
/// Overrun bot matches (WP16): four against four (or any team size) on Breakwater's lane with the real rules (heroes,
/// weapons, abilities, leveling, waves, camps' guards, Bastions), and simple bots. Each bot walks the
/// lane a few metres behind its team's front minion, shoots the nearest enemy hero, then minion, then a
/// Bastion it may hurt, with its own aim, uses its abilities on cooldown and takes a random upgrade at
/// each level. Bots never leave the lane (camps and flanks are for people), so this checks pacing, not
/// strategy: dotnet run --project Tests/CoreSim -- report overrun [matches] [seed] [players per team] [Bastion scale to try].
/// </summary>
static class OverrunSim
{
    const float Dt = OverrunHarness.Dt;

    sealed class Rng
    {
        uint s;
        public Rng(int seed) { s = (uint)seed * 2654435761u ^ 0x5bd1e995u; if (s == 0) s = 1; }
        public float Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFFFF) / (float)0x1000000; }
        public float Range(float a, float b) => a + (b - a) * Next();
    }

    sealed class Bot
    {
        public int id;
        public TeamId team;
        public float accuracy, standBack, side;
    }

    public sealed class Result
    {
        public TeamId winner;
        public MatchEndReason reason;
        public float seconds;
        public TeamId firstBastion = TeamId.None;
        public float firstBastionAt;
        /// <summary>Ahead on enemy Bastion health taken at 7:30, or None if level.</summary>
        public TeamId aheadAtHalf = TeamId.None;
        public int bastionsA, bastionsB;
        public float avgLevel;
        public int heroDeaths, minionsKilled;
    }

    static readonly string[] Heroes = HeroHarness.StarterIds;

    public static Result Play(int seed, int perTeam = 0, float bastionScale = 1f)
    {
        if (perTeam <= 0) perTeam = Ruleset.Default.maxPlayersPerTeam;
        var rng = new Rng(seed);
        var picks = new Dictionary<int, string>();
        for (int i = 1; i <= perTeam; i++)
        {
            picks[i] = Heroes[(int)(rng.Next() * Heroes.Length) % Heroes.Length];
            picks[100 + i] = Heroes[(int)(rng.Next() * Heroes.Length) % Heroes.Length];
        }
        var r = OverrunHarness.QuickRules();
        r.personalShield = Ruleset.Default.personalShield;
        r.maxPlayersPerTeam = Math.Max(r.maxPlayersPerTeam, perTeam); // so the old 5v5 can still be played for comparison
        if (bastionScale != 1f)
        {
            // Tuning: every Bastion's health and shield times this.
            r.overrun = r.overrun.Clone();
            foreach (var u in r.overrun.units) u.maxHealth *= bastionScale;
            r.overrun.forwardShield *= bastionScale;
            r.overrun.coreShield *= bastionScale;
        }
        var h = new OverrunHarness(r, waves: true, perTeam: perTeam, picks: picks, record: false);
        var lane = h.Breakwater.lanes[0];
        float len = lane.Length;

        // A team's skill shifts all its bots a little, so some matches are lopsided.
        float skillA = rng.Range(-0.1f, 0.1f), skillB = rng.Range(-0.1f, 0.1f);
        var bots = h.S.players.Select(p => new Bot
        {
            id = p.id, team = p.team,
            accuracy = Math.Max(0.2f, Math.Min(0.95f, rng.Range(0.4f, 0.75f) + (p.team == TeamId.A ? skillA : skillB))),
            standBack = rng.Range(3f, 9f), side = rng.Range(-3f, 3f),
        }).ToList();

        var res = new Result();
        h.Events.Subscribe(e =>
        {
            switch (e)
            {
                case PlayerSpawnedEvent s:
                    var b = bots.First(x => x.id == s.playerId);
                    float start = b.team == TeamId.A ? 2f : len - 2f;
                    h.Combat.Movement.Teleport(b.id, lane.PointAt(start), b.team == TeamId.A ? 0f : 180f);
                    break;
                case BastionDestroyedEvent d:
                    if (res.firstBastion == TeamId.None) { res.firstBastion = d.byTeam; res.firstBastionAt = h.S.elapsed; }
                    break;
                case PlayerEliminatedEvent _:
                    res.heroDeaths++;
                    break;
                case UnitDestroyedEvent u:
                    if (h.Overrun.Rules.IsMinion(u.typeId) && u.reason == "killed") res.minionsKilled++;
                    break;
            }
        });
        // Everyone starts at their own end of the lane.
        foreach (var b in bots) h.Combat.Movement.Teleport(b.id, lane.PointAt(b.team == TeamId.A ? 2f : len - 2f), 0f);

        float upgradeIn = 0f;
        while (h.S.phase != MatchPhase.MatchEnd && h.S.elapsed < 1200f)
        {
            foreach (var b in bots) Act(h, lane, b, rng);
            if ((upgradeIn -= Dt) <= 0f)
            {
                upgradeIn = 2f;
                foreach (var b in bots)
                    foreach (int level in h.Heroes.Progression.PendingLevels(b.id))
                        h.Heroes.Progression.Choose(b.id, level, rng.Next() < 0.5f ? 0 : 1);
            }
            h.Tick();
            if (res.aheadAtHalf == TeamId.None && Math.Abs(h.S.elapsed - 450f) < Dt * 0.5f) res.aheadAtHalf = h.Overrun.TieBreak();
        }
        res.winner = h.S.winner;
        res.reason = h.S.endReason;
        res.seconds = h.S.elapsed;
        res.bastionsA = h.S.scoreA;
        res.bastionsB = h.S.scoreB;
        res.avgLevel = h.Heroes.Progression.AverageLevel();
        return res;
    }

    static void Act(OverrunHarness h, LaneDef lane, Bot b, Rng rng)
    {
        if (!h.S.IsAlive(b.id)) return;
        var m = h.M(b.id);
        float dir = WaveRules.Dir(b.team);
        float len = lane.Length;

        // Where to stand: a few metres behind the front minion; with none out, by the own Forward (or Core) Bastion.
        float front = float.NaN;
        foreach (var mi in h.Lanes.State.minions)
            if (mi.team == b.team && h.Units.IsAlive(mi.unitId) && (float.IsNaN(front) || (mi.progress - front) * dir > 0f)) front = mi.progress;
        if (float.IsNaN(front))
        {
            var own = h.Bastion(b.team, BastionTier.Forward);
            if (!own.alive) own = h.Bastion(b.team, BastionTier.Core);
            front = lane.Project(own.position, 0f, len, out _) + dir * 4f;
        }
        float hold = Math.Max(0f, Math.Min(len, front - dir * b.standBack));
        float here = lane.Project(m.position, 0f, len, out _);
        float step = h.Combat.Movement.TopSpeed(b.id) * Dt;
        float next = Math.Abs(hold - here) <= step ? hold : here + Math.Sign(hold - here) * step;
        Vec3 walk = lane.DirectionAt(next) * dir;
        Vec3 right = new Vec3(walk.z, 0f, -walk.x);
        h.Combat.Movement.Teleport(b.id, lane.PointAt(next) + right * b.side, WaveRules.Yaw(walk));
        h.Combat.History.Record(b.id, h.S.tick, m.Pose(true));

        int target = PickTarget(h, b, m.position);
        if (target < 0) return;
        // A bot with healing rounds loaded (Mender's Mending Rounds) shoots a hurt teammate first.
        int patient = h.Combat.Weapons.Weapons(b.id).healRounds > 0 ? PickPatient(h, b, m.position) : -1;
        Vec3 aim = h.ChestOf(patient >= 0 ? patient : target);
        if (rng.Next() > b.accuracy) aim = aim + right * 3f + Vec3.Up * 1.5f; // a miss
        Vec3 eye = h.Eye(b.id);
        var hero = h.Combat.Damage.Unit(b.id);
        for (int slot = 0; slot < hero.slots.Count; slot++)
            h.Combat.Apply(new UseAbilityCommand { playerId = b.id, slot = slot, abilityId = "", aimOrigin = eye, aimDirection = (h.ChestOf(target) - eye).Normalized, tick = h.S.tick });
        h.FireAt(b.id, aim);
    }

    /// <summary>The nearest teammate hero under 70% health within 20 m, or -1.</summary>
    static int PickPatient(OverrunHarness h, Bot b, Vec3 at)
    {
        int best = -1;
        float bestDist = 20f;
        foreach (var p in h.S.players)
        {
            if (p.team != b.team || p.id == b.id || !h.S.IsAlive(p.id)) continue;
            if (h.Combat.Damage.Unit(p.id).health >= 0.7f * h.Combat.Damage.MaxHealth(p.id)) continue;
            float d = Vec3.Distance(at, h.M(p.id).position);
            if (d < bestDist) { best = p.id; bestDist = d; }
        }
        return best;
    }

    static int PickTarget(OverrunHarness h, Bot b, Vec3 at)
    {
        int best = -1;
        float bestDist = 20f;
        foreach (var p in h.S.players)
        {
            if (p.team == b.team || !h.S.IsAlive(p.id)) continue;
            float d = Vec3.Distance(at, h.M(p.id).position);
            if (d < bestDist) { best = p.id; bestDist = d; }
        }
        if (best >= 0) return best;
        bestDist = 20f;
        foreach (var u in h.Units.Living())
        {
            if (u.team == b.team || u.team == TeamId.None || !h.Overrun.Rules.IsMinion(u.typeId)) continue;
            float d = Vec3.Distance(at, u.position);
            if (d < bestDist) { best = u.id; bestDist = d; }
        }
        if (best >= 0) return best;
        foreach (var bs in h.Overrun.State.bastions)
            if (bs.team != b.team && bs.alive && Vec3.Distance(at, bs.position) <= 25f && h.Overrun.CanDamage(b.id, bs.unitId)) return bs.unitId;
        return -1;
    }

    /// <summary>dotnet run --project Tests/CoreSim -- report overrun [matches] [seed]</summary>
    public static int Report(string[] args)
    {
        int matches = args.Length > 0 && int.TryParse(args[0], out var n) ? n : 20;
        int seed = args.Length > 1 && int.TryParse(args[1], out var s) ? s : 1;
        int perTeam = args.Length > 2 && int.TryParse(args[2], out var t) ? t : Ruleset.Default.maxPlayersPerTeam;
        float scale = args.Length > 3 ? float.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 1f;
        var rules = Ruleset.Default.overrun;
        Console.WriteLine($"Overrun bot matches on Breakwater's lane, {perTeam} v {perTeam}, random starter heroes, open ground (no walls).");
        Console.WriteLine($"Forward Bastion {rules.Kind(BastionTier.Forward).maxHealth} health + {rules.forwardShield} shield, Core {rules.Kind(BastionTier.Core).maxHealth} + {rules.coreShield}; " +
                          $"Bastions hit for {rules.Kind(BastionTier.Forward).attackDamage}/{rules.Kind(BastionTier.Core).attackDamage} a second at 25 m; late push from minute {rules.latePushMinute}.");
        if (scale != 1f) Console.WriteLine("Trying every Bastion's health and shield x" + scale + ".");
        Console.WriteLine();
        Console.WriteLine(" seed  winner  how             time   first Bastion   ahead at 7:30   Bastions A-B   avg level   hero deaths   minions killed");
        var all = new List<Result>();
        for (int i = 0; i < matches; i++)
        {
            var r = Play(seed + i, perTeam, scale);
            all.Add(r);
            string first = r.firstBastion == TeamId.None ? "-" : r.firstBastion + " at " + Clock(r.firstBastionAt);
            Console.WriteLine($" {seed + i,4}  {r.winner,-6}  {r.reason,-14}  {Clock(r.seconds),5}   {first,-13}   {r.aheadAtHalf,-13}   {r.bastionsA}-{r.bastionsB}            {r.avgLevel,5:0.0}   {r.heroDeaths,11}   {r.minionsKilled,14}");
        }
        Console.WriteLine();
        int inTime = all.Count(r => r.seconds <= 900f + 1f);
        int byCore = all.Count(r => r.reason == MatchEndReason.ScoreReached);
        var withFirst = all.Where(r => r.firstBastion != TeamId.None).ToList();
        var withLead = all.Where(r => r.aheadAtHalf != TeamId.None).ToList();
        Console.WriteLine($"Ended inside 15 minutes (or at the cap): {inTime} of {all.Count}. Won by destroying the Core: {byCore}. Average length {Clock(all.Average(r => r.seconds))}.");
        Console.WriteLine($"First Bastion destroyed by the eventual winner: {withFirst.Count(r => r.firstBastion == r.winner)} of {withFirst.Count}.");
        Console.WriteLine($"Ahead on Bastion damage at 7:30 and won: {withLead.Count(r => r.aheadAtHalf == r.winner)} of {withLead.Count}.");
        Console.WriteLine($"Wins: A {all.Count(r => r.winner == TeamId.A)}, B {all.Count(r => r.winner == TeamId.B)}, none {all.Count(r => r.winner == TeamId.None)}.");
        bool ok = inTime == all.Count && all.All(r => r.winner != TeamId.None);
        Console.WriteLine(ok ? "EVERY MATCH ENDED BY 15 MINUTES WITH A WINNER." : "SOME MATCHES RAN PAST 15 MINUTES OR ENDED WITHOUT A WINNER.");
        return ok ? 0 : 1;
    }

    static string Clock(float seconds) => ((int)seconds / 60) + ":" + ((int)seconds % 60).ToString("00");
}
