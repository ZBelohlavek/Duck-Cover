using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// Units that are not players (WP14): minions, Bastions, camp guards. This makes them exist as
    /// bodies that fight: spawned with a kind's health and size, given a unit id, and put in the hit
    /// history each tick so shots, area abilities, splash and objects find them the way they find
    /// heroes. Damage, healing, shields and statuses come from <see cref="DamageRules"/> and
    /// <see cref="AbilityRunner"/> unchanged. Units are moved by the server, never by a client:
    /// later packages walk them with <see cref="Move"/> at <see cref="Speed"/>, and pushes move them
    /// here. A destroyed unit stays in the history for the lag compensation window, then goes.
    /// What units decide to do (walk the lane, pick a target) is not here: that is WP15.
    /// </summary>
    public sealed class UnitRules
    {
        readonly MatchRules match;
        readonly AbilityRunner abilities;
        readonly HitHistory history;
        readonly IWorldQuery geometry;
        readonly IGameEvents events;

        public UnitRoster Roster { get; }
        public UnitLibrary Library { get; }

        DamageRules Combat => abilities.Combat;
        MatchState State => match.State;
        Ruleset Rules => match.Rules;

        /// <param name="geometry">The map, for stopping pushes at walls (map geometry only, no bodies).</param>
        public UnitRules(MatchRules match, AbilityRunner abilities, HitHistory history, IWorldQuery geometry, IGameEvents events, UnitLibrary library = null)
        {
            this.match = match ?? throw new ArgumentNullException(nameof(match));
            this.abilities = abilities ?? throw new ArgumentNullException(nameof(abilities));
            this.history = history ?? throw new ArgumentNullException(nameof(history));
            this.geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
            this.events = events ?? throw new ArgumentNullException(nameof(events));
            Roster = abilities.Combat.Roster;
            Library = library ?? UnitLibrary.Starter();
            var problems = Library.Validate();
            if (problems.Count > 0) throw new ArgumentException("Can't run units: " + string.Join("; ", problems));
        }

        // ---- Spawning and removing ----

        /// <summary>
        /// Bring a unit of a kind into the match for a team (None = neutral) at a spot, facing
        /// <paramref name="yaw"/> degrees. Returns its unit id, or -1 when the kind is unknown or the
        /// match is over.
        /// </summary>
        public int Spawn(string typeId, TeamId team, Vec3 position, float yaw = 0f)
        {
            var def = Library.Unit(typeId);
            if (def == null || State.phase == MatchPhase.MatchEnd) return -1;
            var u = new UnitState
            {
                id = Roster.nextId++, typeId = def.id, team = team, position = position, yaw = yaw, alive = true,
                maxHealth = def.maxHealth, size = def.size, moveSpeed = def.moveSpeed,
            };
            Roster.units.Add(u);
            Combat.Unit(u.id); // full health
            history.Record(u.id, State.tick, u.Pose());
            Publish(new UnitSpawnedEvent { unitId = u.id, typeId = u.typeId, team = team, position = position, yaw = yaw, maxHealth = u.maxHealth });
            return u.id;
        }

        /// <summary>Take a living unit out without anyone killing it (a wave reset, the match ending).</summary>
        public bool Remove(int unitId) => Combat.DestroyUnit(unitId, -1, "removed");

        // ---- Queries ----

        public UnitState Unit(int unitId) => Roster.Get(unitId);
        public bool IsAlive(int unitId) => Roster.IsAlive(unitId);
        public List<UnitState> Living() => Roster.Living();
        public float Health(int unitId) { var c = Combat.Unit(unitId); return c == null ? 0f : c.health; }

        /// <summary>
        /// Metres a second the unit may walk now: its kind's speed times its moveSpeed stat (slows),
        /// 0 while stunned or rooted. Movement code that walks units should use this.
        /// </summary>
        public float Speed(int unitId)
        {
            var u = Roster.Get(unitId);
            if (u == null || !u.alive) return 0f;
            return u.moveSpeed * abilities.MoveSpeed(unitId);
        }

        /// <summary>Living units within <paramref name="radius"/> of a point (chest to point), in id order.</summary>
        public List<int> InSphere(Vec3 center, float radius)
        {
            var ids = new List<int>();
            foreach (var u in Roster.Living())
                if (Vec3.Distance(Hitboxes.Chest(Rules, u.Pose()), center) <= radius) ids.Add(u.id);
            return ids;
        }

        // ---- Moving ----

        /// <summary>
        /// Put a living unit somewhere (the server's own walking code; no speed check here, so callers
        /// keep to <see cref="Speed"/>). The new spot joins the hit history at this tick.
        /// </summary>
        public bool Move(int unitId, Vec3 position, float yaw)
        {
            var u = Roster.Get(unitId);
            if (u == null || !u.alive) return false;
            u.position = position;
            u.yaw = yaw;
            history.Record(u.id, State.tick, u.Pose());
            return true;
        }

        void OnEvent(GameEvent e)
        {
            if (!(e is PushedEvent pushed) || !UnitIds.IsUnit(pushed.targetId)) return;
            var u = Roster.Get(pushed.targetId);
            if (u == null || !u.alive || pushed.distance <= 0f) return;
            Vec3 dir = new Vec3(pushed.direction.x, 0f, pushed.direction.z);
            if (dir.Magnitude < 1e-4f) return;
            dir = dir.Normalized;
            if (pushed.seconds <= 1e-4f)
            {
                u.position = Slide(u, dir * pushed.distance);
                u.pushLeft = 0f;
                return;
            }
            u.pushVelocity = dir * (pushed.distance / pushed.seconds);
            u.pushLeft = pushed.seconds;
        }

        /// <summary>Move a unit by a step, stopping short of map geometry at chest height.</summary>
        Vec3 Slide(UnitState u, Vec3 step)
        {
            float len = step.Magnitude;
            if (len < 1e-5f) return u.position;
            Vec3 dir = step / len;
            Vec3 chest = Hitboxes.Chest(Rules, u.Pose());
            float radius = Rules.torsoRadius * u.size;
            if (geometry.Raycast(chest, dir, len + radius, State.tick, out var hit) && hit.kind != HitKind.Nothing && hit.kind != HitKind.Player)
                len = Math.Max(0f, hit.distance - radius);
            return u.position + dir * len;
        }

        // ---- Time ----

        /// <summary>
        /// Pushes in progress, then every unit's body into the hit history for this tick. Destroyed
        /// units leave once they are older than the rewind window. Call after movement each tick.
        /// </summary>
        public void Tick(float dt)
        {
            if (dt < 0f) throw new ArgumentOutOfRangeException(nameof(dt));
            float keep = Rules.maxRewindSeconds + dt;
            foreach (var u in Roster.units.ToArray())
            {
                if (!u.alive)
                {
                    u.deadFor += dt;
                    if (u.deadFor > keep)
                    {
                        Roster.units.Remove(u);
                        history.Forget(u.id);
                        continue;
                    }
                }
                else if (u.pushLeft > 0f)
                {
                    float step = Math.Min(dt, u.pushLeft);
                    u.position = Slide(u, u.pushVelocity * step);
                    u.pushLeft -= step;
                }
                history.Record(u.id, State.tick, u.Pose());
            }
        }

        // ---- Snapshot ----

        /// <summary>Living units as the compact list clients draw (id, kind, team, where, health).</summary>
        public void FillSnapshot(MatchSnapshot s)
        {
            var list = new List<UnitSnapshot>();
            foreach (var u in Roster.Living())
            {
                var c = Combat.Unit(u.id);
                var statuses = new string[c == null ? 0 : c.statuses.Count];
                for (int i = 0; i < statuses.Length; i++) statuses[i] = c.statuses[i].statusId;
                list.Add(new UnitSnapshot
                {
                    id = u.id, typeId = u.typeId, team = u.team, position = u.position, yaw = u.yaw,
                    health = c == null ? 0f : c.health,
                    maxHealth = c == null ? u.maxHealth : Combat.MaxHealth(u.id),
                    shield = c == null ? 0f : c.Shield,
                    statuses = statuses,
                });
            }
            s.units = list;
        }

        /// <summary>Listen for pushes. CombatServer calls this once.</summary>
        public void Attach() => events.Subscribe(OnEvent);

        public void Dispose() => events.Unsubscribe(OnEvent);

        void Publish(GameEvent e)
        {
            e.tick = State.tick;
            events.Publish(e);
        }
    }
}
