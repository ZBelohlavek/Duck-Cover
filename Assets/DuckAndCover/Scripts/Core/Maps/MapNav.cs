using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// A walking grid over a map, for the validator: each cell of the floor plan gets the height of
    /// the highest surface on it (floor, platform, ramp, or the top of a wall), and a walker may step
    /// up a little, vault up to the ruleset's vault height (paying the vault time), drop down any
    /// height, and never climb more. Travel times are in seconds at walking speed. It is a 2.5D
    /// model: walkable floors start at ground level, and blocks whose bottom is above a standing
    /// body (lintels, the top crate of a stack) are walked under.
    /// </summary>
    public sealed class MapNav
    {
        /// <summary>Cell size in metres.</summary>
        public readonly float cell;
        public readonly int width, depth;
        readonly MapDef map;
        readonly float[] height;
        readonly float walkSpeed, stepHeight, vaultHeight, vaultSeconds, overhead;

        public const float Unreachable = float.PositiveInfinity;

        /// <param name="shifted">Walk the map as it is after the map shift.</param>
        /// <param name="stepHeight">Highest rise walked without a vault (the controller's step).</param>
        public MapNav(MapDef map, Ruleset rules, bool shifted, float cell = 0.5f, float stepHeight = 0.35f)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            this.cell = cell;
            walkSpeed = rules.walkSpeed;
            vaultHeight = rules.vaultMaxHeight;
            vaultSeconds = rules.vaultSeconds;
            this.stepHeight = stepHeight;
            overhead = rules.standHeight;
            width = (int)Math.Round(map.halfX * 2f / cell);
            depth = (int)Math.Round(map.halfZ * 2f / cell);
            height = new float[width * depth];
            Fill(shifted);
        }

        public float X(int i) => -map.halfX + (i + 0.5f) * cell;
        public float Z(int j) => -map.halfZ + (j + 0.5f) * cell;
        public int I(float x) => Clamp((int)Math.Floor((x + map.halfX) / cell), width);
        public int J(float z) => Clamp((int)Math.Floor((z + map.halfZ) / cell), depth);
        public float Height(int i, int j) => height[j * width + i];

        static int Clamp(int v, int n) => v < 0 ? 0 : v >= n ? n - 1 : v;

        void Fill(bool shifted)
        {
            for (int k = 0; k < height.Length; k++) height[k] = float.NegativeInfinity;
            const float eps = 0.01f;
            foreach (var b in map.blocks)
            {
                if (!b.Solid || !b.Present(shifted)) continue;
                // Lintels and stacked crates start overhead: a body walks under them (or they sit on
                // something that already counts).
                if (b.Min.y >= overhead) continue;
                Vec3 lo = b.Min, hi = b.Max;
                // Every cell the footprint overlaps, so thin walls between cell centres still count.
                int i0 = I(lo.x + eps), i1 = I(hi.x - eps), j0 = J(lo.z + eps), j1 = J(hi.z - eps);
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                    {
                        float top = b.TopAt(Math.Min(Math.Max(X(i), lo.x), hi.x), Math.Min(Math.Max(Z(j), lo.z), hi.z));
                        int k = j * width + i;
                        if (top > height[k]) height[k] = top;
                    }
            }
        }

        /// <summary>Seconds to walk from a cell to a neighbour, or infinity if it can't be done.</summary>
        float StepCost(int fromK, int toK, float metres)
        {
            float a = height[fromK], b = height[toK];
            if (float.IsNegativeInfinity(a) || float.IsNegativeInfinity(b)) return Unreachable; // no floor
            float rise = b - a;
            float t = metres / walkSpeed;
            if (rise <= stepHeight) return t;
            if (rise <= vaultHeight) return t + vaultSeconds;
            return Unreachable;
        }

        static readonly int[] Di = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] Dj = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>Travel times from one start: seconds to every cell (infinity if unreachable) and the cell each was reached from.</summary>
        public sealed class Field
        {
            public float[] seconds;
            public int[] from;
        }

        /// <summary>Seconds from a point to every cell (Dijkstra).</summary>
        public Field TravelFrom(Vec3 start)
        {
            var dist = new float[height.Length];
            var prev = new int[height.Length];
            for (int k = 0; k < dist.Length; k++) { dist[k] = Unreachable; prev[k] = -1; }
            int s = J(start.z) * width + I(start.x);
            dist[s] = 0f;
            var open = new SortedSet<(float, int)>();
            open.Add((0f, s));
            float diag = cell * 1.41421356f;
            while (open.Count > 0)
            {
                var (d, k) = open.Min;
                open.Remove(open.Min);
                if (d > dist[k]) continue;
                int i = k % width, j = k / width;
                for (int n = 0; n < 8; n++)
                {
                    int ni = i + Di[n], nj = j + Dj[n];
                    if (ni < 0 || nj < 0 || ni >= width || nj >= depth) continue;
                    int nk = nj * width + ni;
                    float c;
                    if (n < 4) c = StepCost(k, nk, cell);
                    else
                    {
                        // No cutting a corner: both straight neighbours must be walkable too.
                        int ak = j * width + ni, bk = nj * width + i;
                        if (float.IsInfinity(StepCost(k, ak, cell)) || float.IsInfinity(StepCost(k, bk, cell))) continue;
                        c = StepCost(k, nk, diag);
                    }
                    if (float.IsInfinity(c)) continue;
                    float nd = d + c;
                    if (nd < dist[nk])
                    {
                        if (!float.IsInfinity(dist[nk])) open.Remove((dist[nk], nk));
                        dist[nk] = nd;
                        prev[nk] = k;
                        open.Add((nd, nk));
                    }
                }
            }
            return new Field { seconds = dist, from = prev };
        }

        /// <summary>The cell inside the zone (at the zone's floor height) reached soonest, or -1.</summary>
        int NearestInZone(Field field, MapZone zone)
        {
            int bestK = -1;
            float best = Unreachable;
            for (int j = J(zone.center.z - zone.radius); j <= J(zone.center.z + zone.radius); j++)
                for (int i = I(zone.center.x - zone.radius); i <= I(zone.center.x + zone.radius); i++)
                {
                    int k = j * width + i;
                    float h = height[k];
                    if (float.IsNegativeInfinity(h) || !zone.Contains(new Vec3(X(i), h, Z(j)))) continue;
                    if (field.seconds[k] < best) { best = field.seconds[k]; bestK = k; }
                }
            return bestK;
        }

        /// <summary>The quickest time onto the zone from the field's start.</summary>
        public float TimeToZone(Field field, MapZone zone)
        {
            int k = NearestInZone(field, zone);
            return k < 0 ? Unreachable : field.seconds[k];
        }

        /// <summary>The cells of the quickest path from the field's start onto the zone, start first.</summary>
        public List<(int i, int j)> PathToZone(Field field, MapZone zone)
        {
            var path = new List<(int, int)>();
            for (int k = NearestInZone(field, zone); k >= 0; k = field.from[k]) path.Add((k % width, k / width));
            path.Reverse();
            return path;
        }
    }
}
