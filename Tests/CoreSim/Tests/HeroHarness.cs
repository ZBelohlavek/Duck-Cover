using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DuckAndCover.Core;

/// <summary>
/// A match in Play with the combat rules, every ability file in Data/Resources/Abilities, the starter
/// weapons and the hero data, joined by a <see cref="HeroServer"/>. Team A is players 1..n, team B
/// 101..100+n. Heroes are picked before the match starts; then tests place players with
/// <see cref="Place"/> and use abilities with <see cref="Use"/>.
/// </summary>
sealed class HeroHarness
{
    public const float Dt = 0.05f;
    public const int A1 = 1, A2 = 2, B1 = 101, B2 = 102;

    public readonly BoxWorld Map = new BoxWorld();
    public readonly GameEventBus Events = new GameEventBus { Record = true };
    public readonly MatchRules Match;
    public readonly CombatServer Combat;
    public readonly HeroServer Heroes;

    public MatchState S => Match.State;
    public Ruleset R => Match.Rules;
    public AbilityRunner Abilities => Combat.Abilities;

    public static string[] StarterIds = { "bulwark", "lancer", "mender", "rigger", "warchief", "skulk", "bolete" };

    /// <summary>Every ability file, loaded the strict way (an unknown key fails).</summary>
    public static AbilityLibrary DataLibrary() =>
        AbilityLibrary.From(Directory.GetFiles(AbilityHarness.DataDir, "*.json").OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => AbilityHarness.Parse(File.ReadAllText(f))).ToArray());

    /// <param name="picks">Hero per player id; players not named pick nothing.</param>
    public HeroHarness(Dictionary<int, string> picks, int perTeam = 2, Ruleset rules = null)
    {
        if (rules == null)
        {
            rules = CombatHarness.QuickRules();
            rules.personalShield = 0f; // so damage numbers read straight off health
        }
        Match = new MatchRules(rules, ModeDef.CaptureAndHold(), MatchHarness.TestMap(), Map, Events);
        Combat = new CombatServer(Match, Map, Events, Dt, ArenaLayout.Placeholder(), StarterWeapons.Library(), DataLibrary());
        Heroes = new HeroServer(Combat, ProgressionHarness.DataCatalog(), Map, Events);
        for (int i = 1; i <= perTeam; i++)
        {
            Match.AddPlayer(i, TeamId.A);
            Match.AddPlayer(100 + i, TeamId.B);
        }
        foreach (var kv in picks) Check.True(Match.Apply(new PickHeroCommand { playerId = kv.Key, heroId = kv.Value }).IsAccepted, kv.Key + " picks " + kv.Value);
        Check.True(Match.StartMatch().IsAccepted, "match starts");
        while (S.phase != MatchPhase.Play) Tick();
        // Everyone in a row along x, a long way apart, facing +z; tests move who they need.
        foreach (var p in S.players) Place(p.id, p.id * 20f, -50f);
        foreach (var p in S.players) NoShield(p.id);
        Events.History.Clear();
    }

    public static HeroHarness With(int a1, string heroA, int b1 = -1, string heroB = null, int perTeam = 2)
    {
        var picks = new Dictionary<int, string> { [a1] = heroA };
        if (b1 >= 0 && heroB != null) picks[b1] = heroB;
        return new HeroHarness(picks, perTeam);
    }

    public void Tick()
    {
        Match.Tick(Dt);
        Combat.Tick(Dt);
        Heroes.Tick(Dt);
        // Bodies stay where they were put: record them each tick, as movement does in a real match.
        foreach (var p in S.players)
        {
            var m = Combat.Movement.Motion(p.id);
            if (m != null) Combat.History.Record(p.id, S.tick, m.Pose(S.IsAlive(p.id)));
        }
    }

    public void Run(float seconds)
    {
        int n = (int)Math.Round(seconds / Dt);
        for (int i = 0; i < n; i++) Tick();
    }

    public HeroUnit Unit(int id) => Combat.Damage.Unit(id);
    public float Health(int id) => Unit(id).health;
    public float Shield(int id) => Unit(id).Shield;
    public float Stat(int id, string stat) => Combat.Damage.Stat(id, stat);
    public PlayerMotion M(int id) => Combat.Movement.Motion(id);
    public void NoShield(int id) => Unit(id).shields.Clear();

    public void Place(int id, float x, float z, float y = 0f)
    {
        var m = M(id);
        m.position = new Vec3(x, y, z);
        Combat.History.Record(id, S.tick, m.Pose(S.IsAlive(id)));
    }

    public Vec3 Eye(int id) => M(id).position + Vec3.Up * Hitboxes.EyeHeight(R, M(id).crouched);
    public Vec3 ChestOf(int id) => Hitboxes.Chest(R, M(id).Pose(true));

    /// <summary>Use the ability in a slot from the player's eyes along <paramref name="dir"/>.</summary>
    public CommandResult Use(int id, int slot, Vec3 dir) => Combat.Apply(new UseAbilityCommand
    {
        playerId = id, slot = slot, abilityId = "", aimOrigin = Eye(id), aimDirection = dir.Normalized, tick = S.tick,
    });

    /// <summary>Aim at someone's chest.</summary>
    public CommandResult UseAt(int id, int slot, int target) => Use(id, slot, ChestOf(target) - Eye(id));

    public CommandResult FireAt(int shooter, Vec3 target) => Combat.Apply(new FireCommand
    {
        playerId = shooter, tick = S.tick, weaponId = Combat.Weapons.Weapons(shooter).CurrentId, origin = Eye(shooter),
        // Lobbed weapons aim on their arc so the throw lands where the test points.
        direction = Combat.Weapons.Library.Weapon(Combat.Weapons.Weapons(shooter).CurrentId)?.AimAt(Eye(shooter), target) ?? (target - Eye(shooter)).Normalized,
    });

    /// <summary>Reach a level (experience straight from the leveling rules) and fill the ultimate.</summary>
    public void LevelTo(int id, int level, bool fillUltimate = true)
    {
        var rules = Heroes.Progression.Rules;
        while (Heroes.Progression.Level(id) < level)
            Heroes.Progression.Grant(id, 500f, ExperienceSource.Match);
        if (fillUltimate) Unit(id).ultimateCharge = R.ultimateChargeMax;
    }

    /// <summary>Pick an ultimate upgrade through leveling.</summary>
    public CommandResult PickForm(int id, string upgradeId)
    {
        int level = Heroes.Progression.Rules.UltimateFormLevel();
        var hero = Heroes.Heroes.Resolve(S.Player(id).heroId);
        int option = Array.IndexOf(hero.Level(level).options, upgradeId);
        return Heroes.Progression.Choose(id, level, option);
    }

    public List<T> Of<T>() where T : GameEvent => Events.History.OfType<T>().ToList();
    public T Last<T>() where T : GameEvent => Events.History.OfType<T>().LastOrDefault();
    public void Forget() => Events.History.Clear();

    /// <summary>The ability as the hero has it now, as objects (talents applied).</summary>
    public ResolvedAbility ResolvedDef(int id, string abilityId) =>
        AbilityParams.Resolve(Abilities.Library.Ability(abilityId), Unit(id).changes, Abilities.Library, R.minCooldownSeconds);

    /// <summary>The ability as the hero has it now (talents applied), objects and enabled tags included, as text.</summary>
    public string Resolved(int id, string abilityId)
    {
        var r = AbilityParams.Resolve(Abilities.Library.Ability(abilityId), Unit(id).changes, Abilities.Library, R.minCooldownSeconds);
        var opts = new JsonSerializerOptions { IncludeFields = true };
        return JsonSerializer.Serialize(r.def, opts) + JsonSerializer.Serialize(r.objects.OrderBy(k => k.Key).Select(k => k.Value), opts)
            + string.Join(",", r.enabled.OrderBy(t => t));
    }

    public static readonly Vec3 Ahead = Vec3.Forward;
}
