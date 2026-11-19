using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// Neutral camps (WP15). Each camp starts with neutral guards that stand their ground and shoot
    /// heroes who come near their pad. With every guard down the pad opens, and a team takes it by
    /// standing on it alone for the kind's capture time (an enemy on the pad freezes it, an empty pad
    /// drains it), as on a control point. Taking it gives the reward:
    /// <list type="bullet">
    /// <item>Juggernaut: the team's next wave brings a Juggernaut.</item>
    /// <item>Damage: every hero on the team deals more damage for a while.</item>
    /// <item>Experience: experience split between the team's heroes who helped (hurt a guard or stood on the pad).</item>
    /// <item>Speed: the team's heroes on the pad move faster for a while.</item>
    /// </list>
    /// After its rest time the guards come back.
    /// </summary>
    public sealed class CampRules
    {
        readonly MatchRules match;
        readonly CombatServer combat;
        readonly WaveRules waves;
        readonly IGameEvents events;
        readonly LaneState state;
        readonly List<CampDef> defs;
        readonly Dictionary<int, float> guardCooldown = new Dictionary<int, float>();
        bool started;

        public LaneRuleset Rules { get; }
        /// <summary>Pays the Experience camp's reward. Without it the reward is only announced.</summary>
        public ProgressionRules Progression { get; set; }

        MatchState M => match.State;
        UnitRules Units => combat.Units;

        public CampRules(MatchRules match, CombatServer combat, WaveRules waves, IGameEvents events, LaneRuleset rules, LaneState state, List<CampDef> camps)
        {
            this.match = match;
            this.combat = combat;
            this.waves = waves;
            this.events = events;
            Rules = rules;
            this.state = state;
            defs = camps;
        }

        public CampDef Def(string id)
        {
            foreach (var d in defs) if (d.id == id) return d;
            return null;
        }

        /// <summary>Hurting a guard makes a hero a helper of its camp.</summary>
        public void OnEvent(GameEvent e)
        {
            if (!(e is DamagedEvent d) || UnitIds.IsUnit(d.attackerId) || d.attackerId < 0) return;
            if (!state.guardCamp.TryGetValue(d.targetId, out var camp)) return;
            if (!camp.helpers.Contains(d.attackerId)) camp.helpers.Add(d.attackerId);
        }

        public void Tick(float dt)
        {
            if (!M.IsPlaying) return;
            if (!started)
            {
                started = true;
                foreach (var d in defs)
                {
                    var c = new CampState { id = d.id, kind = d.kind };
                    state.camps.Add(c);
                    SpawnGuards(c, d);
                }
            }
            foreach (var c in state.camps)
            {
                var d = Def(c.id);
                var k = Rules.Camp(c.kind);
                switch (c.phase)
                {
                    case CampPhase.Guarded:
                        GuardsFight(c, d, dt);
                        bool anyUp = false;
                        foreach (int g in c.guardIds) if (Units.IsAlive(g)) anyUp = true;
                        if (!anyUp)
                        {
                            c.phase = CampPhase.Open;
                            c.progress = 0f;
                            c.progressTeam = TeamId.None;
                            Publish(new CampOpenedEvent { campId = c.id, kind = c.kind });
                        }
                        break;
                    case CampPhase.Open:
                        TickPad(c, d, k, dt);
                        break;
                    case CampPhase.Resting:
                        c.respawnIn -= dt;
                        if (c.respawnIn <= 0f)
                        {
                            c.respawnIn = 0f;
                            SpawnGuards(c, d);
                            Publish(new CampRespawnedEvent { campId = c.id, kind = c.kind });
                        }
                        break;
                }
            }
            TickBoosts(dt);
        }

        // ---- Guards ----

        void SpawnGuards(CampState c, CampDef d)
        {
            var k = Rules.Camp(c.kind);
            var spots = new List<Vec3>(d.guardSpots);
            if (spots.Count == 0)
                for (int i = 0; i < k.guards; i++)
                {
                    double a = 2.0 * Math.PI * i / k.guards;
                    spots.Add(d.pad + new Vec3((float)Math.Sin(a), 0f, (float)Math.Cos(a)) * Rules.guardRing);
                }
            var ids = new List<int>();
            for (int i = 0; i < spots.Count; i++)
            {
                var s = spots[i];
                Vec3 face = LaneDef.Flat(d.pad - s);
                int id = Units.Spawn(k.GuardTypeAt(i), TeamId.None, s, face.Magnitude < 1e-4f ? 0f : WaveRules.Yaw(face));
                if (id < 0) continue;
                ids.Add(id);
                state.guardCamp[id] = c;
                guardCooldown[id] = 0f;
            }
            c.guardIds = ids.ToArray();
            c.phase = CampPhase.Guarded;
            c.progress = 0f;
            c.progressTeam = TeamId.None;
            c.contested = false;
            c.helpers.Clear();
        }

        /// <summary>Guards don't move: each shoots the closest hero near its camp that it can hit.</summary>
        void GuardsFight(CampState c, CampDef d, float dt)
        {
            foreach (int g in c.guardIds)
            {
                var u = Units.Unit(g);
                if (u == null || !u.alive) continue;
                var def = Units.Library.Unit(u.typeId);
                float cd = guardCooldown.TryGetValue(g, out var left) ? Math.Max(0f, left - dt) : 0f;
                guardCooldown[g] = cd;
                if (def == null || def.attackDamage <= 0f || cd > 0f) continue;
                int best = -1;
                float bestDist = float.MaxValue;
                foreach (var p in M.players)
                {
                    if (p.team == TeamId.None || !M.IsAlive(p.id) || combat.Movement.Motion(p.id) == null) continue;
                    Vec3 at = waves.PlayerFeet(p.id);
                    if (LaneDef.Flat(at - d.pad).Magnitude > Rules.guardLeash) continue;
                    float dist = Vec3.Distance(waves.BodyChest(p.id), Hitboxes.Chest(combat.Rules, u.Pose()));
                    if (dist > def.attackRange || dist >= bestDist || !waves.CanHit(g, p.id)) continue;
                    best = p.id;
                    bestDist = dist;
                }
                if (best < 0) continue;
                u.yaw = WaveRules.Yaw(LaneDef.Flat(waves.PlayerFeet(best) - u.position));
                combat.Damage.Damage(g, best, def.attackDamage, LaneRuleset.AttackId);
                guardCooldown[g] = def.attackSeconds;
            }
        }

        // ---- The pad ----

        /// <summary>Heroes of a team standing on the pad, alive and connected, in id order.</summary>
        public List<int> OnPad(CampDef d, TeamId team)
        {
            var ids = new List<int>();
            foreach (var p in M.players)
            {
                if (p.team != team || !M.IsAlive(p.id)) continue;
                var m = combat.Movement.Motion(p.id);
                if (m != null && d.OnPad(m.position)) ids.Add(p.id);
            }
            ids.Sort();
            return ids;
        }

        void TickPad(CampState c, CampDef d, CampKindDef k, float dt)
        {
            var onA = OnPad(d, TeamId.A);
            var onB = OnPad(d, TeamId.B);
            c.contested = onA.Count > 0 && onB.Count > 0;
            if (c.contested) return;
            float rate = 1f / k.captureSeconds;
            if (onA.Count == 0 && onB.Count == 0)
            {
                c.progress = Math.Max(0f, c.progress - rate * dt);
                if (c.progress <= 0f) c.progressTeam = TeamId.None;
                return;
            }
            var team = onA.Count > 0 ? TeamId.A : TeamId.B;
            var on = team == TeamId.A ? onA : onB;
            foreach (int id in on) if (!c.helpers.Contains(id)) c.helpers.Add(id);
            float speed = rate * CaptureSpeed(on.Count);
            if (c.progressTeam != team && c.progress > 0f)
            {
                // The other team's progress drains first.
                c.progress = Math.Max(0f, c.progress - speed * dt);
                if (c.progress <= 0f) c.progressTeam = team;
                return;
            }
            c.progressTeam = team;
            c.progress += speed * dt;
            if (c.progress >= 1f - 1e-4f) Take(c, d, k, team, on);
        }

        float CaptureSpeed(int players)
        {
            var table = match.Rules.captureSpeedByPlayers;
            if (players <= 0 || table == null || table.Length == 0) return 1f;
            return table[Math.Min(players, table.Length) - 1];
        }

        void Take(CampState c, CampDef d, CampKindDef k, TeamId team, List<int> onPad)
        {
            c.phase = CampPhase.Resting;
            c.progress = 0f;
            c.progressTeam = TeamId.None;
            c.contested = false;
            c.takenBy = team;
            c.respawnIn = k.respawnSeconds;
            foreach (int g in c.guardIds) { state.guardCamp.Remove(g); guardCooldown.Remove(g); }
            c.guardIds = new int[0];

            var heroes = new List<int>();
            switch (c.kind)
            {
                case CampKind.Juggernaut:
                    waves.QueueJuggernaut(team);
                    heroes.AddRange(onPad);
                    break;
                case CampKind.Damage:
                    foreach (var p in M.players)
                        if (p.team == team && p.connected) { Boost(p.id, CampKind.Damage, StatIds.DamageDealt, k); heroes.Add(p.id); }
                    break;
                case CampKind.Speed:
                    foreach (int id in onPad) { Boost(id, CampKind.Speed, StatIds.MoveSpeed, k); heroes.Add(id); }
                    break;
                case CampKind.Experience:
                    foreach (int id in c.helpers)
                    {
                        var p = M.Player(id);
                        if (p != null && p.team == team && p.connected) heroes.Add(id);
                    }
                    heroes.Sort();
                    if (heroes.Count > 0 && Progression != null)
                    {
                        float share = k.experience / heroes.Count;
                        foreach (int id in heroes) Progression.Grant(id, share, ExperienceSource.Camp);
                    }
                    break;
            }
            heroes.Sort();
            c.helpers.Clear();
            Publish(new CampTakenEvent { campId = c.id, kind = c.kind, team = team, heroes = heroes.ToArray(), respawnSeconds = k.respawnSeconds });
        }

        // ---- Boosts ----

        public static string BoostSource(CampKind kind) => "camp_" + kind.ToString().ToLowerInvariant();

        void Boost(int playerId, CampKind kind, string stat, CampKindDef k)
        {
            combat.Damage.AddModifiers(playerId, BoostSource(kind), new[] { new StatModifier(stat, ModOp.Percent, k.boost) });
            foreach (var b in state.boosts)
                if (b.playerId == playerId && b.kind == kind) { b.timeLeft = k.boostSeconds; return; }
            state.boosts.Add(new CampBoost { playerId = playerId, kind = kind, timeLeft = k.boostSeconds });
        }

        void TickBoosts(float dt)
        {
            foreach (var b in state.boosts.ToArray())
            {
                b.timeLeft -= dt;
                if (b.timeLeft > 0f) continue;
                state.boosts.Remove(b);
                combat.Damage.RemoveModifiers(b.playerId, BoostSource(b.kind));
                Publish(new CampBoostEndedEvent { playerId = b.playerId, kind = b.kind });
            }
        }

        void Publish(GameEvent e)
        {
            e.tick = M.tick;
            events.Publish(e);
        }
    }
}
