using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>What one damage, patch or reinforcement did.</summary>
    public class DestructionResult
    {
        /// <summary>Every cell that changed, once each, in the order the rules touched them.</summary>
        public readonly List<CellChange> changes = new List<CellChange>();
        public int opened;
        public int cracked;
        /// <summary>Cells that took damage (hit points fell) without changing state.</summary>
        public int damaged;
        /// <summary>Cells small damage would have opened but the bullet cap kept closed.</summary>
        public int blockedByPeekCap;
        /// <summary>Cells that would have opened but the zone was at its floor.</summary>
        public int blockedByFloor;
        public int patched;
        public int reinforced;
        /// <summary>Zones this action locked.</summary>
        public readonly List<int> lockedZones = new List<int>();

        public bool AnyChange => changes.Count > 0 || damaged > 0;

        readonly Dictionary<SurfaceCell, int> at = new Dictionary<SurfaceCell, int>();

        internal void Record(SurfaceGrid s, int index)
        {
            var c = new CellChange { cell = s.CellAt(index), state = s.state[index], hp = s.hp[index] };
            if (at.TryGetValue(c.cell, out int i)) changes[i] = c;
            else { at[c.cell] = changes.Count; changes.Add(c); }
        }
    }

    /// <summary>
    /// Destruction rules: damage shapes on surface grids, the bullet cap, zone floors and locking,
    /// patches and reinforcement. Server only, except <see cref="ApplyChanges"/> which clients use to
    /// mirror the server. Budgets are not checked here; charges, patches and reinforcement go through
    /// <see cref="BreachRules"/>, which spends the budget and then calls these.
    /// </summary>
    public static class DestructionRules
    {
        // Scratch lists; Core runs on one thread (the server's tick).
        static readonly List<int> shapeCells = new List<int>();
        static readonly Queue<int> queue = new Queue<int>();
        static readonly HashSet<int> seen = new HashSet<int>();

        /// <summary>
        /// Applies one piece of damage and returns the changed cells (WP5 draws them, WP3 sends them).
        /// Publishes <see cref="CellsChangedEvent"/>, and zone events when integrity moves, if
        /// <paramref name="events"/> is given.
        /// </summary>
        public static DestructionResult ApplyDamage(DestructionState d, DamageRequest req, IGameEvents events = null)
        {
            var result = new DestructionResult();
            var s = d.Surface(req.surfaceId);
            if (s == null || s.Material == SurfaceMaterial.Skeleton) return result;
            var r = d.rules;
            var zone = d.ZoneOf(s);
            bool small = req.source.IsSmall();
            bool hitFloor = false;

            req.shape.Cells(s.Width, s.Height, r.cellSize, shapeCells);
            foreach (int i in shapeCells)
            {
                var st = s.state[i];
                if (st == CellState.Open || st == CellState.Locked) continue;
                int dmg = r.DamageTo(req.source, s.EffectiveMaterial(i));
                if (dmg <= 0) continue;

                int hp = s.hp[i] - dmg;
                if (hp <= 0)
                {
                    if (small && s.PeekCapped && !PeekAllows(s, i, r)) { result.blockedByPeekCap++; hp = 1; }
                    else if (s.CountsForIntegrity && zone != null && !FloorAllows(zone, st, r)) { result.blockedByFloor++; hitFloor = true; hp = 1; }
                }

                if (hp <= 0)
                {
                    s.state[i] = CellState.Open;
                    s.hp[i] = 0;
                    result.opened++;
                    result.Record(s, i);
                    continue;
                }

                bool wasDamaged = s.hp[i] != hp;
                s.hp[i] = (byte)hp;
                if (st == CellState.Intact && hp <= r.MaxHp(s.Material) * r.crackAtFraction)
                {
                    s.state[i] = CellState.Cracked;
                    result.cracked++;
                    result.Record(s, i);
                }
                else if (wasDamaged)
                {
                    result.damaged++;
                    result.Record(s, i);
                }
            }

            if (zone != null && s.CountsForIntegrity && result.opened > 0) CheckZone(d, zone, hitFloor, req.tick, result, events);
            else if (zone != null && hitFloor) CheckZone(d, zone, true, req.tick, result, events);

            Publish(events, result, (CellChangeCause)req.source, req.sourceTag, req.team, req.playerId, req.tick);
            return result;
        }

        /// <summary>What <see cref="ApplyDamage"/> would do, without changing anything. Used to refuse a
        /// charge that would be wasted (a standard charge on a hard wall, a zone at its floor).</summary>
        public static DestructionResult Preview(DestructionState d, DamageRequest req) =>
            WithRollback(d, req.surfaceId, () => ApplyDamage(d, req));

        /// <summary>
        /// Boards up the open hole that contains cell (x, y): up to <see cref="DestructionRuleset.patchMaxCells"/>
        /// connected open cells become patched. In a locked zone the boards lock too. Budget is spent by
        /// <see cref="BreachRules"/>.
        /// </summary>
        public static DestructionResult Patch(DestructionState d, int surfaceId, int x, int y, int tick = 0,
            IGameEvents events = null, TeamId team = TeamId.None, int playerId = -1, string sourceTag = null)
        {
            var result = new DestructionResult();
            var s = d.Surface(surfaceId);
            if (s == null || s.Material == SurfaceMaterial.Skeleton || !s.InBounds(x, y)) return result;
            int start = s.Index(x, y);
            if (s.state[start] != CellState.Open) return result;
            var r = d.rules;
            var zone = d.ZoneOf(s);
            bool lockBoards = zone != null && zone.locked;

            queue.Clear();
            seen.Clear();
            queue.Enqueue(start);
            seen.Add(start);
            while (queue.Count > 0 && result.patched < r.patchMaxCells)
            {
                int i = queue.Dequeue();
                s.state[i] = lockBoards ? CellState.Locked : CellState.Patched;
                s.hp[i] = (byte)r.barricadeHp;
                result.patched++;
                result.Record(s, i);
                ForNeighbours(s, i, n =>
                {
                    if (s.state[n] == CellState.Open && seen.Add(n)) queue.Enqueue(n);
                });
            }

            if (zone != null && s.CountsForIntegrity) PublishIntegrity(d, zone, tick, events);
            Publish(events, result, CellChangeCause.Patch, sourceTag, team, playerId, tick);
            return result;
        }

        /// <summary>
        /// Turns a full-height strip of a soft surface, <see cref="DestructionRuleset.reinforceWidth"/>
        /// wide and centred on <paramref name="u"/> (metres from the left edge), hard for the rest of the
        /// match. Only intact and cracked cells change. Not allowed in a locked zone.
        /// </summary>
        public static DestructionResult Reinforce(DestructionState d, int surfaceId, float u, int tick = 0,
            IGameEvents events = null, TeamId team = TeamId.None, int playerId = -1, string sourceTag = null)
        {
            var result = new DestructionResult();
            var s = d.Surface(surfaceId);
            if (s == null || s.Material != SurfaceMaterial.Soft) return result;
            var zone = d.ZoneOf(s);
            if (zone != null && zone.locked) return result;
            var r = d.rules;
            var strip = DamageShape.Rect(u, s.Height * r.cellSize / 2f, r.reinforceWidth, s.Height * r.cellSize);
            strip.Cells(s.Width, s.Height, r.cellSize, shapeCells);
            foreach (int i in shapeCells)
            {
                if (s.state[i] != CellState.Intact && s.state[i] != CellState.Cracked) continue;
                s.state[i] = CellState.Reinforced;
                s.hp[i] = (byte)r.hardHp;
                result.reinforced++;
                result.Record(s, i);
            }
            Publish(events, result, CellChangeCause.Reinforce, sourceTag, team, playerId, tick);
            return result;
        }

        /// <summary>Preview of <see cref="Patch"/> or <see cref="Reinforce"/>, without changing anything.</summary>
        public static DestructionResult PreviewPatch(DestructionState d, int surfaceId, int x, int y) =>
            WithRollback(d, surfaceId, () => Patch(d, surfaceId, x, y));

        public static DestructionResult PreviewReinforce(DestructionState d, int surfaceId, float u) =>
            WithRollback(d, surfaceId, () => Reinforce(d, surfaceId, u));

        /// <summary>Client side: copy cells the server changed into this machine's state.</summary>
        public static void ApplyChanges(DestructionState d, IEnumerable<CellChange> changes)
        {
            foreach (var c in changes)
            {
                var s = d.Surface(c.cell.surfaceId);
                if (s == null || !s.InBounds(c.cell.x, c.cell.y)) continue;
                int i = s.Index(c.cell.x, c.cell.y);
                s.state[i] = c.state;
                s.hp[i] = c.hp;
            }
        }

        /// <summary>Client side: mirror a <see cref="ZoneLockedEvent"/>.</summary>
        public static void ApplyZoneLock(DestructionState d, int zoneId)
        {
            var z = d.Zone(zoneId);
            if (z != null) z.locked = true;
        }

        /// <summary>Locks a zone now: every cell of its surfaces that isn't open becomes locked.</summary>
        public static DestructionResult LockZone(DestructionState d, int zoneId, int tick = 0, IGameEvents events = null)
        {
            var result = new DestructionResult();
            var z = d.Zone(zoneId);
            if (z == null || z.locked) return result;
            Lock(z, result);
            events?.Publish(new ZoneLockedEvent { tick = tick, zoneId = z.Id });
            Publish(events, result, CellChangeCause.ZoneLock, null, TeamId.None, -1, tick);
            return result;
        }

        // ---- Holes ----

        /// <summary>
        /// The connected openings on a surface (4-neighbour, open cells only; patched cells count as
        /// closed). A hole is passable when it holds an open rectangle a player fits through:
        /// passableWidth by passableHeight on a wall, a passableWidth square on a floor.
        /// </summary>
        public static List<Hole> FindHoles(DestructionState d, int surfaceId)
        {
            var holes = new List<Hole>();
            var s = d.Surface(surfaceId);
            if (s == null) return holes;
            var r = d.rules;
            int pw = r.Cells(r.passableWidth);
            int ph = s.def.kind == SurfaceKind.Floor ? pw : r.Cells(r.passableHeight);

            // Prefix sums of open cells, for "is this rectangle fully open".
            int W = s.Width, H = s.Height;
            var sum = new int[(W + 1) * (H + 1)];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    sum[(y + 1) * (W + 1) + x + 1] = (s.state[s.Index(x, y)] == CellState.Open ? 1 : 0)
                        + sum[y * (W + 1) + x + 1] + sum[(y + 1) * (W + 1) + x] - sum[y * (W + 1) + x];
            bool FullyOpen(int x0, int y0, int w, int h) =>
                sum[(y0 + h) * (W + 1) + x0 + w] - sum[y0 * (W + 1) + x0 + w] - sum[(y0 + h) * (W + 1) + x0] + sum[y0 * (W + 1) + x0] == w * h;

            var visited = new bool[s.CellCount];
            for (int start = 0; start < s.CellCount; start++)
            {
                if (visited[start] || s.state[start] != CellState.Open) continue;
                var hole = new Hole { surfaceId = s.Id, minX = W, minY = H, maxX = -1, maxY = -1 };
                queue.Clear();
                queue.Enqueue(start);
                visited[start] = true;
                while (queue.Count > 0)
                {
                    int i = queue.Dequeue();
                    int x = i % W, y = i / W;
                    hole.cells++;
                    hole.minX = Math.Min(hole.minX, x); hole.maxX = Math.Max(hole.maxX, x);
                    hole.minY = Math.Min(hole.minY, y); hole.maxY = Math.Max(hole.maxY, y);
                    ForNeighbours(s, i, n =>
                    {
                        if (!visited[n] && s.state[n] == CellState.Open) { visited[n] = true; queue.Enqueue(n); }
                    });
                }
                // A fully open rectangle is connected, so it lies inside one hole's bounds.
                for (int y0 = hole.minY; y0 + ph - 1 <= hole.maxY && !hole.passable; y0++)
                    for (int x0 = hole.minX; x0 + pw - 1 <= hole.maxX && !hole.passable; x0++)
                        if (FullyOpen(x0, y0, pw, ph)) hole.passable = true;
                holes.Add(hole);
            }
            return holes;
        }

        // ---- Internals ----

        /// <summary>The bullet cap: small damage may open a cell only while the surface stays under the
        /// peek share and the hole it joins stays peek-sized.</summary>
        static bool PeekAllows(SurfaceGrid s, int index, DestructionRuleset r)
        {
            int cap = (int)Math.Floor(r.peekCapFraction * s.CellCount);
            if (s.Count(CellState.Open) + 1 > cap) return false;

            // Size of the hole this cell would join, stopping as soon as it is too big.
            queue.Clear();
            seen.Clear();
            seen.Add(index);
            queue.Enqueue(index);
            int size = 1;
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                bool tooBig = false;
                ForNeighbours(s, i, n =>
                {
                    if (tooBig || s.state[n] != CellState.Open || !seen.Add(n)) return;
                    size++;
                    if (size > r.peekMaxHoleCells) tooBig = true;
                    else queue.Enqueue(n);
                });
                if (tooBig) return false;
            }
            return size <= r.peekMaxHoleCells;
        }

        /// <summary>The zone floor: a cell may open only if the zone stays at or above its floor.</summary>
        static bool FloorAllows(ZoneState z, CellState current, DestructionRuleset r)
        {
            if (z.locked) return false;
            float extra = current == CellState.Patched ? r.patchIntegrityRefund : 1f;
            return z.Lost(r) + extra <= z.MaxLost + 1e-4f;
        }

        static void CheckZone(DestructionState d, ZoneState z, bool hitFloor, int tick, DestructionResult result, IGameEvents events)
        {
            if (z.locked) return;
            var r = d.rules;
            float integrity = z.Integrity(r);
            if (hitFloor || integrity <= z.floor + 1e-4f)
            {
                Lock(z, result);
                result.lockedZones.Add(z.Id);
                events?.Publish(new ZoneLockedEvent { tick = tick, zoneId = z.Id });
            }
            PublishIntegrity(d, z, tick, events);
        }

        static void PublishIntegrity(DestructionState d, ZoneState z, int tick, IGameEvents events)
        {
            if (events == null) return;
            float integrity = z.Integrity(d.rules);
            events.Publish(new ZoneIntegrityEvent
            {
                tick = tick, zoneId = z.Id, integrity = integrity, floor = z.floor,
                warning = integrity <= z.floor + d.rules.integrityWarnMargin,
            });
        }

        static void Lock(ZoneState z, DestructionResult result)
        {
            z.locked = true;
            foreach (var s in z.surfaces)
            {
                if (s.Material == SurfaceMaterial.Skeleton) continue;
                for (int i = 0; i < s.CellCount; i++)
                {
                    if (s.state[i] == CellState.Open || s.state[i] == CellState.Locked) continue;
                    s.state[i] = CellState.Locked;
                    result.Record(s, i);
                }
            }
        }

        static void ForNeighbours(SurfaceGrid s, int i, Action<int> visit)
        {
            int x = i % s.Width, y = i / s.Width;
            if (x > 0) visit(i - 1);
            if (x < s.Width - 1) visit(i + 1);
            if (y > 0) visit(i - s.Width);
            if (y < s.Height - 1) visit(i + s.Width);
        }

        static void Publish(IGameEvents events, DestructionResult result, CellChangeCause cause, string tag, TeamId team, int playerId, int tick)
        {
            if (events == null || result.changes.Count == 0) return;
            events.Publish(new CellsChangedEvent
            {
                tick = tick, cause = cause, sourceTag = tag, team = team, playerId = playerId,
                changes = new List<CellChange>(result.changes),
            });
        }

        /// <summary>Runs an action, then puts back every surface of the target's zone (or the surface
        /// alone) and the zone lock, so the action only reports what it would do.</summary>
        static DestructionResult WithRollback(DestructionState d, int surfaceId, Func<DestructionResult> action)
        {
            var s = d.Surface(surfaceId);
            if (s == null) return new DestructionResult();
            var zone = d.ZoneOf(s);
            var touched = zone != null ? new List<SurfaceGrid>(zone.surfaces) : new List<SurfaceGrid> { s };
            if (!touched.Contains(s)) touched.Add(s);
            var states = new List<CellState[]>();
            var hps = new List<byte[]>();
            foreach (var t in touched) { states.Add((CellState[])t.state.Clone()); hps.Add((byte[])t.hp.Clone()); }
            bool wasLocked = zone != null && zone.locked;
            try { return action(); }
            finally
            {
                for (int k = 0; k < touched.Count; k++)
                {
                    Array.Copy(states[k], touched[k].state, states[k].Length);
                    Array.Copy(hps[k], touched[k].hp, hps[k].Length);
                }
                if (zone != null) zone.locked = wasLocked;
            }
        }
    }

    /// <summary>One connected opening on a surface, from <see cref="DestructionRules.FindHoles"/>.</summary>
    public struct Hole
    {
        public int surfaceId;
        public int cells;
        public int minX, minY, maxX, maxY;
        /// <summary>A player fits through (it holds a full passable rectangle). Otherwise it is a peek hole.</summary>
        public bool passable;
    }

    /// <summary>
    /// The map evolution hook from the plan: runs between rounds, empty in the base game. A map can
    /// script changes here later (a shutter opening in round 4, a floor giving way once its zone locks).
    /// </summary>
    public interface IMapEvolution
    {
        void BetweenRounds(DestructionState d, int nextRound, IGameEvents events);
    }
}
