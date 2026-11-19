using System;
using System.Collections.Generic;
using System.Linq;
using DuckAndCover.Core;

/// <summary>
/// An Overrun match on Breakwater's lane, camps and Bastion spots, with heroes (every ability file,
/// the starter weapons and the hero data), the lane rules and the Overrun rules. The ground is open
/// (no walls: the cloud has no map geometry), so lines of fire are clear. Team A is players 1..n,
/// team B 101..100+n. Waves are off unless asked for; tests spawn minions where they want them.
/// </summary>
sealed class OverrunHarness
{
    public const float Dt = 0.05f;
    public const int A1 = 1, A2 = 2, B1 = 101, B2 = 102;

    public readonly BoxWorld Map = new BoxWorld();
    public readonly GameEventBus Events;
    public readonly MapDef Breakwater = global::DuckAndCover.Core.Breakwater.Build();
    public readonly MatchRules Match;
    public readonly CombatServer Combat;
    public readonly HeroServer Heroes;
    public readonly LaneServer Lanes;
    public readonly OverrunServer Overrun;

    public MatchState S => Match.State;
    public Ruleset R => Match.Rules;
    public UnitRules Units => Combat.Units;

    public static Ruleset QuickRules()
    {
        var r = CombatHarness.QuickRules();
        r.personalShield = 0f; // damage reads straight off health
        return r;
    }

    /// <param name="picks">Hero per player id; players not named play Lancer.</param>
    public OverrunHarness(Ruleset rules = null, bool waves = false, int perTeam = 2, Dictionary<int, string> picks = null, bool record = true)
    {
        var r = rules ?? QuickRules();
        if (!waves) r.lanes.firstWaveSeconds = 100000f;
        Events = new GameEventBus { Record = record };
        Match = new MatchRules(r, ModeDef.Overrun(), Breakwater.ToMatchMap(), Map, Events);
        Combat = new CombatServer(Match, Map, Events, Dt, ArenaLayout.Placeholder(), StarterWeapons.Library(), HeroHarness.DataLibrary());
        Heroes = new HeroServer(Combat, ProgressionHarness.DataCatalog(), Map, Events);
        Lanes = new LaneServer(Match, Combat, Map, Events, Breakwater, Heroes.Progression);
        Overrun = new OverrunServer(Lanes, Events, Breakwater, Heroes.Progression);
        for (int i = 1; i <= perTeam; i++)
        {
            Match.AddPlayer(i, TeamId.A);
            Match.AddPlayer(100 + i, TeamId.B);
        }
        foreach (var p in S.players)
        {
            string hero = picks != null && picks.TryGetValue(p.id, out var h) ? h : "lancer";
            Check.True(Match.Apply(new PickHeroCommand { playerId = p.id, heroId = hero }).IsAccepted, p.id + " picks " + hero);
        }
        Check.True(Match.StartMatch().IsAccepted, "match starts");
        while (S.phase != MatchPhase.Play) Tick();
        Park();
        Tick(); // camps put out their guards on the first tick of play
        foreach (var p in S.players) Combat.Damage.Unit(p.id).shields.Clear();
        Events.History.Clear();
    }

    /// <summary>Every hero far off to the east, out of every Bastion's and minion's reach.</summary>
    public void Park()
    {
        foreach (var p in S.players) Place(p.id, 200f + p.id, p.team == TeamId.A ? -50f : 50f);
    }

    public void Tick()
    {
        Match.Tick(Dt);
        Combat.Tick(Dt);
        Heroes.Tick(Dt);
        Lanes.Tick(Dt);
        Overrun.Tick(Dt);
        // Bodies stay where they were put: record them each tick, as movement does in a real match.
        foreach (var p in S.players)
        {
            var m = Combat.Movement.Motion(p.id);
            if (m != null) Combat.History.Record(p.id, S.tick, m.Pose(S.IsAlive(p.id)));
        }
    }

    public void Run(float seconds, Action each = null)
    {
        int n = (int)Math.Round(seconds / Dt);
        for (int i = 0; i < n && S.phase != MatchPhase.MatchEnd; i++) { Tick(); each?.Invoke(); }
    }

    public PlayerMotion M(int id) => Combat.Movement.Motion(id);

    public void Place(int id, float x, float z, float y = 0f)
    {
        var m = M(id);
        m.position = new Vec3(x, y, z);
        Combat.History.Record(id, S.tick, m.Pose(S.IsAlive(id)));
    }

    public Vec3 Eye(int id) => M(id).position + Vec3.Up * Hitboxes.EyeHeight(R, M(id).crouched);

    public BastionState Bastion(TeamId team, BastionTier tier) => Overrun.State.Bastion(team, tier);

    public Vec3 ChestOf(int id) => UnitIds.IsUnit(id) ? Hitboxes.Chest(R, Units.Unit(id).Pose()) : Hitboxes.Chest(R, M(id).Pose(true));

    public CommandResult FireAt(int shooter, Vec3 target) => Combat.Apply(new FireCommand
    {
        playerId = shooter, tick = S.tick, weaponId = Combat.Weapons.Weapons(shooter).CurrentId, origin = Eye(shooter),
        // Lobbed weapons aim on their arc so the throw lands where the bot points.
        direction = Combat.Weapons.Library.Weapon(Combat.Weapons.Weapons(shooter).CurrentId)?.AimAt(Eye(shooter), target) ?? (target - Eye(shooter)).Normalized,
    });

    /// <summary>A Scrapper of <paramref name="team"/> standing still at a spot (not walking the lane).</summary>
    public int Scrapper(TeamId team, float x, float z) => Units.Spawn(LaneRuleset.Scrapper, team, new Vec3(x, 0f, z));

    /// <summary>Health plus shield of a Bastion, from the combat rules.</summary>
    public float Total(BastionState b)
    {
        var c = Combat.Damage.Unit(b.unitId);
        return c == null ? 0f : c.health + c.Shield;
    }

    /// <summary>Wear a Bastion down to nothing with damage from a minion of the other team (it may be anywhere).</summary>
    public void Destroy(BastionState b)
    {
        int minion = Scrapper(b.team.Other(), 300f, 0f);
        for (int i = 0; i < 1000 && b.alive && S.IsPlaying; i++) Combat.Damage.Damage(minion, b.unitId, 500f);
        Units.Remove(minion);
    }

    public List<T> Of<T>() where T : GameEvent => Events.History.OfType<T>().ToList();
    public void Forget() => Events.History.Clear();
}
