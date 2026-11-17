using System;
using System.Collections.Generic;
using System.Linq;
using DuckAndCover.Core;

/// <summary>A map for the WP4 tests: a list of solid boxes (walls, crates). Players are never in it.</summary>
sealed class BoxWorld : IWorldQuery
{
    public readonly List<(Vec3 min, Vec3 max)> Boxes = new List<(Vec3, Vec3)>();
    public readonly Dictionary<int, ZoneId> OnZone = new Dictionary<int, ZoneId>();

    public void Wall(Vec3 center, Vec3 size) => Boxes.Add((center - size * 0.5f, center + size * 0.5f));

    public bool Raycast(Vec3 o, Vec3 d, float max, int tick, out HitInfo hit)
    {
        hit = HitInfo.Miss;
        float best = max;
        foreach (var (mn, mx) in Boxes)
        {
            if (!Slab(o, d, mn, mx, out float t, out Vec3 n) || t > best) continue;
            best = t;
            hit = new HitInfo { kind = HitKind.World, point = o + d * t, normal = n, distance = t, playerId = -1, deviceId = -1, cell = SurfaceCell.None };
        }
        return hit.kind != HitKind.Nothing;
    }

    static bool Slab(Vec3 o, Vec3 d, Vec3 mn, Vec3 mx, out float t, out Vec3 normal)
    {
        float tmin = 0f, tmax = float.MaxValue;
        normal = Vec3.Zero;
        float[] O = { o.x, o.y, o.z }, D = { d.x, d.y, d.z }, A = { mn.x, mn.y, mn.z }, B = { mx.x, mx.y, mx.z };
        for (int i = 0; i < 3; i++)
        {
            if (Math.Abs(D[i]) < 1e-9f) { if (O[i] < A[i] || O[i] > B[i]) { t = 0; return false; } continue; }
            float t1 = (A[i] - O[i]) / D[i], t2 = (B[i] - O[i]) / D[i];
            float sign = -1f;
            if (t1 > t2) { var k = t1; t1 = t2; t2 = k; sign = 1f; }
            if (t1 > tmin)
            {
                tmin = t1;
                normal = new Vec3(i == 0 ? sign : 0f, i == 1 ? sign : 0f, i == 2 ? sign : 0f);
            }
            tmax = Math.Min(tmax, t2);
            if (tmin > tmax) { t = 0; return false; }
        }
        t = tmin;
        return true;
    }

    public int[] PlayersInZone(ZoneId zone) => OnZone.Where(kv => kv.Value == zone).Select(kv => kv.Key).OrderBy(i => i).ToArray();
    public int[] PlayersInSphere(Vec3 center, float radius, int tick) => Array.Empty<int>();
    public SurfaceCell CellAt(Vec3 point) => SurfaceCell.None;
}

/// <summary>
/// A match in Play with the combat rules on top. Team A is players 1..n, team B 101..100+n. After
/// construction everyone stands where the tests put them with <see cref="Place"/>.
/// </summary>
sealed class CombatHarness
{
    public const float Dt = 0.05f;
    public const int A1 = 1, A2 = 2, B1 = 101, B2 = 102;

    public readonly BoxWorld Map = new BoxWorld();
    public readonly GameEventBus Events = new GameEventBus { Record = true };
    public readonly MatchRules Match;
    public readonly CombatServer Combat;

    public MatchState S => Match.State;
    public Ruleset R => Match.Rules;

    public static Ruleset QuickRules()
    {
        var r = Ruleset.Default;
        r.heroPickSeconds = 0.1f;
        r.deploySeconds = 0.1f;
        return r;
    }

    public CombatHarness(int perTeam = 2, Ruleset rules = null, bool toPlay = true, WeaponLibrary weapons = null)
    {
        Match = new MatchRules(rules ?? QuickRules(), ModeDef.CaptureAndHold(), MatchHarness.TestMap(), Map, Events);
        Combat = new CombatServer(Match, Map, Events, Dt, ArenaLayout.Placeholder(), weapons);
        for (int i = 1; i <= perTeam; i++)
        {
            Match.AddPlayer(i, TeamId.A);
            Match.AddPlayer(100 + i, TeamId.B);
        }
        if (!toPlay) return;
        Check.True(Match.StartMatch().IsAccepted, "match starts");
        while (S.phase != MatchPhase.Play) Tick();
        Forget();
    }

    public void Tick()
    {
        Match.Tick(Dt);
        Combat.Tick(Dt);
    }

    public void Run(float seconds)
    {
        int n = (int)Math.Round(seconds / Dt);
        for (int i = 0; i < n; i++) Tick();
    }

    public PlayerMotion M(int id) => Combat.Movement.Motion(id);
    public HeroUnit Unit(int id) => Combat.Damage.Unit(id);
    public float Health(int id) => Unit(id).health;
    public float Shield(int id) => Unit(id).Shield;
    public WeaponState W(int id) => Combat.Weapons.Weapons(id);

    /// <summary>Teleport a player (as the server would), facing +z, and record it for this tick.</summary>
    public void Place(int id, float x, float z, float y = 0f, bool crouched = false)
    {
        var m = M(id);
        m.position = new Vec3(x, y, z);
        m.crouched = crouched;
        Combat.History.Record(id, S.tick, m.Pose(S.IsAlive(id)));
    }

    public Vec3 Eye(int id) => M(id).position + Vec3.Up * Hitboxes.EyeHeight(R, M(id).crouched);
    public Vec3 ChestOf(int id) => Hitboxes.Chest(R, M(id).Pose(true));
    public Vec3 HeadOf(int id) => Hitboxes.HeadCenter(R, M(id).Pose(true));

    public CommandResult FireAt(int shooter, Vec3 target, int tick = 0, string weaponId = null) => Combat.Apply(new FireCommand
    {
        playerId = shooter, tick = tick, weaponId = weaponId ?? W(shooter).CurrentId, origin = Eye(shooter), direction = (target - Eye(shooter)).Normalized,
    });

    public CommandResult Fire(int shooter, Vec3 origin, Vec3 dir, int tick = 0) => Combat.Apply(new FireCommand
    {
        playerId = shooter, tick = tick, weaponId = W(shooter).CurrentId, origin = origin, direction = dir,
    });

    public CommandResult Move(int id, Vec3 to, bool crouched = false) => Combat.Apply(new MoveCommand
    {
        playerId = id, position = to, yaw = M(id).yaw, placement = M(id).placement, crouched = crouched,
    });

    /// <summary>Arm a player with one weapon only.</summary>
    public void Arm(int id, params string[] weapons) => Check.True(Combat.Weapons.SetLoadout(id, weapons).IsAccepted, "loadout " + string.Join(",", weapons));

    /// <summary>Clear the personal shield so damage numbers read straight off health.</summary>
    public void NoShield(int id) => Unit(id).shields.Clear();

    public List<T> Of<T>() where T : GameEvent => Events.History.OfType<T>().ToList();
    public T Last<T>() where T : GameEvent => Events.History.OfType<T>().LastOrDefault();
    public void Forget() => Events.History.Clear();
}
