using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DuckAndCover.Core;
using static HeroHarness;

/// <summary>
/// A stand-in Overrun wave for the hero kit tests (WP20): three Scrappers and a Warden with the
/// numbers WP15 settled (Scrapper 150 health, size 0.7; Warden 200 health, size 0.85, a 25 shield
/// for Scrappers within 6 m every 4 s; both walk at 4 m/s). Test code only: the real kinds live in
/// WP15's lane ruleset, and the Warden's pulse here is a stand-in for its behaviour there.
/// </summary>
static class WaveHarness
{
    public const string Scrapper = "wave_scrapper", Warden = "wave_warden";
    public const float WardenShield = 25f, WardenShieldSeconds = 5f, WardenEvery = 4f, WardenReach = 6f;

    public static void AddKinds(UnitRules units)
    {
        units.Library.Add(new UnitDef { id = Scrapper, name = "Scrapper (stand-in)", maxHealth = 150f, size = 0.7f, moveSpeed = 4f });
        units.Library.Add(new UnitDef { id = Warden, name = "Warden (stand-in)", maxHealth = 200f, size = 0.85f, moveSpeed = 4f });
    }

    /// <summary>
    /// A wave in its walking shape around <paramref name="center"/>: two Scrappers abreast in front,
    /// the third behind them, the Warden at the back, about 1.5 m apart. Scrappers first, Warden last.
    /// </summary>
    public static int[] Spawn(UnitRules units, TeamId team, Vec3 center, float facingZ = -1f)
    {
        if (units.Library.Unit(Scrapper) == null) AddKinds(units);
        float f = facingZ < 0f ? -1f : 1f; // the way the wave walks along z
        float yaw = f < 0f ? 180f : 0f;
        return new[]
        {
            units.Spawn(Scrapper, team, center + new Vec3(-0.8f, 0f, 0.8f * f), yaw),
            units.Spawn(Scrapper, team, center + new Vec3(0.8f, 0f, 0.8f * f), yaw),
            units.Spawn(Scrapper, team, center + new Vec3(0f, 0f, -0.6f * f), yaw),
            units.Spawn(Warden, team, center + new Vec3(0f, 0f, -2f * f), yaw),
        };
    }

    public static Vec3 Center(UnitRules units, int[] wave)
    {
        var alive = wave.Where(units.IsAlive).Select(id => units.Unit(id).position).ToList();
        if (alive.Count == 0) return Vec3.Zero;
        Vec3 sum = Vec3.Zero;
        foreach (var p in alive) sum = sum + p;
        return sum / alive.Count;
    }

    public static int Alive(UnitRules units, int[] wave) => wave.Count(units.IsAlive);

    /// <summary>The Warden's shield pulse: every Scrapper of its team within reach.</summary>
    public static void WardenPulse(CombatServer combat, int warden)
    {
        var u = combat.Units.Unit(warden);
        if (u == null || !u.alive) return;
        foreach (var other in combat.Units.Living())
            if (other.team == u.team && other.typeId == Scrapper && Vec3.Distance(other.position, u.position) <= WardenReach)
                combat.Damage.AddShield(warden, other.id, WardenShield, WardenShieldSeconds, "warden_shield");
    }

    /// <summary>Walk every living unit of the wave one tick along z at its current speed (slows, roots, stuns count).</summary>
    public static void WalkTick(UnitRules units, int[] wave, float dz, float dt)
    {
        foreach (int id in wave)
            if (units.IsAlive(id)) units.Walk(id, new Vec3(0f, 0f, Math.Sign(dz) * units.Speed(id) * dt), dz < 0f ? 180f : 0f);
    }

    /// <summary>A hero at a level: -1 = level 1; 8 = the ultimate's left form; 20 with path 0 or 1 = every left or right pick.</summary>
    public static HeroHarness Hero(string heroId, int level, int path = 0)
    {
        var h = With(A1, heroId);
        var hero = h.Heroes.Heroes.Resolve(heroId);
        if (level >= 20)
        {
            h.LevelTo(A1, 20);
            foreach (var l in hero.levels)
                if (l.options != null && l.options.Length > path)
                    Check.True(h.Heroes.Progression.Choose(A1, l.level, path).IsAccepted, heroId + " takes " + l.options[path]);
        }
        else if (level > 1)
        {
            h.LevelTo(A1, level);
            foreach (var l in hero.levels)
                if (l.level <= level && l.options != null && l.options.Length > path)
                    Check.True(h.Heroes.Progression.Choose(A1, l.level, path).IsAccepted, heroId + " takes " + l.options[path]);
        }
        h.Unit(A1).shields.Clear();
        return h;
    }

    public struct ClearResult
    {
        public float seconds;      // time to clear the wave, or the cap
        public int left;           // units still standing at the cap
        public float damage;       // damage the hero (and their objects) dealt to the wave
        public float abilityShare; // share of it from abilities and objects rather than the weapon
    }

    /// <summary>
    /// A simple bot clearing a standing wave: the hero stands <paramref name="distance"/> m away,
    /// fires at the first living unit, and uses every ability and the ultimate at the wave whenever
    /// they are ready (except loading healing rounds, which only heal). The Warden pulses its shield. Nothing shoots back (minion attacks are WP15's).
    /// </summary>
    public static ClearResult Clear(string heroId, int level, int path, float distance = 9f, float cap = 40f)
    {
        var h = Hero(heroId, level, path);
        h.R.ultimateChargePerSecond = 0f;
        h.Place(A1, 0f, 0f);
        var wave = Spawn(h.Combat.Units, TeamId.B, new Vec3(0f, 0f, distance));
        h.Unit(A1).ultimateCharge = h.R.ultimateChargeMax;
        h.Tick();
        h.Forget();
        float t = 0f, wardenIn = 0f;
        var waveIds = new HashSet<int>(wave);
        while (t < cap && Alive(h.Combat.Units, wave) > 0)
        {
            if (wardenIn <= 0f) { WardenPulse(h.Combat, wave[3]); wardenIn += WardenEvery; }
            int target = wave.First(h.Combat.Units.IsAlive);
            var chest = Hitboxes.Chest(h.R, h.Combat.Units.Unit(target).Pose());
            var center = Center(h.Combat.Units, wave) + Vec3.Up * 0.6f;
            for (int slot = 0; slot < h.Unit(A1).slots.Count; slot++)
            {
                // Healing rounds only heal, and there is nobody to heal here.
                var def = h.Abilities.AbilityIn(A1, slot);
                if (def != null && def.effects.Any(e => e != null && e.kind == EffectKinds.HealRounds)) continue;
                // Aimed abilities at the wave's middle, Tracer Mark at the target.
                var aim = heroId == "lancer" && slot == 1 ? chest : center;
                h.Use(A1, slot, aim - h.Eye(A1));
            }
            h.FireAt(A1, chest);
            h.Tick();
            t += Dt;
            wardenIn -= Dt;
        }
        var hits = h.Of<DamagedEvent>().Where(e => waveIds.Contains(e.targetId) && e.attackerId == A1).ToList();
        float total = hits.Sum(e => e.amount);
        var weapons = h.Combat.Weapons.Weapons(A1).weapons;
        float fromAbilities = hits.Where(e => !weapons.Contains(e.abilityId)).Sum(e => e.amount);
        return new ClearResult { seconds = t, left = Alive(h.Combat.Units, wave), damage = total, abilityShare = total > 0f ? fromAbilities / total : 0f };
    }

    /// <summary>
    /// dotnet run --project Tests/CoreSim -- report kitwaves: how long each starter hero takes to clear
    /// one stand-in wave alone, at level 1, 8 and 20 down each path, and the ultimate charge it gives.
    /// </summary>
    public static int Report(string[] args)
    {
        var r = Ruleset.Default;
        Console.WriteLine("One stand-in wave (3 Scrappers at 150, a Warden at 200, Warden shields 25 every 4 s), standing still, 3 m and 9 m away.");
        Console.WriteLine("The hero fires at the first living unit and uses every ability at the wave when ready. Nothing shoots back.");
        Console.WriteLine("Charge = ultimate charge the hero gets from that damage (" + r.ultimateChargePerDamage + " a point, x" + r.ultimateChargeUnitShare + " against units).");
        Console.WriteLine();
        Console.WriteLine(string.Format("{0,-9} {1,-12} {2,5} {3,8} {4,6} {5,8} {6,10} {7,8}", "hero", "build", "at", "clear s", "left", "damage", "abilities", "charge"));
        foreach (var heroId in StarterIds)
            foreach (var (level, path, label) in new[] { (1, 0, "level 1"), (8, 0, "level 8 L"), (8, 1, "level 8 R"), (20, 0, "level 20 L"), (20, 1, "level 20 R") })
                foreach (float at in new[] { 3f, 9f })
                {
                    var c = Clear(heroId, level, path, at);
                    Console.WriteLine(string.Format("{0,-9} {1,-12} {2,4}m {3,8:0.0} {4,6} {5,8:0} {6,9:0}% {7,8:0.0}", heroId, label, at,
                        c.seconds, c.left, c.damage, c.abilityShare * 100f, c.damage * r.ultimateChargePerDamage * r.ultimateChargeUnitShare));
                }
        return 0;
    }
}
