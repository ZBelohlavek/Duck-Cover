using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DuckAndCover.Core;

/// <summary>
/// The hero balance pass: weapon damage a second, ability damage and healing per cast, time to kill,
/// one-on-one duels with abilities, four-against-four Overrun bot matches with each path, and an audit
/// of upgrades that change nothing. Builds are level 1 (no picks), level 20 "left" (option 0 at every
/// level) and level 20 "right" (option 1 at every level).
/// dotnet run --project Tests/CoreSim -- report balance [dps|abilities|ttk|duels|matches|upgrades|all] [count] [seed]
/// </summary>
static class BalanceReport
{
    const float Dt = HeroHarness.Dt;
    const int A1 = HeroHarness.A1, A2 = HeroHarness.A2, B1 = HeroHarness.B1, B2 = HeroHarness.B2;
    static readonly string[] Roster = HeroHarness.StarterIds;
    enum Build { L1, Left, Right }
    static readonly Build[] Builds = { Build.L1, Build.Left, Build.Right };
    static string Label(Build b) => b == Build.L1 ? "L1" : b == Build.Left ? "L20 left" : "L20 right";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static int Report(string[] args)
    {
        string what = args.Length > 0 ? args[0] : "all";
        int count = args.Length > 1 && int.TryParse(args[1], out var c) ? c : 0;
        int seed = args.Length > 2 && int.TryParse(args[2], out var s) ? s : 1;
        if (what == "dps" || what == "all") Dps();
        if (what == "abilities" || what == "all") Abilities();
        if (what == "ttk" || what == "all") Ttk();
        if (what == "upgrades" || what == "all") Upgrades();
        if (what == "debugduel")
        {
            var rng0 = new Rng(seed);
            DebugDuel = true;
            for (int i = 0; i < count; i++) { float r0 = Duel(args[3], null, args[4], null, rng0, out float t0); Console.WriteLine("result " + r0 + " in " + t0); }
        }
        if (what == "duels" || what == "all") Duels(count > 0 ? count : 12, seed);
        if (what == "matches" || what == "all") Matches(count > 0 ? count : 40, seed);
        return 0;
    }

    // ------------------------------------------------------------------ setup

    static HeroHarness Make(Dictionary<int, string> picks)
    {
        var h = new HeroHarness(picks);
        return h;
    }

    static void ApplyBuild(HeroHarness h, int id, Build b) => ApplyBuild(h.Heroes, h.S, id, b == Build.L1 ? null : (int?)(b == Build.Left ? 0 : 1));

    /// <summary>Level 20 with the same option at every level (null: stay at level 1). Health topped up.</summary>
    static void ApplyBuild(HeroServer heroes, MatchState s, int id, int? option)
    {
        if (option == null) return;
        var hero = heroes.Heroes.Resolve(s.Player(id).heroId);
        while (heroes.Progression.Level(id) < heroes.Progression.Rules.maxLevel) heroes.Progression.Grant(id, 500f, ExperienceSource.Match);
        foreach (var l in hero.levels.OrderBy(l => l.level))
        {
            var r = heroes.Progression.Choose(id, l.level, option.Value);
            if (!r.IsAccepted) throw new Exception(hero.id + " can't pick " + option + " at " + l.level + ": " + r.reason);
        }
    }

    static void TopUp(HeroHarness h, int id) { var u = h.Unit(id); u.health = h.Combat.Damage.MaxHealth(id); }

    static string WeaponOf(string hero) => ProgressionHarness.DataCatalog().heroes.First(x => x.id == hero).weaponId;
    static bool Melee(string hero) => StarterWeapons.Library().Weapon(WeaponOf(hero)).kind == WeaponKind.Melee;

    /// <summary>Where each hero likes to fight, in metres.</summary>
    static float Preferred(string hero) => hero == "warchief" ? 2.5f : hero == "bulwark" ? 5f : hero == "vampire" ? 5f : hero == "skulk" ? 10f : 12f;

    static bool DebugDuel;
    /// <summary>The hero's range, or closer while a short-reach damaging ability (Serrated Knife) is ready.</summary>
    static float PreferredNow(CombatServer c, int id, string hero)
    {
        float pref = Preferred(hero);
        var u = c.Damage.Unit(id);
        if (u == null) return pref;
        for (int slot = 0; slot < u.slots.Count; slot++)
        {
            var a = c.Abilities.AbilityIn(id, slot);
            if (a == null || u.slots[slot].charges <= 0 || a.targeting.affects != Affects.Enemies) continue;
            if (a.targeting.shape == "arc" && a.targeting.range > 0f && a.targeting.range < pref) pref = a.targeting.range * 0.8f;
        }
        return pref;
    }

    static string F(float v, string fmt = "0") => v.ToString(fmt, Inv);

    // ------------------------------------------------------------------ 1. weapon dps

    struct DpsResult { public float burst, sustained, heal; }

    static DpsResult WeaponDps(string hero, Build b, float dist, bool head, bool ambush)
    {
        var h = Make(new Dictionary<int, string> { [A1] = hero, [B1] = "lancer" });
        ApplyBuild(h, A1, b);
        h.Place(A1, 0f, 0f);
        h.Place(B1, 0f, dist);
        h.Tick();
        TopUp(h, A1);
        float dmg = 0f, burst = 0f, heal = 0f;
        h.Events.Subscribe(e => { if (e is DamagedEvent d && d.attackerId == A1 && d.targetId == B1) dmg += d.amount; });
        const float T = 12f;
        int n = (int)(T / Dt);
        var trig = new Trigger();
        for (int i = 0; i < n; i++)
        {
            TopUp(h, B1);
            var me = h.Unit(A1);
            float max = h.Combat.Damage.MaxHealth(A1);
            me.health = max * 0.6f;
            if (ambush) me.ambushLeft = 5f;
            var pose = h.M(B1).Pose(true);
            Vec3 aim = head ? Hitboxes.HeadCenter(h.R, pose) : Hitboxes.Chest(h.R, pose);
            trig.Pull(h.Combat, h.S, A1, () => h.FireAt(A1, aim));
            h.Tick();
            heal += Math.Max(0f, h.Unit(A1).health - max * 0.6f);
            if (i == (int)(2f / Dt) - 1) burst = dmg / 2f;
        }
        return new DpsResult { burst = burst, sustained = dmg / T, heal = heal / T };
    }

    static void Dps()
    {
        Console.WriteLine("=== 1. Primary fire: damage a second at the chest, perfect aim, against a level-1 Lancer");
        Console.WriteLine("    burst = first 2 s from a full magazine; sustained = 12 s with reloads; heal = health a second regained (life steal, regen) while at 60% health");
        var dists = new[] { 2.5f, 8f, 15f, 30f };
        Console.WriteLine(string.Format("{0,-9} {1,-10} {2,8} {3,8} {4,8} {5,8} {6,8}  {7,9} {8,7}", "hero", "build", "burst8m", "sus2.5m", "sus8m", "sus15m", "sus30m", "head8m", "heal8m"));
        foreach (var hero in Roster)
            foreach (var b in Builds)
            {
                var r = dists.Select(d => WeaponDps(hero, b, d, false, false)).ToArray();
                var hd = WeaponDps(hero, b, 8f, true, false);
                Console.WriteLine(string.Format("{0,-9} {1,-10} {2,8} {3,8} {4,8} {5,8} {6,8}  {7,9} {8,7}", hero, Label(b), F(r[1].burst), F(r[0].sustained), F(r[1].sustained), F(r[2].sustained), F(r[3].sustained), F(hd.sustained), F(r[1].heal, "0.0")));
            }
        Console.WriteLine("Conditional bonuses (ambush forced on, sustained at 8 m):");
        foreach (var b in Builds) Console.WriteLine("  rigger " + Label(b) + ": " + F(WeaponDps("rigger", b, 8f, false, true).sustained) + " (heal " + F(WeaponDps("rigger", b, 8f, false, true).heal, "0.0") + "/s)");
        Console.WriteLine();
    }

    // ------------------------------------------------------------------ 2. abilities

    struct CastResult { public float toOne, toAll, healAlly, healSelf; public bool accepted; public string why; }

    static CastResult Cast(string hero, Build b, int slot, bool atAlly, float dist)
    {
        var h = Make(new Dictionary<int, string> { [A1] = hero, [A2] = "lancer", [B1] = "lancer", [B2] = "lancer" });
        ApplyBuild(h, A1, b);
        h.Place(A1, 0f, 0f);
        h.Place(A2, 2f, 1f);
        h.Place(B1, 0f, dist);
        h.Place(B2, 1.2f, dist);
        h.Tick();
        h.Unit(A1).health = 40f;
        h.Unit(A2).health = 20f;
        h.Forget();
        var res = h.UseAt(A1, slot, atAlly ? A2 : B1);
        var r = new CastResult { accepted = res.IsAccepted, why = res.reason };
        if (!res.IsAccepted) return r;
        h.Run(3f);
        h.Place(B1, 300f, 300f); // they walk out of anything lingering
        h.Place(B2, -300f, 300f);
        h.Run(10f);
        float b1 = h.Of<DamagedEvent>().Where(e => e.attackerId == A1 && e.targetId == B1).Sum(e => e.amount);
        float b2 = h.Of<DamagedEvent>().Where(e => e.attackerId == A1 && e.targetId == B2).Sum(e => e.amount);
        r.toOne = Math.Max(b1, b2);
        r.toAll = b1 + b2;
        r.healAlly = h.Of<HealedEvent>().Where(e => e.healerId == A1 && e.targetId == A2).Sum(e => e.amount);
        r.healSelf = h.Of<HealedEvent>().Where(e => e.healerId == A1 && e.targetId == A1).Sum(e => e.amount);
        return r;
    }

    static void Abilities()
    {
        Console.WriteLine("=== 2. Abilities: one cast at an enemy pair 4 m away (or at a hurt teammate), totals over 13 s");
        Console.WriteLine("    dmg1 = most to one enemy, dmgAll = both enemies, healTm/healMe = healing to a teammate / self; cd = effective cooldown");
        Console.WriteLine(string.Format("{0,-9} {1,-22} {2,-10} {3,5} {4,6} {5,7} {6,7} {7,7}", "hero", "ability", "build", "cd", "dmg1", "dmgAll", "healTm", "healMe"));
        var lib = HeroHarness.DataLibrary();
        foreach (var hero in Roster)
        {
            var def = ProgressionHarness.DataCatalog().heroes.First(x => x.id == hero);
            var slots = HeroServer.Slots(def);
            for (int slot = 0; slot < slots.Length; slot++)
                foreach (var b in Builds)
                {
                    var foe = Cast(hero, b, slot, false, 4f);
                    var ally = Cast(hero, b, slot, true, 4f);
                    var h = Make(new Dictionary<int, string> { [A1] = hero });
                    ApplyBuild(h, A1, b);
                    h.Tick();
                    float cd = h.Abilities.EffectiveCooldown(A1, slot);
                    string note = !foe.accepted && !ally.accepted ? "  (refused: " + foe.why + ")" : "";
                    Console.WriteLine(string.Format("{0,-9} {1,-22} {2,-10} {3,5} {4,6} {5,7} {6,7} {7,7}{8}", hero, lib.Ability(slots[slot]).displayName, Label(b), F(cd, "0.#"),
                        F(Math.Max(foe.toOne, ally.toOne)), F(Math.Max(foe.toAll, ally.toAll)), F(Math.Max(foe.healAlly, ally.healAlly)), F(Math.Max(foe.healSelf, ally.healSelf)), note));
                }
        }
        Console.WriteLine();
    }

    // ------------------------------------------------------------------ 3. time to kill

    /// <summary>Seconds for <paramref name="att"/> to kill <paramref name="def"/> (same build tier), perfect aim at the chest. NaN = not within 30 s.</summary>
    static float TimeToKill(string att, string def, Build b, float dist, bool abilities, float accuracy = 1f, Dictionary<string, float> bySource = null)
    {
        var h = Make(new Dictionary<int, string> { [A1] = att, [B1] = def });
        if (bySource != null) h.Events.Subscribe(e => { if (e is DamagedEvent d && d.attackerId == A1 && d.targetId == B1) { string k = (string.IsNullOrEmpty(d.abilityId) ? "?" : d.abilityId); bySource[k] = bySource.GetValueOrDefault(k) + d.amount; } });
        ApplyBuild(h, A1, b);
        ApplyBuild(h, B1, b);
        h.Place(A1, 0f, 0f);
        h.Place(B1, 0f, dist);
        h.Tick();
        TopUp(h, A1);
        TopUp(h, B1);
        var hero = h.Heroes.Heroes.Resolve(att);
        int slots = HeroServer.Slots(hero).Length;
        int shots = 0;
        var trig = new Trigger();
        for (int i = 0; i < (int)(30f / Dt); i++)
        {
            if (!h.S.IsAlive(B1)) return i * Dt;
            if (abilities)
                for (int s = 0; s < slots; s++)
                {
                    var a = h.Abilities.AbilityIn(A1, s);
                    if (a == null || a.targeting.affects != Affects.Enemies && a.targeting.shape != "self") continue;
                    if (a.held || a.effects.Any(e => e != null && e.kind == EffectKinds.HealRounds)) continue;
                    h.UseAt(A1, s, B1);
                }
            var pose = h.M(B1).Pose(true);
            trig.Pull(h.Combat, h.S, A1, () =>
            {
                Vec3 aim = Hitboxes.Chest(h.R, pose);
                // A fixed hit pattern: accuracy 0.5 = every other shot misses.
                bool hit = accuracy >= 1f || ((shots + 1) * accuracy) - Math.Floor(shots * accuracy) >= 1f - 1e-4;
                if (!hit) aim = aim + new Vec3(3f, 1.5f, 0f);
                var res = h.FireAt(A1, aim);
                if (res.IsAccepted) shots++;
                return res;
            });
            h.Tick();
        }
        return h.S.IsAlive(B1) ? float.NaN : 30f;
    }

    static void TtkTable(Build b, bool abilities, float accuracy, string title)
    {
        Console.WriteLine(title);
        Console.WriteLine(string.Format("{0,-12}", "attacker") + string.Join("", Roster.Select(r => string.Format("{0,9}", r))) + "   (range used)");
        foreach (var att in Roster)
        {
            float d = abilities ? Math.Min(Preferred(att), 4f) : Preferred(att);
            Console.WriteLine(string.Format("{0,-12}", att) + string.Join("", Roster.Select(def =>
            {
                float t = TimeToKill(att, def, b, d, abilities, accuracy);
                return string.Format("{0,9}", float.IsNaN(t) ? ">30" : F(t, "0.00"));
            })) + "   " + F(d, "0.#") + " m");
        }
        Console.WriteLine();
    }

    static void Ttk()
    {
        Console.WriteLine("=== 3. Time to kill in seconds (rows attack columns, both at the same build tier, standing still)");
        foreach (var b in Builds)
        {
            TtkTable(b, false, 1f, "-- " + Label(b) + ", primary fire only, every shot at the chest, at the attacker's range");
            TtkTable(b, false, 0.5f, "-- " + Label(b) + ", primary fire only, half the shots miss");
            TtkTable(b, true, 1f, "-- " + Label(b) + ", every damaging ability opened with, then primary fire, at 4 m or closer");
        }
        Console.WriteLine("Fastest openers against a 200-health hero (L1 Lancer target at the same tier), damage by source:");
        foreach (var att in Roster)
            foreach (var b in new[] { Build.Left, Build.Right })
            {
                var src = new Dictionary<string, float>();
                float t = TimeToKill(att, "lancer", b, Math.Min(Preferred(att), 4f), true, 1f, src);
                Console.WriteLine($"  {att,-9} {Label(b),-10} {(float.IsNaN(t) ? ">30" : F(t, "0.00")),5} s: " + string.Join(", ", src.OrderByDescending(k => k.Value).Select(k => k.Key + " " + F(k.Value))));
            }
        Console.WriteLine("Max health: " + string.Join(", ", Roster.Select(r => r + " " + ProgressionHarness.DataCatalog().heroes.First(x => x.id == r).maxHealth)));
        Console.WriteLine();
    }

    // ------------------------------------------------------------------ bots (shared by duels and matches)

    sealed class Rng
    {
        uint s;
        public Rng(int seed) { s = (uint)seed * 2654435761u ^ 0x5bd1e995u; if (s == 0) s = 1; }
        public float Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFFFF) / (float)0x1000000; }
        public float Range(float a, float b) => a + (b - a) * Next();
        public int Pick(int n) => Math.Min(n - 1, (int)(Next() * n));
    }

    /// <summary>
    /// Uses a hero's abilities the way a sensible player would: damage and control at the enemy, healing
    /// and buffs on a hurt teammate or on themselves, a held shield (Rampart) up only while reloading.
    /// </summary>
    static void UseAbilities(CombatServer combat, MatchState s, int me, int enemy, int patient, Func<int, Vec3> chest, Func<int, Vec3> eye, Func<int, Vec3> feet, float enemyDist)
    {
        var unit = combat.Damage.Unit(me);
        var weapons = combat.Weapons.Weapons(me);
        for (int slot = 0; slot < unit.slots.Count; slot++)
        {
            var a = combat.Abilities.AbilityIn(me, slot);
            if (a == null) continue;
            Vec3 from = eye(me);
            Vec3 dir;
            if (a.held)
            {
                bool up = combat.Abilities.IsRaised(me, slot);
                bool want = weapons.Reloading && enemy >= 0;
                if (up == want) continue;
                dir = enemy >= 0 ? chest(enemy) - from : Vec3.Forward;
                combat.Apply(new UseAbilityCommand { playerId = me, slot = slot, abilityId = "", aimOrigin = from, aimDirection = dir.Normalized, tick = s.tick, lower = up });
                continue;
            }
            else if (a.effects.Any(e => e != null && e.kind == EffectKinds.HealRounds))
            {
                if (patient < 0) continue;
                dir = Vec3.Forward;
            }
            else if (a.targeting.affects == Affects.Allies)
            {
                if (patient < 0) continue;
                dir = chest(patient) - from;
            }
            else if (a.targeting.affects == Affects.AlliesAndSelf)
            {
                int who = patient >= 0 ? patient : (combat.Damage.Unit(me).health < 0.75f * combat.Damage.MaxHealth(me) ? me : -1);
                if (who < 0) continue;
                dir = who == me ? (feet(me) + Vec3.Forward * 0.5f) - from : feet(who) - from;
            }
            else if (a.targeting.affects == Affects.Self || a.targeting.shape == "self")
            {
                // Buffs and dashes: in a fight only.
                if (enemy < 0 || enemyDist > 15f) continue;
                dir = chest(enemy) - from;
            }
            else if (a.targeting.origin == "aimPoint" && a.effects.All(e => e == null || e.kind == EffectKinds.Spawn))
            {
                // A wall (Bulkhead): stand it on the ground between us, nearer them.
                if (enemy < 0 || enemyDist > 20f) continue;
                Vec3 at = feet(me) + (feet(enemy) - feet(me)) * 0.6f;
                dir = at - from;
            }
            else
            {
                if (enemy < 0) continue;
                float reach = a.targeting.range > 0f ? a.targeting.range : (a.targeting.radius > 0f && a.targeting.origin == "caster" ? a.targeting.radius : 30f);
                if (enemyDist > reach + 0.5f) continue;
                dir = chest(enemy) - from;
            }
            combat.Apply(new UseAbilityCommand { playerId = me, slot = slot, abilityId = "", aimOrigin = from, aimDirection = dir.Normalized, tick = s.tick });
        }
    }

    /// <summary>Carries out a push the way the Game layer does: straight away, flat.</summary>
    static void ApplyPush(CombatServer combat, PushedEvent p)
    {
        if (UnitIds.IsUnit(p.targetId)) return;
        var m = combat.Movement.Motion(p.targetId);
        if (m == null || p.distance <= 0f) return;
        Vec3 dir = p.direction;
        if (p.mode != PushDirections.Aim && p.mode != PushDirections.Leap && dir.Magnitude < 1e-3f) dir = m.position - p.origin;
        dir = new Vec3(dir.x, 0f, dir.z);
        if (dir.Magnitude < 1e-3f) return;
        float dist = p.distance;
        if (p.mode == PushDirections.Leap) dist = Math.Min(dist, Vec3.Distance(new Vec3(m.position.x, 0f, m.position.z), new Vec3(p.origin.x, 0f, p.origin.z)));
        m.position = m.position + dir.Normalized * dist;
    }

    /// <summary>
    /// Fires the way the game's client does: no faster than the weapon's interval (the server allows a
    /// little burst for network jitter; a bot that fired whenever the server let it would double-tap).
    /// </summary>
    sealed class Trigger
    {
        float next = -1f;
        public void Pull(CombatServer c, MatchState s, int id, Func<CommandResult> fire)
        {
            float t = s.tick * Dt;
            var w = c.Weapons.Library.Weapon(c.Weapons.Weapons(id).CurrentId);
            if (w == null) return;
            float iv = c.Weapons.FireInterval(id, w);
            if (next < t - iv) next = t;
            for (int k = 0; k < 4 && next <= t + 1e-4f; k++)
            {
                if (!fire().IsAccepted) break;
                next += iv;
            }
        }
    }

    // ------------------------------------------------------------------ 4. duels

    sealed class Tally { public int wins, games; public float time; public void Add(bool won, float t) { games++; if (won) wins++; time += t; } public string Rate => games == 0 ? "-" : F(100f * wins / games) + "%"; }

    /// <summary>1 if A1 wins, 0 if B1, 0.5 draw. Both bots fight with abilities, aim with misses, and close or keep range.</summary>
    static float Duel(string ha, int? pa, string hb, int? pb, Rng rng, out float seconds)
    {
        var h = Make(new Dictionary<int, string> { [A1] = ha, [B1] = hb });
        ApplyBuild(h.Heroes, h.S, A1, pa);
        ApplyBuild(h.Heroes, h.S, B1, pb);
        h.Events.Subscribe(e => { if (e is PushedEvent p) ApplyPush(h.Combat, p); });
        if (DebugDuel) h.Events.Subscribe(e =>
        {
            if (e is DamagedEvent dd) Console.WriteLine($"  t={F(h.S.tick * Dt, "0.00")} {dd.attackerId}->{dd.targetId} {dd.abilityId} {F(dd.amount)} left {F(dd.healthLeft)}");
            if (e is AbilityUsedEvent au) Console.WriteLine($"  t={F(h.S.tick * Dt, "0.00")} {au.playerId} uses {au.abilityId}");
            if (e is ObjectSpawnedEvent os) Console.WriteLine($"  t={F(h.S.tick * Dt, "0.00")} object {os.defId} by {os.ownerId} at {F(os.position.x,"0.0")},{F(os.position.z,"0.0")}  A1 {F(h.M(A1).position.z,"0.0")} B1 {F(h.M(B1).position.z,"0.0")}");
            if (e is PushedEvent pe) Console.WriteLine($"  t={F(h.S.tick * Dt, "0.00")} push {pe.targetId} {pe.mode} {F(pe.distance)}");
        });
        float d = 16f;
        h.Place(A1, 0f, 0f);
        h.Place(B1, 0f, d);
        h.Tick();
        TopUp(h, A1);
        TopUp(h, B1);
        var acc = new Dictionary<int, float> { [A1] = rng.Range(0.45f, 0.75f), [B1] = rng.Range(0.45f, 0.75f) };
        var pref = new Dictionary<int, float> { [A1] = Preferred(ha), [B1] = Preferred(hb) };
        var trig = new Dictionary<int, Trigger> { [A1] = new Trigger(), [B1] = new Trigger() };
        int n = (int)(60f / Dt);
        int flip = rng.Pick(2);
        for (int i = 0; i < n; i++)
        {
            if (!h.S.IsAlive(A1) || !h.S.IsAlive(B1)) break;
            // Range: whoever wants to be closer closes in at their speed less a backpedal.
            Vec3 pa3 = h.M(A1).position, pb3 = h.M(B1).position;
            float dist = Vec3.Distance(new Vec3(pa3.x, 0, pa3.z), new Vec3(pb3.x, 0, pb3.z));
            pref[A1] = PreferredNow(h.Combat, A1, ha);
            pref[B1] = PreferredNow(h.Combat, B1, hb);
            float want = Math.Min(pref[A1], pref[B1]);
            int chaser = pref[A1] <= pref[B1] ? A1 : B1;
            float speed = h.Combat.Movement.TopSpeed(chaser) * 0.5f;
            if (dist > want + 0.1f)
            {
                var cm = h.M(chaser);
                Vec3 to = (chaser == A1 ? pb3 - pa3 : pa3 - pb3);
                to = new Vec3(to.x, 0f, to.z).Normalized;
                cm.position = cm.position + to * Math.Min(dist - want, speed * Dt);
            }
            foreach (int id in new[] { A1, B1 })
            {
                var mm = h.M(id);
                Vec3 face = h.M(id == A1 ? B1 : A1).position - mm.position;
                if (new Vec3(face.x, 0f, face.z).Magnitude > 1e-3f) mm.yaw = WaveRules.Yaw(new Vec3(face.x, 0f, face.z));
                h.Combat.History.Record(id, h.S.tick, mm.Pose(h.S.IsAlive(id)));
            }
            foreach (int me in ((i + flip) % 2 == 0 ? new[] { A1, B1 } : new[] { B1, A1 }))
            {
                int foe = me == A1 ? B1 : A1;
                float fd = Vec3.Distance(h.M(me).position, h.M(foe).position);
                UseAbilities(h.Combat, h.S, me, foe, -1, id => h.ChestOf(id), id => h.Eye(id), id => h.M(id).position, fd);
                var pose = h.M(foe).Pose(true);
                trig[me].Pull(h.Combat, h.S, me, () =>
                {
                    float roll = rng.Next();
                    Vec3 aim = roll < 0.15f * acc[me] ? Hitboxes.HeadCenter(h.R, pose) : Hitboxes.Chest(h.R, pose);
                    if (rng.Next() > acc[me]) aim = aim + new Vec3(3f, 1.5f, 0f);
                    var fr = h.FireAt(me, aim);
                    if (DebugDuel && me == B1) Console.WriteLine($"  t={F(h.S.tick * Dt, "0.00")} B1 fires: {(fr.IsAccepted ? "ok" : fr.reason)} dist {F(Vec3.Distance(h.M(A1).position, h.M(B1).position),"0.0")}");
                    return fr;
                });
            }
            h.Tick();
        }
        seconds = h.S.elapsed;
        bool aAlive = h.S.IsAlive(A1), bAlive = h.S.IsAlive(B1);
        if (aAlive && !bAlive) return 1f;
        if (bAlive && !aAlive) return 0f;
        return 0.5f;
    }

    static void Duels(int perPair, int seed)
    {
        Console.WriteLine("=== 4. Duels: one against one, 16 m apart, bots aim with misses (45-75% hit, some heads), use abilities, the shorter-range hero closes in");
        Console.WriteLine($"    {perPair} duels per ordered pair; level-20 duels pick each side's path at random. Win % for the row hero; draws (60 s) count half.");
        var rng = new Rng(seed);
        foreach (var tier in new[] { "L1", "L20" })
        {
            var grid = new Dictionary<(string, string), float>();
            var byHero = Roster.ToDictionary(r => r, r => new Tally());
            var byPath = new Dictionary<(string, int), Tally>();
            foreach (var r in Roster) { byPath[(r, 0)] = new Tally(); byPath[(r, 1)] = new Tally(); }
            float totalTime = 0f; int games = 0, draws = 0;
            foreach (var a in Roster)
                foreach (var b in Roster)
                {
                    float score = 0f;
                    for (int i = 0; i < perPair; i++)
                    {
                        int? pa = tier == "L1" ? null : (int?)rng.Pick(2), pb = tier == "L1" ? null : (int?)rng.Pick(2);
                        float res = Duel(a, pa, b, pb, rng, out float t);
                        score += res;
                        totalTime += t; games++;
                        if (res == 0.5f) draws++;
                        byHero[a].games++; byHero[b].games++;
                        byHero[a].time += res; byHero[b].time += 1f - res; // time used as score sum
                        if (pa != null) { byPath[(a, pa.Value)].games++; byPath[(a, pa.Value)].time += res; byPath[(b, pb.Value)].games++; byPath[(b, pb.Value)].time += 1f - res; }
                    }
                    grid[(a, b)] = score / perPair;
                }
            Console.WriteLine($"-- {tier}: average duel {F(totalTime / games, "0.0")} s, draws {draws} of {games}");
            Console.WriteLine(string.Format("{0,-10}", "row beats") + string.Join("", Roster.Select(r => string.Format("{0,9}", r))) + "   overall");
            foreach (var a in Roster)
                Console.WriteLine(string.Format("{0,-10}", a) + string.Join("", Roster.Select(b => string.Format("{0,9}", F(100f * grid[(a, b)]) + "%"))) + "   " + F(100f * byHero[a].time / byHero[a].games) + "%");
            if (tier == "L20")
            {
                Console.WriteLine("   by path (overall duel score):");
                foreach (var r in Roster)
                    Console.WriteLine($"   {r,-9} left {F(100f * byPath[(r, 0)].time / Math.Max(1, byPath[(r, 0)].games))}%  right {F(100f * byPath[(r, 1)].time / Math.Max(1, byPath[(r, 1)].games))}%");
            }
            Console.WriteLine();
        }
    }

    // ------------------------------------------------------------------ 5. Overrun matches

    sealed class HeroStats
    {
        public int picks, wins, kills, deaths;
        public float heroDamage, unitDamage, healing, abilityHeroDamage;
        public readonly Dictionary<string, int> used = new Dictionary<string, int>();
        public readonly Dictionary<string, int> refused = new Dictionary<string, int>();
    }

    sealed class Bot { public int id; public TeamId team; public string hero; public int path; public float accuracy, standBack, side; public readonly Trigger trigger = new Trigger(); }

    static void Matches(int matches, int seed)
    {
        Console.WriteLine($"=== 5. Overrun bot matches, 4 v 4 on Breakwater's lane, {matches} matches, random heroes, each bot keeps to one path (left or right) all game");
        Console.WriteLine("    Bots walk behind their minions, the shorter-range heroes step in to fight, abilities are aimed as a player would (heals at hurt teammates),");
        Console.WriteLine("    dashes, leaps and knockbacks move bodies. They never go into the jungle, so camps, jungle bonuses and flanks are not tested.");
        var stats = new Dictionary<(string, int), HeroStats>();
        foreach (var r in Roster) { stats[(r, 0)] = new HeroStats(); stats[(r, 1)] = new HeroStats(); }
        var lengths = new List<float>();
        int winsA = 0, capped = 0, totalDeaths = 0;
        for (int m = 0; m < matches; m++)
        {
            var res = PlayMatch(seed + m, stats, out float len, out int deaths);
            lengths.Add(len);
            totalDeaths += deaths;
            if (res == TeamId.A) winsA++;
            if (len >= 899f) capped++;
            Console.Write(m % 10 == 9 ? (m + 1) + "\n" : ".");
        }
        Console.WriteLine();
        Console.WriteLine($"Average length {Clock(lengths.Average())}, {capped} of {matches} went to the 15-minute cap; team A won {winsA}; hero deaths a match {F((float)totalDeaths / matches, "0")}");
        Console.WriteLine(string.Format("{0,-9} {1,-6} {2,6} {3,6} {4,7} {5,7} {6,9} {7,10} {8,9} {9,9}", "hero", "path", "picks", "win%", "kills", "deaths", "heroDmg", "abilityDmg", "unitDmg", "healing"));
        foreach (var r in Roster)
            foreach (int p in new[] { 0, 1 })
            {
                var s = stats[(r, p)];
                if (s.picks == 0) { Console.WriteLine($"{r,-9} {(p == 0 ? "left" : "right"),-6} {0,6}"); continue; }
                Console.WriteLine(string.Format("{0,-9} {1,-6} {2,6} {3,6} {4,7} {5,7} {6,9} {7,10} {8,9} {9,9}", r, p == 0 ? "left" : "right", s.picks, F(100f * s.wins / s.picks) + "%",
                    F((float)s.kills / s.picks, "0.0"), F((float)s.deaths / s.picks, "0.0"), F(s.heroDamage / s.picks), F(s.abilityHeroDamage / s.picks), F(s.unitDamage / s.picks), F(s.healing / s.picks)));
            }
        Console.WriteLine("Ability casts a match (per pick), and the most common refusals:");
        var lib = HeroHarness.DataLibrary();
        foreach (var r in Roster)
        {
            var def = ProgressionHarness.DataCatalog().heroes.First(x => x.id == r);
            int picks = stats[(r, 0)].picks + stats[(r, 1)].picks;
            if (picks == 0) continue;
            var parts = HeroServer.Slots(def).Select(id =>
            {
                int u = stats[(r, 0)].used.GetValueOrDefault(id) + stats[(r, 1)].used.GetValueOrDefault(id);
                var refs = stats[(r, 0)].refused.Concat(stats[(r, 1)].refused).Where(k => k.Key.StartsWith(id + "|")).GroupBy(k => k.Key).Select(g => (g.Key.Substring(id.Length + 1), g.Sum(x => x.Value))).OrderByDescending(x => x.Item2).FirstOrDefault();
                return lib.Ability(id).displayName + " " + F((float)u / picks, "0.0") + (refs.Item1 == null ? "" : " [refused " + F((float)refs.Item2 / picks, "0") + "x: " + refs.Item1 + "]");
            });
            Console.WriteLine($"  {r,-9} " + string.Join("; ", parts));
        }
        Console.WriteLine();
    }

    static TeamId PlayMatch(int seed, Dictionary<(string, int), HeroStats> stats, out float length, out int deaths)
    {
        var rng = new Rng(seed * 7919 + 13);
        int perTeam = 4;
        var picks = new Dictionary<int, string>();
        for (int i = 1; i <= perTeam; i++) { picks[i] = Roster[rng.Pick(Roster.Length)]; picks[100 + i] = Roster[rng.Pick(Roster.Length)]; }
        var r = OverrunHarness.QuickRules();
        r.personalShield = Ruleset.Default.personalShield;
        var h = new OverrunHarness(r, waves: true, perTeam: perTeam, picks: picks, record: false);
        var lane = h.Breakwater.lanes[0];
        float len = lane.Length;
        float skillA = rng.Range(-0.1f, 0.1f), skillB = rng.Range(-0.1f, 0.1f);
        var bots = h.S.players.Select(p => new Bot
        {
            id = p.id, team = p.team, hero = picks[p.id], path = rng.Pick(2),
            accuracy = Math.Max(0.2f, Math.Min(0.95f, rng.Range(0.4f, 0.75f) + (p.team == TeamId.A ? skillA : skillB))),
            standBack = rng.Range(3f, 9f), side = rng.Range(-3f, 3f),
        }).ToList();
        var byId = bots.ToDictionary(b => b.id);
        HeroStats St(int id) => byId.TryGetValue(id, out var b) ? stats[(b.hero, b.path)] : null;
        int dcount = 0;
        h.Events.Subscribe(e =>
        {
            switch (e)
            {
                case PlayerSpawnedEvent s:
                    var b = byId[s.playerId];
                    h.Combat.Movement.Teleport(b.id, lane.PointAt(b.team == TeamId.A ? 2f : len - 2f), b.team == TeamId.A ? 0f : 180f);
                    break;
                case PushedEvent p:
                    ApplyPush(h.Combat, p);
                    break;
                case PlayerEliminatedEvent el:
                    dcount++;
                    St(el.playerId).deaths++;
                    if (el.killerId >= 0 && byId.ContainsKey(el.killerId) && el.killerId != el.playerId) St(el.killerId).kills++;
                    break;
                case DamagedEvent d:
                    var a = St(d.attackerId);
                    if (a == null) break;
                    if (byId.ContainsKey(d.targetId))
                    {
                        if (byId[d.targetId].team == byId[d.attackerId].team) break;
                        a.heroDamage += d.amount;
                        if (!string.IsNullOrEmpty(d.abilityId) && d.abilityId != WeaponOf(byId[d.attackerId].hero)) a.abilityHeroDamage += d.amount;
                    }
                    else a.unitDamage += d.amount;
                    break;
                case HealedEvent he:
                    var hs = St(he.healerId);
                    if (hs != null) hs.healing += he.amount;
                    break;
                case AbilityUsedEvent u:
                    var us = St(u.playerId);
                    if (us != null) us.used[u.abilityId] = us.used.GetValueOrDefault(u.abilityId) + 1;
                    break;
                case AbilityRejectedEvent rj:
                    var rs = St(rj.playerId);
                    if (rs != null && !rj.reason.Contains("cooldown") && !rj.reason.Contains("charge")) rs.refused[rj.abilityId + "|" + rj.reason] = rs.refused.GetValueOrDefault(rj.abilityId + "|" + rj.reason) + 1;
                    break;
            }
        });
        foreach (var b in bots) h.Combat.Movement.Teleport(b.id, lane.PointAt(b.team == TeamId.A ? 2f : len - 2f), 0f);
        float upgradeIn = 0f;
        while (h.S.phase != MatchPhase.MatchEnd && h.S.elapsed < 1200f)
        {
            foreach (var b in bots) Act(h, lane, b, rng);
            if ((upgradeIn -= Dt) <= 0f)
            {
                upgradeIn = 2f;
                foreach (var b in bots)
                    foreach (int level in h.Heroes.Progression.PendingLevels(b.id).ToList())
                        h.Heroes.Progression.Choose(b.id, level, b.path);
            }
            h.Tick();
        }
        foreach (var b in bots)
        {
            var s = stats[(b.hero, b.path)];
            s.picks++;
            if (h.S.winner == b.team) s.wins++;
        }
        length = h.S.elapsed;
        deaths = dcount;
        return h.S.winner;
    }

    static void Act(OverrunHarness h, LaneDef lane, Bot b, Rng rng)
    {
        if (!h.S.IsAlive(b.id)) return;
        var m = h.M(b.id);
        float dir = WaveRules.Dir(b.team);
        float len = lane.Length;
        float front = float.NaN;
        foreach (var mi in h.Lanes.State.minions)
            if (mi.team == b.team && h.Units.IsAlive(mi.unitId) && (float.IsNaN(front) || (mi.progress - front) * dir > 0f)) front = mi.progress;
        if (float.IsNaN(front))
        {
            var own = h.Bastion(b.team, BastionTier.Forward);
            if (!own.alive) own = h.Bastion(b.team, BastionTier.Core);
            front = lane.Project(own.position, 0f, len, out _) + dir * 4f;
        }
        // The nearest enemy hero within 20 m.
        int foe = -1; float foeDist = 20f;
        foreach (var p in h.S.players)
        {
            if (p.team == b.team || !h.S.IsAlive(p.id)) continue;
            float d = Vec3.Distance(m.position, h.M(p.id).position);
            if (d < foeDist) { foe = p.id; foeDist = d; }
        }
        float pref = PreferredNow(h.Combat, b.id, b.hero);
        if (foe >= 0 && pref < 8f && foeDist > pref)
        {
            // Short-range heroes walk at the enemy hero.
            Vec3 to = h.M(foe).position - m.position;
            to = new Vec3(to.x, 0f, to.z);
            float step = Math.Min(foeDist - pref, h.Combat.Movement.TopSpeed(b.id) * Dt);
            h.Combat.Movement.Teleport(b.id, m.position + to.Normalized * step, WaveRules.Yaw(to));
        }
        else
        {
            float hold = Math.Max(0f, Math.Min(len, front - dir * b.standBack));
            float here = lane.Project(m.position, 0f, len, out _);
            float step = h.Combat.Movement.TopSpeed(b.id) * Dt;
            float next = Math.Abs(hold - here) <= step ? hold : here + Math.Sign(hold - here) * step;
            Vec3 walk = lane.DirectionAt(next) * dir;
            Vec3 right = new Vec3(walk.z, 0f, -walk.x);
            h.Combat.Movement.Teleport(b.id, lane.PointAt(next) + right * b.side, WaveRules.Yaw(walk));
        }
        int target0 = foe >= 0 ? foe : PickUnitTarget(h, b, m.position);
        if (target0 >= 0)
        {
            Vec3 face = h.ChestOf(target0) - m.position;
            if (new Vec3(face.x, 0f, face.z).Magnitude > 1e-3f) m.yaw = WaveRules.Yaw(new Vec3(face.x, 0f, face.z));
        }
        h.Combat.History.Record(b.id, h.S.tick, m.Pose(true));

        int target = foe >= 0 ? foe : PickUnitTarget(h, b, m.position);
        int patient = PickPatient(h, b, m.position);
        UseAbilities(h.Combat, h.S, b.id, foe, patient, id => h.ChestOf(id), id => h.Eye(id), id => h.M(id).position, foe >= 0 ? foeDist : 99f);
        if (target < 0) return;
        var weapons = h.Combat.Weapons.Weapons(b.id);
        Vec3 aim = h.ChestOf(weapons.healRounds > 0 && patient >= 0 ? patient : target);
        if (rng.Next() > b.accuracy) aim = aim + new Vec3(2f, 1.5f, 0f);
        if (weapons.healRounds > 0 && patient < 0) { h.Combat.Apply(new ReloadCommand { playerId = b.id }); return; }
        b.trigger.Pull(h.Combat, h.S, b.id, () => h.FireAt(b.id, aim));
    }

    static int PickPatient(OverrunHarness h, Bot b, Vec3 at)
    {
        int best = -1; float bestDist = 20f;
        foreach (var p in h.S.players)
        {
            if (p.team != b.team || p.id == b.id || !h.S.IsAlive(p.id)) continue;
            if (h.Combat.Damage.Unit(p.id).health >= 0.7f * h.Combat.Damage.MaxHealth(p.id)) continue;
            float d = Vec3.Distance(at, h.M(p.id).position);
            if (d < bestDist) { best = p.id; bestDist = d; }
        }
        return best;
    }

    static int PickUnitTarget(OverrunHarness h, Bot b, Vec3 at)
    {
        int best = -1; float bestDist = 20f;
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

    static string Clock(float seconds) => ((int)seconds / 60) + ":" + ((int)seconds % 60).ToString("00");

    // ------------------------------------------------------------------ 6. upgrades that change nothing

    /// <summary>
    /// For each upgrade, at level 20 with every other level on one path: does taking it change any stat
    /// (conditions on or off) or any of the hero's abilities? One that changes nothing in a build is listed.
    /// </summary>
    static void Upgrades()
    {
        Console.WriteLine("=== 6. Upgrade audit: an upgrade that changes no stat and no ability when the other nine picks are all-left or all-right");
        var statIds = typeof(StatIds).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetValue(null))
            .Where(s => !new[] { "add", "percent", "multiply", "set" }.Contains(s)).ToArray();
        int flagged = 0;
        foreach (var hero in Roster)
        {
            var def = ProgressionHarness.DataCatalog().heroes.First(x => x.id == hero);
            foreach (var lvl in def.levels)
                for (int opt = 0; opt < 2; opt++)
                    foreach (int other in new[] { 0, 1 })
                    {
                        string before = Snapshot(hero, lvl.level, -1, other, statIds);
                        string after = Snapshot(hero, lvl.level, opt, other, statIds);
                        if (before == after)
                        {
                            flagged++;
                            var u = def.Upgrade(lvl.options[opt]);
                            Console.WriteLine($"  {hero} L{lvl.level} {u.displayName}: no change with the rest {(other == 0 ? "left" : "right")} ({u.description})");
                        }
                    }
        }
        if (flagged == 0) Console.WriteLine("  none");
        Console.WriteLine("  (Upgrades that change ability numbers only matter if the effect they touch is switched on; those are checked in the duel and match numbers.)");
        Console.WriteLine();
    }

    static string Snapshot(string hero, int level, int option, int otherPath, string[] statIds)
    {
        var h = Make(new Dictionary<int, string> { [A1] = hero });
        var def = h.Heroes.Heroes.Resolve(hero);
        while (h.Heroes.Progression.Level(A1) < h.Heroes.Progression.Rules.maxLevel) h.Heroes.Progression.Grant(A1, 500f, ExperienceSource.Match);
        foreach (var l in def.levels)
        {
            if (l.level == level) { if (option >= 0) h.Heroes.Progression.Choose(A1, l.level, option); continue; }
            h.Heroes.Progression.Choose(A1, l.level, otherPath);
        }
        h.Tick();
        var u = h.Unit(A1);
        var parts = statIds.Select(s => s + "=" + F(u.stats.Get(s, _ => true), "0.####") + "/" + F(u.stats.Get(s, _ => false), "0.####"));
        string abil = string.Join("|", HeroServer.Slots(def).Concat(string.IsNullOrEmpty(def.passiveId) ? new string[0] : new[] { def.passiveId }).Select(id => h.Resolved(A1, id)));
        string slots = string.Join(",", u.slots.Select(s => s.charges));
        return string.Join(";", parts) + abil + slots;
    }
}
