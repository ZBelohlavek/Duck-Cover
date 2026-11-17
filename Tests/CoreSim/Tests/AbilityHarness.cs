using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using DuckAndCover.Core;

/// <summary>
/// A world with positions for the WP7 tests: players are 0.5 m spheres, spawned objects are 0.5 m
/// spheres with a device id, and an optional wall stops every ray at a fixed distance.
/// </summary>
sealed class AbilityWorld : IWorldQuery, IPositionQuery, ILineOfSightQuery
{
    public readonly Dictionary<int, Vec3> Pos = new Dictionary<int, Vec3>();
    public readonly Dictionary<int, ZoneId> OnZone = new Dictionary<int, ZoneId>();
    public readonly Dictionary<int, Vec3> Devices = new Dictionary<int, Vec3>();
    /// <summary>Players nothing has line of sight to.</summary>
    public readonly HashSet<int> Hidden = new HashSet<int>();
    public float WallAt = -1f;
    const float BodyRadius = 0.5f;

    public int[] PlayersInZone(ZoneId zone) => OnZone.Where(kv => kv.Value == zone).Select(kv => kv.Key).ToArray();

    // Reverse id order on purpose: the rules must sort, not rely on the world's order.
    public int[] PlayersInSphere(Vec3 center, float radius, int tick) =>
        Pos.Where(kv => Vec3.Distance(kv.Value, center) <= radius + 1e-4f).Select(kv => kv.Key).OrderByDescending(id => id).ToArray();

    public bool Raycast(Vec3 origin, Vec3 dir, float max, int tick, out HitInfo hit)
    {
        hit = HitInfo.Miss;
        float best = max;
        if (WallAt >= 0f && WallAt <= best)
        {
            best = WallAt;
            hit = new HitInfo { kind = HitKind.World, point = origin + dir * WallAt, distance = WallAt, playerId = -1, deviceId = -1, cell = SurfaceCell.None };
        }
        foreach (var kv in Pos)
        {
            if (Vec3.Distance(kv.Value, origin) < BodyRadius + 0.1f) continue; // the caster
            if (Hit(origin, dir, kv.Value, out float t) && t < best)
            {
                best = t;
                hit = new HitInfo { kind = HitKind.Player, point = origin + dir * t, distance = t, playerId = kv.Key, deviceId = -1, cell = SurfaceCell.None };
            }
        }
        foreach (var kv in Devices)
        {
            if (Hit(origin, dir, kv.Value, out float t) && t < best)
            {
                best = t;
                hit = new HitInfo { kind = HitKind.Device, point = origin + dir * t, distance = t, playerId = -1, deviceId = kv.Key, cell = SurfaceCell.None };
            }
        }
        return hit.kind != HitKind.Nothing;
    }

    static bool Hit(Vec3 origin, Vec3 dir, Vec3 center, out float t)
    {
        t = 0f;
        Vec3 oc = center - origin;
        float along = Vec3.Dot(oc, dir);
        if (along < 0f) return false;
        float off2 = oc.SqrMagnitude - along * along;
        if (off2 > BodyRadius * BodyRadius) return false;
        t = along - (float)Math.Sqrt(BodyRadius * BodyRadius - off2);
        return t >= 0f;
    }

    public SurfaceCell CellAt(Vec3 point) => SurfaceCell.None;
    public bool TryGetPosition(int playerId, int tick, out Vec3 position) => Pos.TryGetValue(playerId, out position);
    public bool HasLineOfSight(Vec3 from, int playerId, int tick) => !Hidden.Contains(playerId);
}

/// <summary>
/// A live match with the ability rules on top and the test ability file loaded. Team A is players
/// 1..n, team B 101..100+n. Everyone starts far apart; tests place who matters.
/// </summary>
sealed class AbilityHarness
{
    public const float Dt = 0.05f;

    public readonly AbilityWorld World = new AbilityWorld();
    public readonly GameEventBus Events = new GameEventBus { Record = true };
    public readonly MatchRules Match;
    public readonly AbilityRunner Abilities;
    public readonly AbilityLibrary Library;

    public MatchState S => Match.State;
    public DamageRules Combat => Abilities.Combat;

    public static string DataDir => Path.Combine(Repo.Root, "Assets", "DuckAndCover", "Data", "Abilities");

    /// <summary>Loads a data file the strict way: public fields only, and a key that is no field is an error.</summary>
    public static AbilityDataFile LoadFile(string name = "test_abilities.json") =>
        Parse(File.ReadAllText(Path.Combine(DataDir, name)));

    public static AbilityDataFile Parse(string json) => JsonSerializer.Deserialize<AbilityDataFile>(json,
        new JsonSerializerOptions { IncludeFields = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow });

    public AbilityHarness(int perTeam = 3, Ruleset rules = null, Action<AbilityLibrary> extra = null, bool toPlay = true)
    {
        var r = rules ?? Ruleset.Default;
        Match = new MatchRules(r, ModeDef.CaptureAndHold(), MatchHarness.TestMap(), World, Events);
        Library = AbilityLibrary.From(LoadFile());
        extra?.Invoke(Library);
        Abilities = new AbilityRunner(Match, Library, World, Events);
        Events.Subscribe(e =>
        {
            if (e is ObjectSpawnedEvent s) World.Devices[s.objectId] = s.position;
            if (e is ObjectRemovedEvent x) World.Devices.Remove(x.objectId);
        });
        for (int i = 1; i <= perTeam; i++)
        {
            Match.AddPlayer(i, TeamId.A);
            Match.AddPlayer(100 + i, TeamId.B);
            World.Pos[i] = new Vec3(-500f - 50f * i, 0f, 0f);
            World.Pos[100 + i] = new Vec3(500f + 50f * i, 0f, 0f);
        }
        if (!toPlay) return;
        Check.True(Match.StartMatch().IsAccepted, "match starts");
        while (S.phase != MatchPhase.Play) Match.Tick(Dt);
    }

    public const int A1 = 1, A2 = 2, A3 = 3, B1 = 101, B2 = 102, B3 = 103;

    public HeroUnit Unit(int id) => Abilities.Unit(id);
    public float Health(int id) => Unit(id).health;
    public void Place(int id, float x, float z = 0f) => World.Pos[id] = new Vec3(x, 0f, z);

    public void Give(int id, params string[] abilityIds) =>
        Check.True(Abilities.SetLoadout(id, abilityIds).IsAccepted, "loadout " + string.Join(",", abilityIds));

    /// <summary>Use a slot, aiming along +x unless told otherwise, from where the player stands.</summary>
    public CommandResult Use(int id, int slot = 0, Vec3? aim = null) => Abilities.Apply(new UseAbilityCommand
    {
        playerId = id, tick = S.tick, slot = slot, aimOrigin = World.Pos.TryGetValue(id, out var at) ? at : Vec3.Zero, aimDirection = aim ?? Vec3.Right,
    });

    public void Run(float seconds)
    {
        int ticks = (int)Math.Round(seconds / Dt);
        for (int i = 0; i < ticks; i++) { Match.Tick(Dt); Abilities.Tick(Dt); }
    }

    public List<T> Of<T>() where T : GameEvent => Events.History.OfType<T>().ToList();
    public T Last<T>() where T : GameEvent => Events.History.OfType<T>().LastOrDefault();
    public void Forget() => Events.History.Clear();

    public string Log() => string.Join("\n", Events.History.Select(e => e.tick + " " + e.GetType().Name + " " + string.Join(",", e.GetType().GetFields()
        .Where(f => f.Name != "tick")
        .Select(f => f.Name + "=" + (f.GetValue(e) is Array a ? "[" + string.Join(" ", a.Cast<object>()) + "]" : f.GetValue(e))))));
}
