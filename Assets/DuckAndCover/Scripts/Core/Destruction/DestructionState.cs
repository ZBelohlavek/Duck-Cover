using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>The live grid of one surface: a state and hit points per cell, index = y * width + x.</summary>
    public class SurfaceGrid
    {
        public readonly SurfaceDef def;
        public readonly CellState[] state;
        public readonly byte[] hp;

        public int Id => def.id;
        public int Width => def.width;
        public int Height => def.height;
        public int CellCount => state.Length;
        public SurfaceMaterial Material => def.material;
        /// <summary>Counts toward its zone's integrity (soft and hard surfaces with a zone).</summary>
        public bool CountsForIntegrity => def.zoneId > 0 && (def.material == SurfaceMaterial.Soft || def.material == SurfaceMaterial.Hard);
        /// <summary>Small damage is capped here. Boards on doors and windows break freely.</summary>
        public bool PeekCapped => def.material == SurfaceMaterial.Soft || def.material == SurfaceMaterial.Hard;

        public SurfaceGrid(SurfaceDef def, DestructionRuleset r)
        {
            this.def = def;
            state = new CellState[def.width * def.height];
            hp = new byte[state.Length];
            Reset(r);
        }

        public void Reset(DestructionRuleset r)
        {
            byte full = (byte)r.MaxHp(def.material);
            for (int i = 0; i < state.Length; i++)
            {
                state[i] = def.startOpen ? CellState.Open : CellState.Intact;
                hp[i] = def.startOpen ? (byte)0 : full;
            }
        }

        public int Index(int x, int y) => y * def.width + x;
        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < def.width && y < def.height;
        public CellState this[int x, int y] => state[Index(x, y)];
        public SurfaceCell CellAt(int index) => new SurfaceCell(def.id, index % def.width, index / def.width);

        public int Count(CellState s)
        {
            int n = 0;
            foreach (var c in state) if (c == s) n++;
            return n;
        }

        /// <summary>The material a cell behaves as: patches are boards, reinforcement is hard.</summary>
        public SurfaceMaterial EffectiveMaterial(int index)
        {
            switch (state[index])
            {
                case CellState.Patched: return SurfaceMaterial.Barricade;
                case CellState.Reinforced: return SurfaceMaterial.Hard;
                default: return def.material;
            }
        }
    }

    /// <summary>Match-long state of one structure zone.</summary>
    public class ZoneState
    {
        public readonly ZoneDef def;
        public readonly float floor;
        public readonly List<SurfaceGrid> surfaces = new List<SurfaceGrid>();
        /// <summary>Breakable cells in the zone (soft and hard surfaces only).</summary>
        public int totalCells;
        public bool locked;

        public int Id => def.id;

        public ZoneState(ZoneDef def, DestructionRuleset r)
        {
            this.def = def;
            floor = def.floorOverride > 0f ? def.floorOverride : def.nearSite ? r.siteZoneFloor : r.zoneFloor;
        }

        /// <summary>Cells lost so far: an open cell counts 1, a patched cell counts (1 - refund).</summary>
        public float Lost(DestructionRuleset r)
        {
            int open = 0, patched = 0;
            foreach (var s in surfaces)
            {
                if (!s.CountsForIntegrity) continue;
                foreach (var c in s.state)
                {
                    if (c == CellState.Open) open++;
                    else if (c == CellState.Patched) patched++;
                }
            }
            return open + patched * (1f - r.patchIntegrityRefund);
        }

        /// <summary>Share of the zone still standing, 0..1. 1 for a zone with nothing breakable.</summary>
        public float Integrity(DestructionRuleset r) => totalCells == 0 ? 1f : 1f - Lost(r) / totalCells;

        /// <summary>The most cells the zone can lose before it reaches its floor.</summary>
        public float MaxLost => totalCells * (1f - floor);
    }

    /// <summary>
    /// Every breakable surface and zone on the map, for the whole match. Created once from the map;
    /// rounds change only the budgets (Breach), never this state. Server-owned: clients mirror it with
    /// <see cref="DestructionRules.ApplyChanges"/> and <see cref="ApplySnapshot"/>.
    /// </summary>
    public class DestructionState
    {
        public readonly DestructionRuleset rules;
        public readonly List<SurfaceGrid> surfaces = new List<SurfaceGrid>();
        public readonly List<ZoneState> zones = new List<ZoneState>();
        readonly Dictionary<int, SurfaceGrid> surfaceById = new Dictionary<int, SurfaceGrid>();
        readonly Dictionary<int, ZoneState> zoneById = new Dictionary<int, ZoneState>();

        public DestructionState(MapDestructionDef map, DestructionRuleset rules)
        {
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            if (map == null) throw new ArgumentNullException(nameof(map));
            foreach (var z in map.zones)
            {
                if (z.id <= 0 || zoneById.ContainsKey(z.id)) throw new ArgumentException("Zone ids must be unique and start at 1: " + z.id);
                var zs = new ZoneState(z, rules);
                zones.Add(zs);
                zoneById[z.id] = zs;
            }
            foreach (var d in map.surfaces)
            {
                if (d.id <= 0 || surfaceById.ContainsKey(d.id)) throw new ArgumentException("Surface ids must be unique and start at 1: " + d.id);
                if (d.width <= 0 || d.height <= 0) throw new ArgumentException("Surface " + d.id + " has no cells");
                var g = new SurfaceGrid(d, rules);
                surfaces.Add(g);
                surfaceById[d.id] = g;
                if (d.zoneId > 0)
                {
                    if (!zoneById.TryGetValue(d.zoneId, out var zone)) throw new ArgumentException("Surface " + d.id + " names missing zone " + d.zoneId);
                    zone.surfaces.Add(g);
                    if (g.CountsForIntegrity) zone.totalCells += g.CellCount;
                }
                else if (d.material == SurfaceMaterial.Soft || d.material == SurfaceMaterial.Hard)
                    throw new ArgumentException("Soft and hard surfaces need a zone: surface " + d.id);
            }
        }

        public SurfaceGrid Surface(int id) => surfaceById.TryGetValue(id, out var s) ? s : null;
        public ZoneState Zone(int id) => zoneById.TryGetValue(id, out var z) ? z : null;
        public ZoneState ZoneOf(SurfaceGrid s) => s == null ? null : Zone(s.def.zoneId);

        public CellState StateAt(SurfaceCell c)
        {
            var s = Surface(c.surfaceId);
            return s != null && s.InBounds(c.x, c.y) ? s[c.x, c.y] : CellState.Intact;
        }

        // ---- Snapshot and checksum ----

        /// <summary>A full copy for round start, late joiners and drift repair.</summary>
        public DestructionSnapshot ToSnapshot()
        {
            var snap = new DestructionSnapshot { surfaces = new SurfaceSnapshot[surfaces.Count] };
            for (int i = 0; i < surfaces.Count; i++)
            {
                var s = surfaces[i];
                var states = new byte[s.CellCount];
                for (int c = 0; c < states.Length; c++) states[c] = (byte)s.state[c];
                snap.surfaces[i] = new SurfaceSnapshot { id = s.Id, state = states, hp = (byte[])s.hp.Clone() };
            }
            var locked = new List<int>();
            foreach (var z in zones) if (z.locked) locked.Add(z.Id);
            snap.lockedZones = locked.ToArray();
            snap.checksum = Checksum();
            return snap;
        }

        /// <summary>Replaces this state with a snapshot taken from the same map. Returns false (and
        /// changes nothing) if the snapshot doesn't fit the map or its checksum doesn't match its data.</summary>
        public bool ApplySnapshot(DestructionSnapshot snap)
        {
            if (snap == null || snap.surfaces == null || snap.surfaces.Length != surfaces.Count) return false;
            foreach (var ss in snap.surfaces)
            {
                var s = Surface(ss.id);
                if (s == null || ss.state == null || ss.hp == null || ss.state.Length != s.CellCount || ss.hp.Length != s.CellCount) return false;
            }
            foreach (var ss in snap.surfaces)
            {
                var s = Surface(ss.id);
                for (int c = 0; c < s.CellCount; c++) s.state[c] = (CellState)ss.state[c];
                Buffer.BlockCopy(ss.hp, 0, s.hp, 0, ss.hp.Length);
            }
            foreach (var z in zones) z.locked = false;
            if (snap.lockedZones != null) foreach (var id in snap.lockedZones) { var z = Zone(id); if (z != null) z.locked = true; }
            return Checksum() == snap.checksum;
        }

        /// <summary>FNV-1a over every surface (in map order) and zone lock. Two machines with the same
        /// destruction always agree; any differing cell changes it.</summary>
        public uint Checksum()
        {
            uint h = 2166136261;
            void Mix(int v)
            {
                for (int b = 0; b < 4; b++) { h ^= (byte)(v >> (b * 8)); h *= 16777619; }
            }
            foreach (var s in surfaces)
            {
                Mix(s.Id);
                for (int c = 0; c < s.CellCount; c++) { h ^= (byte)s.state[c]; h *= 16777619; h ^= s.hp[c]; h *= 16777619; }
            }
            foreach (var z in zones) { Mix(z.Id); Mix(z.locked ? 1 : 0); }
            return h;
        }
    }

    /// <summary>Serializable copy of the destruction state (JsonUtility friendly: arrays, public fields).</summary>
    [Serializable]
    public class DestructionSnapshot
    {
        public SurfaceSnapshot[] surfaces;
        public int[] lockedZones;
        public uint checksum;
    }

    [Serializable]
    public class SurfaceSnapshot
    {
        public int id;
        /// <summary><see cref="CellState"/> per cell, as bytes.</summary>
        public byte[] state;
        public byte[] hp;
    }
}
