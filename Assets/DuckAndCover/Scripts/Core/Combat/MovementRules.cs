using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// Player-owned movement, checked by the server (the plan's first step; it sits behind this
    /// class so server-run prediction can replace it later without touching weapons or rules).
    /// Each player's machine moves its own body and sends a <see cref="MoveCommand"/>; this keeps a
    /// movement allowance that refills at the player's top speed (sprint, times slows) and refuses
    /// moves that outrun it, rise too far, or come from a stunned or rooted player. A refused move
    /// puts the player back where the server had them. It also places players at spawns and feeds
    /// the lag compensation history once per tick.
    /// </summary>
    public sealed class MovementRules
    {
        sealed class Allowance
        {
            public int playerId;
            public float metres;
        }

        readonly MatchRules match;
        readonly DamageRules combat;
        readonly HitHistory history;
        readonly IGameEvents events;
        readonly List<PlayerMotion> motions = new List<PlayerMotion>();
        readonly List<Allowance> allowances = new List<Allowance>();
        readonly Dictionary<TeamId, int> spawnTurn = new Dictionary<TeamId, int>();
        readonly HashSet<int> dummies = new HashSet<int>();

        public ArenaLayout Layout { get; set; }
        Ruleset Rules => match.Rules;
        MatchState State => match.State;

        public MovementRules(MatchRules match, DamageRules combat, HitHistory history, IGameEvents events, ArenaLayout layout)
        {
            this.match = match ?? throw new ArgumentNullException(nameof(match));
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            this.history = history ?? throw new ArgumentNullException(nameof(history));
            this.events = events ?? throw new ArgumentNullException(nameof(events));
            Layout = layout ?? ArenaLayout.Placeholder();
        }

        public IReadOnlyList<PlayerMotion> Motions => motions;

        public PlayerMotion Motion(int playerId)
        {
            foreach (var m in motions) if (m.playerId == playerId) return m;
            return null;
        }

        /// <summary>Practice dummies come back at the layout's dummy spots instead of a spawn room.</summary>
        public void MarkDummy(int playerId) => dummies.Add(playerId);

        /// <summary>Top speed for the checks right now: sprint speed times the hero's move speed stat (slows).</summary>
        public float TopSpeed(int playerId) => Rules.TopSpeed * combat.MoveSpeed(playerId);

        public bool CanMove(int playerId)
        {
            var u = combat.Unit(playerId);
            return u != null && !u.HasStatus(AbilityLibrary.StunStatus) && !u.HasStatus(AbilityLibrary.RootStatus);
        }

        // ---- Commands ----

        public CommandResult Apply(MoveCommand cmd)
        {
            var p = State.Player(cmd.playerId);
            if (p == null) return CommandResult.Reject("unknown player");
            if (!State.IsAlive(cmd.playerId)) return CommandResult.Accepted; // a move still in flight from before going down
            var m = Motion(cmd.playerId);
            if (m == null) return CommandResult.Reject("not placed yet");
            if (cmd.placement != m.placement) return CommandResult.Accepted; // made before the latest teleport: stale, ignored
            if (!IsFinite(cmd.position) || !IsFinite(cmd.velocity)) return Correct(m, "bad position");

            Vec3 delta = cmd.position - m.position;
            float flat = (float)Math.Sqrt(delta.x * delta.x + delta.z * delta.z);
            var allowance = AllowanceOf(cmd.playerId);
            if (!CanMove(cmd.playerId) && flat > Rules.moveSlack) return Correct(m, "can't move while stunned or rooted");
            if (flat > allowance.metres + Rules.moveSlack) return Correct(m, "moved too fast");
            if (delta.y > Rules.maxRisePerMove) return Correct(m, "rose too far");

            allowance.metres = Math.Max(0f, allowance.metres - flat);
            m.position = cmd.position;
            m.velocity = cmd.velocity;
            m.yaw = cmd.yaw;
            m.pitch = Math.Max(-89f, Math.Min(89f, cmd.pitch));
            m.crouched = cmd.crouched;
            m.grounded = cmd.grounded;
            m.sprinting = cmd.sprinting && !cmd.crouched;
            m.vaulting = cmd.vaulting;
            return CommandResult.Accepted;
        }

        CommandResult Correct(PlayerMotion m, string reason)
        {
            m.placement++;
            m.velocity = Vec3.Zero;
            Publish(new PlayerPlacedEvent { playerId = m.playerId, position = m.position, yaw = m.yaw, placement = m.placement, correction = true });
            return CommandResult.Reject(reason);
        }

        // ---- Server events ----

        /// <summary>Spawns place players; pushes (WP7) add to the allowance by the push distance.</summary>
        public void OnEvent(GameEvent e)
        {
            switch (e)
            {
                case PlayerSpawnedEvent spawned:
                    Place(spawned.playerId, spawned.team, spawned.zone);
                    break;
                case PushedEvent pushed:
                    AllowanceOf(pushed.targetId).metres += Math.Max(0f, pushed.distance);
                    break;
                case PlayerLeftEvent left:
                    history.Forget(left.playerId);
                    break;
            }
        }

        /// <summary>Put a player on a spawn spot (theirs, by turns) and tell everyone.</summary>
        public void Place(int playerId, TeamId team, ZoneId zone)
        {
            var spot = PickSpot(playerId, team, zone);
            var m = Motion(playerId);
            if (m == null)
            {
                m = new PlayerMotion { playerId = playerId };
                motions.Add(m);
                motions.Sort((a, b) => a.playerId.CompareTo(b.playerId));
            }
            m.position = spot.position;
            m.yaw = spot.yaw;
            m.pitch = 0f;
            m.velocity = Vec3.Zero;
            m.crouched = false;
            m.grounded = true;
            m.sprinting = false;
            m.vaulting = false;
            m.placement++;
            AllowanceOf(playerId).metres = MaxAllowance(playerId);
            Publish(new PlayerPlacedEvent { playerId = playerId, position = m.position, yaw = m.yaw, placement = m.placement });
        }

        /// <summary>Move a player somewhere outright (a teleport ability, a test), and tell everyone.</summary>
        public bool Teleport(int playerId, Vec3 position, float yaw)
        {
            var m = Motion(playerId);
            if (m == null) return false;
            m.position = position;
            m.yaw = yaw;
            m.velocity = Vec3.Zero;
            m.placement++;
            AllowanceOf(playerId).metres = MaxAllowance(playerId);
            Publish(new PlayerPlacedEvent { playerId = playerId, position = position, yaw = yaw, placement = m.placement });
            return true;
        }

        SpawnPoint PickSpot(int playerId, TeamId team, ZoneId zone)
        {
            List<SpawnPoint> spots = null;
            if (dummies.Contains(playerId) && Layout.dummies.Count > 0)
            {
                // Each dummy keeps its own spot: the order they were added in.
                int index = 0;
                foreach (var d in SortedDummies()) { if (d == playerId) break; index++; }
                return Layout.dummies[index % Layout.dummies.Count];
            }
            if (zone.IsValid)
            {
                spots = new List<SpawnPoint>();
                foreach (var s in Layout.forward) if (s.zone == zone) spots.Add(s);
            }
            if (spots == null || spots.Count == 0) spots = Layout.Base(team);
            if (spots.Count == 0) return new SpawnPoint();
            spawnTurn.TryGetValue(team, out int turn);
            spawnTurn[team] = turn + 1;
            return spots[turn % spots.Count];
        }

        List<int> SortedDummies()
        {
            var list = new List<int>(dummies);
            list.Sort();
            return list;
        }

        // ---- Time ----

        /// <summary>Refill allowances and record every body in the lag compensation history. Call once per tick, after the match.</summary>
        public void Tick(float dt)
        {
            foreach (var m in motions)
            {
                bool alive = State.IsAlive(m.playerId);
                if (alive && CanMove(m.playerId))
                {
                    var a = AllowanceOf(m.playerId);
                    float cap = MaxAllowance(m.playerId);
                    // Running refills up to the cap. Metres a push gave above it (a dash) are kept while
                    // the dash plays out, then fade at the cap's size a second, so they can't be saved up.
                    if (a.metres <= cap) a.metres = Math.Min(cap, a.metres + TopSpeed(m.playerId) * Rules.moveTolerance * dt);
                    else a.metres = Math.Max(cap, a.metres - cap * dt);
                }
                history.Record(m.playerId, State.tick, m.Pose(alive));
            }
        }

        float MaxAllowance(int playerId) => TopSpeed(playerId) * Rules.moveTolerance * Rules.moveBurstSeconds;

        public void FillSnapshot(MatchSnapshot s)
        {
            s.motion = new List<PlayerMotion>(motions.Count);
            foreach (var m in motions) s.motion.Add(m.Clone());
        }

        Allowance AllowanceOf(int playerId)
        {
            foreach (var a in allowances) if (a.playerId == playerId) return a;
            var made = new Allowance { playerId = playerId, metres = MaxAllowance(playerId) };
            allowances.Add(made);
            return made;
        }

        static bool IsFinite(Vec3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        void Publish(GameEvent e)
        {
            e.tick = State.tick;
            events.Publish(e);
        }
    }
}
