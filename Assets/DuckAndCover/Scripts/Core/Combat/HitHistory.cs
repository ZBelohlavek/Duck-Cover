using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// Lag compensation: the server remembers every body (players, and units from WP14, by unit id) for the last
    /// <see cref="Ruleset.maxRewindSeconds"/>, one sample per tick, so a shot can be checked against
    /// the hitboxes as they were at the tick the shooter saw. Between two samples the position is
    /// interpolated. Plain C#; the network library has no part in it.
    /// </summary>
    public sealed class HitHistory
    {
        struct Sample
        {
            public int tick;
            public BodyPose pose;
        }

        sealed class Track
        {
            public int playerId;
            public Sample[] ring;
            public int count;
            public int head; // index of the newest
        }

        readonly Ruleset rules;
        readonly int capacity;
        readonly List<Track> tracks = new List<Track>();

        public int NewestTick { get; private set; } = -1;

        /// <param name="tickSeconds">Seconds per server tick; the history keeps enough ticks for the rewind window.</param>
        public HitHistory(Ruleset rules, float tickSeconds)
        {
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            if (tickSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(tickSeconds));
            capacity = Math.Max(2, (int)Math.Ceiling(rules.maxRewindSeconds / tickSeconds) + 2);
        }

        /// <summary>Ticks of history kept per player.</summary>
        public int Capacity => capacity;

        /// <summary>Remember where a player's body is at a tick. Call once per tick per player, in tick order.</summary>
        public void Record(int playerId, int tick, BodyPose pose)
        {
            var t = TrackOf(playerId, true);
            if (t.count > 0 && t.ring[t.head].tick == tick) { t.ring[t.head].pose = pose; return; }
            t.head = (t.head + 1) % capacity;
            t.ring[t.head] = new Sample { tick = tick, pose = pose };
            if (t.count < capacity) t.count++;
            if (tick > NewestTick) NewestTick = tick;
        }

        public void Forget(int playerId) => tracks.RemoveAll(t => t.playerId == playerId);

        /// <summary>
        /// The player's body at a tick, interpolated between samples, clamped to the oldest and newest
        /// kept. False when the player has no history.
        /// </summary>
        public bool PoseAt(int playerId, int tick, out BodyPose pose)
        {
            pose = default;
            var t = TrackOf(playerId, false);
            if (t == null || t.count == 0) return false;
            var newest = t.ring[t.head];
            if (tick >= newest.tick) { pose = newest.pose; return true; }
            for (int i = 1; i < t.count; i++)
            {
                var older = t.ring[(t.head - i + capacity) % capacity];
                var newer = t.ring[(t.head - i + 1 + capacity) % capacity];
                if (tick < older.tick) continue;
                if (tick == older.tick || newer.tick == older.tick) { pose = older.pose; return true; }
                float k = (float)(tick - older.tick) / (newer.tick - older.tick);
                // Someone who appeared or vanished between the two samples is taken as the nearer one.
                if (older.pose.alive != newer.pose.alive) { pose = k < 0.5f ? older.pose : newer.pose; return true; }
                pose = new BodyPose(Vec3.Lerp(older.pose.position, newer.pose.position, k), k < 0.5f ? older.pose.crouched : newer.pose.crouched, newer.pose.alive, newer.pose.size);
                return true;
            }
            pose = t.ring[(t.head - t.count + 1 + capacity) % capacity].pose; // older than the history: the oldest
            return true;
        }

        /// <summary>Every body with history (players, then units, since unit ids sit above player ids), in id order.</summary>
        public List<int> Players()
        {
            var ids = new List<int>();
            foreach (var t in tracks) ids.Add(t.playerId);
            ids.Sort();
            return ids;
        }

        /// <summary>
        /// The nearest hitbox a ray enters at a tick, skipping <paramref name="ignorePlayer"/> (the
        /// shooter) and anyone <paramref name="passThrough"/> says to (teammates, when friendly fire is
        /// off). Ties go to the lower player id, so every run decides the same.
        /// </summary>
        public bool Raycast(Vec3 origin, Vec3 dir, float max, int tick, int ignorePlayer, out HitInfo hit, Func<int, bool> passThrough = null)
        {
            hit = HitInfo.Miss;
            float best = max;
            foreach (int id in Players())
            {
                if (id == ignorePlayer || (passThrough != null && passThrough(id)) || !PoseAt(id, tick, out var pose)) continue;
                if (!Hitboxes.Raycast(rules, pose, origin, dir, best, out float d, out int part)) continue;
                if (d >= best && hit.kind == HitKind.Player) continue;
                best = d;
                hit = new HitInfo
                {
                    kind = HitKind.Player, point = origin + dir * d, normal = (origin - (origin + dir * d)).Normalized,
                    distance = d, playerId = id, bodyPart = part, deviceId = -1, cell = SurfaceCell.None,
                };
            }
            return hit.kind == HitKind.Player;
        }

        Track TrackOf(int playerId, bool create)
        {
            foreach (var t in tracks) if (t.playerId == playerId) return t;
            if (!create) return null;
            var made = new Track { playerId = playerId, ring = new Sample[capacity], head = capacity - 1 };
            tracks.Add(made);
            return made;
        }
    }

    /// <summary>
    /// The world the combat rules see: unbreakable geometry from the map (the Game layer's physics,
    /// or a fake in tests) plus the hitboxes of players and units from the <see cref="HitHistory"/>. Implements the
    /// contract's lag-compensated Raycast and PlayersInSphere, and WP7's optional position and line of
    /// sight queries, so abilities hit the same bodies weapons do.
    /// </summary>
    public sealed class CombatWorld : IWorldQuery, IPositionQuery, ILineOfSightQuery, ICasterQuery
    {
        readonly IWorldQuery geometry;
        readonly Ruleset rules;

        public HitHistory History { get; }

        /// <summary>
        /// Barriers and gates placed by abilities (set by <see cref="CombatServer"/>): solid ones stop
        /// rays and block line of sight like walls, and report a Device hit with the object's id.
        /// </summary>
        public IReadOnlyList<ObjectState> Objects;
        /// <summary>Whether a shooter's ray passes through an object (their own team's, with friendly fire off).</summary>
        public Func<int, ObjectState, bool> PassesObject;

        /// <param name="geometry">Answers Raycast for map geometry (players are ignored) and PlayersInZone.</param>
        public CombatWorld(IWorldQuery geometry, HitHistory history, Ruleset rules)
        {
            this.geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
            History = history ?? throw new ArgumentNullException(nameof(history));
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }

        /// <summary>
        /// A ray against the map and every player's hitboxes as they were at <paramref name="tick"/>,
        /// skipping the shooter (and anyone <paramref name="passThrough"/> names). The nearest thing
        /// wins; geometry wins a tie, so cover is cover.
        /// </summary>
        public bool RaycastFrom(int shooterId, Vec3 origin, Vec3 dir, float max, int tick, out HitInfo hit, Func<int, bool> passThrough = null)
        {
            dir = dir.Normalized;
            bool wall = geometry.Raycast(origin, dir, max, tick, out var world) && world.kind != HitKind.Player;
            Func<ObjectState, bool> skip = null;
            if (PassesObject != null && shooterId >= 0) skip = o => PassesObject(shooterId, o);
            if (SolidObjects.Raycast(Objects, origin, dir, wall ? world.distance : max, skip, out var solid))
            {
                world = solid;
                wall = true;
            }
            float limit = wall ? world.distance : max;
            if (History.Raycast(origin, dir, limit, tick, shooterId, out var body, passThrough) && (!wall || body.distance < world.distance))
            {
                hit = body;
                return true;
            }
            hit = wall ? world : HitInfo.Miss;
            return wall;
        }

        public bool Raycast(Vec3 origin, Vec3 dir, float max, int tick, out HitInfo hit) => RaycastFrom(-1, origin, dir, max, tick, out hit);

        /// <summary>An ability's ray, judged like the caster's shots.</summary>
        public bool RaycastFor(int casterId, Vec3 origin, Vec3 dir, float max, int tick, out HitInfo hit) => RaycastFrom(casterId, origin, dir, max, tick, out hit);

        public int[] PlayersInZone(ZoneId zone) => geometry.PlayersInZone(zone);

        /// <summary>
        /// Bodies whose chest is within the radius at the tick, in id order: players and units alike
        /// (units ride the hit history under their unit ids), so area abilities and splash reach both.
        /// </summary>
        public int[] PlayersInSphere(Vec3 center, float radius, int tick)
        {
            var ids = new List<int>();
            foreach (int id in History.Players())
                if (History.PoseAt(id, tick, out var pose) && pose.alive && Vec3.Distance(Hitboxes.Chest(rules, pose), center) <= radius)
                    ids.Add(id);
            return ids.ToArray();
        }

        public SurfaceCell CellAt(Vec3 point) => geometry.CellAt(point);

        public bool TryGetPosition(int playerId, int tick, out Vec3 position)
        {
            position = Vec3.Zero;
            if (!History.PoseAt(playerId, tick, out var pose)) return false;
            position = pose.position;
            return true;
        }

        /// <summary>Nothing of the map between the point and the player's chest.</summary>
        public bool HasLineOfSight(Vec3 from, int playerId, int tick) => HasLineOfSightFor(-1, from, playerId, tick);

        /// <summary>Line of sight for something a player owns (a sentry): their own team's barriers don't block it.</summary>
        public bool HasLineOfSightFor(int viewerId, Vec3 from, int playerId, int tick)
        {
            if (!History.PoseAt(playerId, tick, out var pose)) return false;
            Vec3 to = Hitboxes.Chest(rules, pose);
            Vec3 d = to - from;
            float len = d.Magnitude;
            if (len < 1e-4f) return true;
            Func<ObjectState, bool> skip = null;
            if (PassesObject != null && viewerId >= 0) skip = o => PassesObject(viewerId, o);
            if (SolidObjects.Raycast(Objects, from, d / len, len - 1e-3f, skip, out _, true)) return false;
            return !(geometry.Raycast(from, d / len, len, tick, out var hit) && hit.kind != HitKind.Player && hit.distance < len - 1e-3f);
        }
    }
}
