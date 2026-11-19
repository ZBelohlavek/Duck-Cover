using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// Where a lane map's jungle is, for <see cref="StatConditions.Jungle"/>: on one of its heroes-only
    /// routes (MapDef.routes, within half their width plus Ruleset.jungleTrailMargin) or in a camp's
    /// clearing (within Ruleset.jungleClearingRadius of its pad), and never on the lane itself (within half
    /// MapDef.laneWidth of its centre line). Bases, plazas and the lane are not jungle. Distances are flat.
    /// </summary>
    public sealed class JungleArea
    {
        readonly List<LaneDef> lanes = new List<LaneDef>();
        readonly List<MapRoute> routes = new List<MapRoute>();
        readonly List<Vec3> pads = new List<Vec3>();
        readonly float laneHalf, trailMargin, clearing;

        public JungleArea(MapDef map, Ruleset rules)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            rules = rules ?? new Ruleset();
            foreach (var l in map.lanes) if (l != null && l.points.Count >= 2) lanes.Add(l);
            foreach (var r in map.routes) if (r != null && r.points.Count >= 2) routes.Add(r);
            foreach (var c in map.camps) if (c != null) pads.Add(c.pad);
            laneHalf = map.laneWidth * 0.5f;
            trailMargin = rules.jungleTrailMargin;
            clearing = rules.jungleClearingRadius;
        }

        /// <summary>For a map with a jungle (routes or camps), or null when it has none.</summary>
        public static JungleArea For(MapDef map, Ruleset rules) =>
            map == null || (map.routes.Count == 0 && map.camps.Count == 0) ? null : new JungleArea(map, rules);

        /// <summary>Whether someone standing here (feet) is in the jungle.</summary>
        public bool Contains(Vec3 feet)
        {
            foreach (var l in lanes)
            {
                l.Project(feet, 0f, float.MaxValue, out float off);
                if (off <= laneHalf) return false;
            }
            foreach (var p in pads)
                if (LaneDef.Flat(feet - p).Magnitude <= clearing) return true;
            foreach (var r in routes)
                if (PathDistance(r.points, feet) <= r.width * 0.5f + trailMargin) return true;
            return false;
        }

        /// <summary>Flat distance from a point to a polyline.</summary>
        static float PathDistance(List<Vec3> points, Vec3 at)
        {
            var p = LaneDef.Flat(at);
            float best = float.MaxValue;
            for (int i = 1; i < points.Count; i++)
            {
                var a = LaneDef.Flat(points[i - 1]);
                var ab = LaneDef.Flat(points[i]) - a;
                float len2 = Vec3.Dot(ab, ab);
                float t = len2 < 1e-8f ? 0f : Math.Max(0f, Math.Min(1f, Vec3.Dot(p - a, ab) / len2));
                best = Math.Min(best, Vec3.Distance(p, a + ab * t));
            }
            return best;
        }
    }
}
