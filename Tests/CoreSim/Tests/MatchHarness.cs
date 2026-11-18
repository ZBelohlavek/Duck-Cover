using System;
using System.Collections.Generic;
using System.Linq;
using DuckAndCover.Core;

/// <summary>A world where tests say who stands on which zone. Shared by the WP1 test files.</summary>
sealed class FakeWorld : IWorldQuery
{
    readonly Dictionary<int, ZoneId> standingOn = new Dictionary<int, ZoneId>();

    public void Put(int playerId, ZoneId zone) => standingOn[playerId] = zone;
    public void Put(IEnumerable<int> players, ZoneId zone) { foreach (var p in players) Put(p, zone); }
    public void Clear(int playerId) => standingOn.Remove(playerId);
    public void ClearAll() => standingOn.Clear();

    public int[] PlayersInZone(ZoneId zone) => standingOn.Where(kv => kv.Value == zone).Select(kv => kv.Key).ToArray();
    public int[] PlayersInSphere(Vec3 center, float radius, int tick) => Array.Empty<int>();
    public bool Raycast(Vec3 origin, Vec3 dir, float max, int tick, out HitInfo hit) { hit = HitInfo.Miss; return false; }
    public SurfaceCell CellAt(Vec3 point) => SurfaceCell.None;
}

/// <summary>
/// A match on a test map with a recorded event stream. Team A is players 1..n, team B 101..100+n.
/// "points": 1 (A's forward point), 2 (middle), 3 (B's forward point). "hills": 11, 12, 13, 14.
/// "bases" (Flag Rush): 21 in A's base, 22 in B's; flag spots at x = -10, 0 and 10.
/// </summary>
sealed class MatchHarness
{
    public const float Dt = 0.05f; // 20 server ticks a second

    public static readonly ZoneId NearA = new ZoneId(1), Mid = new ZoneId(2), NearB = new ZoneId(3);
    public static readonly ZoneId[] Hills = { new ZoneId(11), new ZoneId(12), new ZoneId(13), new ZoneId(14) };

    public readonly FakeWorld World = new FakeWorld();
    public readonly GameEventBus Events = new GameEventBus { Record = true };
    public readonly MatchRules Match;

    public MatchState S => Match.State;

    public static MatchMap TestMap() => new MatchMap
    {
        mapId = "test",
        zoneSets =
        {
            new ZoneSetDef
            {
                name = "points",
                zones = { new ZoneDef(1, forwardSpawnFor: TeamId.A), new ZoneDef(2), new ZoneDef(3, forwardSpawnFor: TeamId.B) },
            },
            new ZoneSetDef
            {
                name = "hills",
                zones = { new ZoneDef(11), new ZoneDef(12), new ZoneDef(13), new ZoneDef(14) },
            },
            new ZoneSetDef
            {
                name = "bases",
                zones = { new ZoneDef(21, startOwner: TeamId.A), new ZoneDef(22, startOwner: TeamId.B) },
            },
        },
        flagSpots = { new Vec3(-10f, 0f, 0f), new Vec3(0f, 0f, 0f), new Vec3(10f, 0f, 0f) },
    };

    public MatchHarness(ModeDef mode = null, int perTeam = 5, Ruleset rules = null, bool toPlay = true)
    {
        Match = new MatchRules(rules ?? Ruleset.Default, mode ?? ModeDef.CaptureAndHold(), TestMap(), World, Events);
        for (int i = 1; i <= perTeam; i++)
        {
            Match.AddPlayer(i, TeamId.A);
            Match.AddPlayer(100 + i, TeamId.B);
        }
        if (!toPlay) return;
        Check.True(Match.StartMatch().IsAccepted, "match starts");
        RunTo(MatchPhase.Play);
    }

    public int[] Team(TeamId team) => S.players.Where(p => p.team == team).Select(p => p.id).ToArray();
    public int[] A => Team(TeamId.A);
    public int[] B => Team(TeamId.B);

    public ZoneState Zone(ZoneId id) => S.Zone(id);

    /// <summary>Tick for <paramref name="seconds"/> of game time (rounded to whole ticks).</summary>
    public void Run(float seconds)
    {
        int ticks = (int)Math.Round(seconds / Dt);
        for (int i = 0; i < ticks; i++) Match.Tick(Dt);
    }

    /// <summary>Tick until <paramref name="done"/> holds; returns the game seconds it took (fails past the limit).</summary>
    public float RunUntil(Func<bool> done, float limitSeconds = 1200f, string what = "condition")
    {
        int limit = (int)(limitSeconds / Dt);
        for (int i = 0; i < limit; i++)
        {
            if (done()) return i * Dt;
            Match.Tick(Dt);
        }
        Check.True(done(), what + " within " + limitSeconds + " s");
        return limitSeconds;
    }

    public void RunTo(MatchPhase phase, float limitSeconds = 1200f) => RunUntil(() => S.phase == phase, limitSeconds, "reach " + phase);

    /// <summary>Put players on a zone and run until the team owns it.</summary>
    public void Take(ZoneId zone, TeamId team, int players = 1)
    {
        var ids = Team(team).Take(players).ToArray();
        World.Put(ids, zone);
        RunUntil(() => Zone(zone).owner == team, 60f, team + " takes " + zone);
        foreach (var id in ids) World.Clear(id);
    }

    public List<T> Of<T>() where T : GameEvent => Events.History.OfType<T>().ToList();
    public T Last<T>() where T : GameEvent => Events.History.OfType<T>().LastOrDefault();

    /// <summary>The event history as text, for determinism checks.</summary>
    public string Log() => string.Join("\n", Events.History.Select(e => e.tick + " " + e.GetType().Name + " " + Describe(e)));

    static string Describe(GameEvent e) => string.Join(",", e.GetType().GetFields()
        .Where(f => f.Name != "tick")
        .Select(f => f.Name + "=" + (f.GetValue(e) is int[] a ? "[" + string.Join(" ", a) + "]" : f.GetValue(e))));
}
