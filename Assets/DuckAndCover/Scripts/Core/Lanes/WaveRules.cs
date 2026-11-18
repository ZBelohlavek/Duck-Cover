using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// Minion waves on the map's lanes (WP15). Every <see cref="LaneRuleset.waveSeconds"/> each team
    /// sends a wave from its end of every lane (the wave makeup comes from the minute of the match,
    /// plus a Juggernaut when the team took the Juggernaut camp). Minions walk the lane, and stop to
    /// fight the closest enemy-team hero or unit they can see, but never stray more than the lane's
    /// leash from it; when nothing is left to fight they walk on. They stop at the far end of the
    /// lane, where WP16's Bastions stand. Wardens shield Scrappers near them.
    ///
    /// Fair by construction: every minion decides first and all shots of a tick land together, so
    /// two mirrored waves wear each other down at the same rate and meet in the middle.
    /// </summary>
    public sealed class WaveRules
    {
        readonly MatchRules match;
        readonly CombatServer combat;
        readonly IWorldQuery geometry;
        readonly IGameEvents events;
        readonly List<LaneDef> lanes;
        readonly LaneState state;

        public LaneRuleset Rules { get; }

        MatchState M => match.State;
        UnitRules Units => combat.Units;

        public WaveRules(MatchRules match, CombatServer combat, IWorldQuery geometry, IGameEvents events, LaneRuleset rules, LaneState state, List<LaneDef> lanes)
        {
            this.match = match;
            this.combat = combat;
            this.geometry = geometry;
            this.events = events;
            Rules = rules;
            this.state = state;
            this.lanes = lanes;
            state.nextWaveIn = rules.firstWaveSeconds;
        }

        public IReadOnlyList<LaneDef> Lanes => lanes;

        /// <summary>The team's next wave brings a Juggernaut (the Juggernaut camp's reward).</summary>
        public void QueueJuggernaut(TeamId team) => state.SetJuggernaut(team, true);

        // ---- Waves ----

        /// <summary>Send both teams' waves now on every lane (the timer calls this; tests may too).</summary>
        public void SpawnWaves()
        {
            var row = Rules.RowAt(M.elapsed / 60f);
            foreach (var team in new[] { TeamId.A, TeamId.B })
            {
                bool jugg = state.JuggernautPending(team);
                state.SetJuggernaut(team, false);
                int wave = team == TeamId.A ? ++state.wavesA : ++state.wavesB;
                for (int l = 0; l < lanes.Count; l++)
                {
                    var kinds = new List<string>();
                    int room = Math.Max(0, Rules.maxMinionsPerTeam - state.Alive(team));
                    for (int i = 0; i < row.scrappers + row.wardens && kinds.Count < room; i++)
                    {
                        // Wardens walk in the middle of the line, Scrappers on both sides of them.
                        kinds.Add(i < row.scrappers ? LaneRuleset.Scrapper : LaneRuleset.Warden);
                    }
                    kinds = MiddleOut(kinds);
                    // The Juggernaut walks with the first lane's wave, whatever the cap.
                    if (jugg && l == 0) kinds.Insert(kinds.Count / 2, LaneRuleset.Juggernaut);
                    var ids = new List<int>();
                    for (int i = 0; i < kinds.Count; i++)
                    {
                        float offset = (i - (kinds.Count - 1) * 0.5f) * Rules.waveSpacing;
                        int id = SpawnMinion(kinds[i], team, l, offset);
                        if (id >= 0) ids.Add(id);
                    }
                    Publish(new WaveSpawnedEvent { team = team, laneId = lanes[l].id, wave = wave, unitIds = ids.ToArray(), juggernaut = jugg && l == 0 });
                }
            }
        }

        static List<string> MiddleOut(List<string> kinds)
        {
            var scrappers = kinds.FindAll(k => k == LaneRuleset.Scrapper);
            var others = kinds.FindAll(k => k != LaneRuleset.Scrapper);
            var line = new List<string>();
            int left = scrappers.Count / 2;
            line.AddRange(scrappers.GetRange(0, left));
            line.AddRange(others);
            line.AddRange(scrappers.GetRange(left, scrappers.Count - left));
            return line;
        }

        /// <summary>Put one minion at its team's end of a lane, <paramref name="offset"/> metres to the right of the line.</summary>
        public int SpawnMinion(string typeId, TeamId team, int lane, float offset = 0f)
        {
            var l = lanes[lane];
            float start = team == TeamId.A ? 0f : l.Length;
            Vec3 walk = l.DirectionAt(start) * Dir(team);
            Vec3 pos = l.PointAt(start) + Right(walk) * offset;
            int id = Units.Spawn(typeId, team, pos, Yaw(walk));
            if (id < 0) return -1;
            var def = Units.Library.Unit(typeId);
            state.minions.Add(new MinionState
            {
                unitId = id, team = team, typeId = typeId, lane = lane, progress = start, offset = offset,
                pulseIn = typeId == LaneRuleset.Warden ? Rules.wardenPulseSeconds : 0f,
                cooldown = def == null ? 0f : def.attackSeconds * 0.5f,
            });
            return id;
        }

        // ---- Time ----

        public void Tick(float dt)
        {
            if (!M.IsPlaying) return;
            state.nextWaveIn -= dt;
            if (state.nextWaveIn <= 0f)
            {
                SpawnWaves();
                state.nextWaveIn += Rules.waveSeconds;
            }

            // Everyone decides from where everyone stood at the start of the tick, then all move and
            // all shots land, so the order minions are listed in never favours a team.
            var shots = new List<Shot>();
            var moves = new List<Step>();
            foreach (var m in state.minions.ToArray())
            {
                var u = Units.Unit(m.unitId);
                if (u == null || !u.alive) { state.minions.Remove(m); continue; }
                Think(m, u, dt, shots, moves);
            }
            foreach (var s in moves)
            {
                StepUnit(s.minion.unitId, s.to, s.yaw);
                s.minion.progress = s.along;
                // Stopped short (a wall, Bastion Panel, Lockout Gate): progress is where it actually stands,
                // so a held wave doesn't run its place along the lane on ahead of itself.
                var moved = Units.Unit(s.minion.unitId);
                if (moved != null && Vec3.Distance(moved.position, s.to) > 1e-3f)
                {
                    float window = Rules.lookAhead + 1f;
                    s.minion.progress = lanes[s.minion.lane].Project(moved.position, s.along - window, s.along + window, out _);
                }
            }
            PulseWardens(dt);
            Fire(shots);
        }

        void Think(MinionState m, UnitState u, float dt, List<Shot> shots, List<Step> moves)
        {
            var def = Units.Library.Unit(m.typeId);
            var lane = lanes[m.lane];
            m.cooldown = Math.Max(0f, m.cooldown - dt);
            float speed = Units.Speed(m.unitId);
            float leash = lane.leash;

            m.targetId = def != null && def.attackDamage > 0f ? PickTarget(m, u, def, lane) : -1;
            if (m.targetId >= 0)
            {
                Vec3 at = FeetOf(m.targetId);
                Vec3 toward = LaneDef.Flat(at - u.position);
                float dist = toward.Magnitude;
                if (dist <= def.attackRange && CanHit(m.unitId, m.targetId))
                {
                    Face(u, toward);
                    if (m.cooldown <= 0f)
                    {
                        shots.Add(new Shot { from = m.unitId, to = m.targetId, damage = def.attackDamage });
                        m.cooldown = def.attackSeconds;
                    }
                }
                else Walk(m, u, lane, at, speed * dt, leash, moves);
            }
            else
            {
                float dir = Dir(m.team);
                float len = lane.Length;
                bool atEnd = dir > 0f ? m.progress >= len - Rules.laneEndStop : m.progress <= Rules.laneEndStop;
                if (!atEnd)
                {
                    float ahead = Clamp(m.progress + dir * Rules.lookAhead, 0f, len);
                    Vec3 walk = lane.DirectionAt(m.progress) * dir;
                    Vec3 aim = lane.PointAt(ahead) + Right(walk) * m.offset;
                    Walk(m, u, lane, aim, speed * dt, leash, moves);
                }
            }
        }

        /// <summary>The closest enemy-team hero or unit it can see, near enough to its lane; ties to the lower id. -1 if none.</summary>
        int PickTarget(MinionState m, UnitState u, UnitDef def, LaneDef lane)
        {
            int best = -1;
            float bestDist = float.MaxValue;
            float reach = lane.leash + def.attackRange;
            float window = def.sightRange + lane.leash;
            foreach (int id in Enemies(m.team))
            {
                Vec3 at = FeetOf(id);
                float d = LaneDef.Flat(at - u.position).Magnitude;
                if (d > def.sightRange || d > bestDist || (d == bestDist && id > best)) continue;
                lane.Project(at, m.progress - window, m.progress + window, out float off);
                if (off > reach) continue;
                best = id;
                bestDist = d;
            }
            return best;
        }

        /// <summary>Living heroes and units of the other team, in id order (never neutral units).</summary>
        List<int> Enemies(TeamId team)
        {
            var ids = new List<int>();
            foreach (var p in M.players)
                if (p.team != team && p.team != TeamId.None && M.IsAlive(p.id) && combat.Movement.Motion(p.id) != null) ids.Add(p.id);
            foreach (var other in Units.Living())
                if (other.team != team && other.team != TeamId.None) ids.Add(other.id);
            ids.Sort();
            return ids;
        }

        /// <summary>Plan a step toward a point, kept within the leash of the lane line, and where along it the minion will be.</summary>
        void Walk(MinionState m, UnitState u, LaneDef lane, Vec3 aim, float step, float leash, List<Step> moves)
        {
            Vec3 to = LaneDef.Flat(aim - u.position);
            float len = to.Magnitude;
            if (len < 1e-4f || step <= 0f) return;
            Vec3 next = u.position + to / len * Math.Min(step, len);
            float window = Rules.lookAhead + step + 1f;
            float along = lane.Project(next, m.progress - window, m.progress + window, out float off);
            Vec3 onLine = lane.PointAt(along);
            if (off > leash)
            {
                Vec3 away = LaneDef.Flat(next - onLine);
                next = onLine + away.Normalized * leash;
            }
            next.y = onLine.y;
            moves.Add(new Step { minion = m, to = next, yaw = Yaw(to), along = along });
        }

        void Face(UnitState u, Vec3 toward)
        {
            if (toward.Magnitude < 1e-4f) return;
            u.yaw = Yaw(toward);
        }

        // ---- Wardens ----

        void PulseWardens(float dt)
        {
            foreach (var w in state.minions)
            {
                if (w.typeId != LaneRuleset.Warden) continue;
                w.pulseIn -= dt;
                if (w.pulseIn > 0f) continue;
                w.pulseIn += Rules.wardenPulseSeconds;
                var wu = Units.Unit(w.unitId);
                if (wu == null || !wu.alive) continue;
                foreach (var s in state.minions)
                {
                    if (s.team != w.team || s.typeId != LaneRuleset.Scrapper) continue;
                    var su = Units.Unit(s.unitId);
                    if (su == null || !su.alive || Vec3.Distance(su.position, wu.position) > Rules.wardenRadius) continue;
                    combat.Damage.AddShield(w.unitId, s.unitId, Rules.wardenShield, Rules.wardenShieldSeconds, LaneRuleset.WardenShieldId);
                }
            }
        }

        // ---- Shots ----

        struct Shot { public int from, to; public float damage; }
        struct Step { public MinionState minion; public Vec3 to; public float yaw, along; }

        /// <summary>All the tick's shots land together, so who decides first doesn't matter.</summary>
        void Fire(List<Shot> shots)
        {
            foreach (var s in shots) combat.Damage.Damage(s.from, s.to, s.damage, LaneRuleset.AttackId);
        }

        // ---- The two places units meet the world ----

        /// <summary>Can this unit's shot reach the target? A ray from chest to chest (<see cref="UnitRules.LineOfFire"/>).</summary>
        public bool CanHit(int unitId, int targetId)
        {
            var u = Units.Unit(unitId);
            if (u == null || !u.alive || !combat.Damage.IsAlive(targetId)) return false;
            // Map geometry and enemy barriers and gates block the shot; the unit's own team's let it through (WP20).
            return Units.LineOfFire(unitId, targetId);
        }

        /// <summary>
        /// Move a unit one step (the server's walking; minions never move faster than <see cref="UnitRules.Speed"/>).
        /// It stops short of walls and of Bastion Panels and Lockout Gates (<see cref="UnitRules.Walk"/>).
        /// </summary>
        public void StepUnit(int unitId, Vec3 to, float yaw)
        {
            var u = Units.Unit(unitId);
            if (u == null) return;
            Units.Walk(unitId, to - u.position, yaw);
        }

        // ---- Bodies ----

        Vec3 FeetOf(int id)
        {
            if (UnitIds.IsUnit(id)) { var u = Units.Unit(id); return u == null ? Vec3.Zero : u.position; }
            var m = combat.Movement.Motion(id);
            return m == null ? Vec3.Zero : m.position;
        }

        Vec3 ChestOf(int id)
        {
            if (UnitIds.IsUnit(id)) { var u = Units.Unit(id); return u == null ? Vec3.Zero : Hitboxes.Chest(combat.Rules, u.Pose()); }
            var m = combat.Movement.Motion(id);
            return m == null ? Vec3.Zero : Hitboxes.Chest(combat.Rules, m.Pose(true));
        }

        internal Vec3 PlayerFeet(int id) => FeetOf(id);
        internal Vec3 BodyChest(int id) => ChestOf(id);

        // ---- Helpers ----

        public static float Dir(TeamId team) => team == TeamId.B ? -1f : 1f;
        static Vec3 Right(Vec3 walk) => new Vec3(walk.z, 0f, -walk.x);
        public static float Yaw(Vec3 dir) => (float)(Math.Atan2(dir.x, dir.z) * 180.0 / Math.PI);
        static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

        void Publish(GameEvent e)
        {
            e.tick = M.tick;
            events.Publish(e);
        }
    }
}
