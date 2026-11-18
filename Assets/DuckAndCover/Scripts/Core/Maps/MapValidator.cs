using System;
using System.Collections.Generic;
using System.Text;

namespace DuckAndCover.Core
{
    /// <summary>How strict <see cref="MapValidator"/> is. Tooling numbers, not game rules.</summary>
    public class MapCheckSettings
    {
        /// <summary>Positions and sizes of turned twins may differ by this much (metres).</summary>
        public float symmetryTolerance = 0.01f;
        /// <summary>A team's travel to a zone and the other team's travel to its twin may differ by this much (seconds).</summary>
        public float travelTolerance = 0.25f;
        /// <summary>The shift must cut a team's travel from its own point to the middle by at least this much (seconds).</summary>
        public float minShiftGain = 1.5f;
        /// <summary>The first hill of king of the hill may favour one team by at most this much (seconds).</summary>
        public float firstHillSkew = 2.5f;
        public int spawnsPerTeam = 5;
    }

    /// <summary>A team's walking time to each zone, before and after the shift.</summary>
    public struct TravelRow
    {
        public ZoneId zone;
        public string label;
        public float a, b, aShifted, bShifted;
    }

    public sealed class MapReport
    {
        public readonly List<string> errors = new List<string>();
        public readonly List<TravelRow> fromSpawns = new List<TravelRow>();
        /// <summary>Seconds from each team's own point to the middle point, before and after the shift.</summary>
        public float pointToMidA, pointToMidB, pointToMidAShifted, pointToMidBShifted;
        public bool Ok => errors.Count == 0;

        public string Table()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Walking time from each spawn room (seconds at walk speed; after the shift in brackets)");
            sb.AppendLine("  zone        team A          team B");
            foreach (var r in fromSpawns)
                sb.AppendLine("  " + r.label.PadRight(10) + Pair(r.a, r.aShifted).PadRight(16) + Pair(r.b, r.bShifted));
            sb.AppendLine("Own point to the middle: A " + Pair(pointToMidA, pointToMidAShifted) + ", B " + Pair(pointToMidB, pointToMidBShifted));
            return sb.ToString();
        }

        static string Pair(float before, float after) => Fmt(before) + " (" + Fmt(after) + ")";
        static string Fmt(float s) => float.IsInfinity(s) ? "none" : s.ToString("0.0");
    }

    /// <summary>
    /// Checks a map is complete and fair: everything has its turned twin, the zone sets suit both
    /// modes, the spawns are safe from each other, every zone can be walked to from both spawn
    /// rooms before and after the shift, and both teams walk the same time to matching zones.
    /// </summary>
    public static class MapValidator
    {
        public static MapReport Check(MapDef map, Ruleset rules = null, MapCheckSettings settings = null)
        {
            rules = rules ?? Ruleset.Default;
            settings = settings ?? new MapCheckSettings();
            var report = new MapReport();
            var e = report.errors;
            if (map == null) { e.Add("no map"); return report; }

            CheckBasics(map, e);
            CheckZoneSets(map, e);
            CheckSymmetry(map, settings, e);
            CheckSpawns(map, settings, e);
            CheckSightlines(map, rules, e);
            if (e.Count == 0) CheckTravel(map, rules, settings, report);
            return report;
        }

        static void CheckBasics(MapDef map, List<string> e)
        {
            if (string.IsNullOrEmpty(map.id)) e.Add("map needs an id");
            if (map.halfX <= 0f || map.halfZ <= 0f) e.Add("map needs a positive size");
            var names = new HashSet<string>();
            foreach (var b in map.blocks)
            {
                if (b == null) { e.Add("a block is missing"); continue; }
                if (string.IsNullOrEmpty(b.name)) e.Add("a block has no name");
                else if (!names.Add(b.name)) e.Add("block name '" + b.name + "' is used twice");
                if (b.size.x <= 0f || b.size.y <= 0f || b.size.z <= 0f) e.Add("block " + b.name + " has no size");
                // Outer walls may stick out up to a metre.
                if (Math.Abs(b.center.x) > map.halfX + 1f || Math.Abs(b.center.z) > map.halfZ + 1f) e.Add("block " + b.name + " is outside the map");
                if (b.kind == BlockKind.Ramp && b.rampRisesToward % 90 != 0) e.Add("ramp " + b.name + " must rise along an axis");
            }
            bool shifts = false;
            foreach (var b in map.blocks) if (b != null && b.shift != ShiftRole.None) shifts = true;
            if (!shifts) e.Add("the map has no shift: mark the blocks it removes or adds");
            if (string.IsNullOrEmpty(map.shiftDescription)) e.Add("the map shift needs a description");
        }

        static void CheckZoneSets(MapDef map, List<string> e)
        {
            var ids = new HashSet<int>();
            foreach (var z in map.zones)
            {
                if (!ids.Add(z.id.value)) e.Add("zone " + z.id + " is defined twice");
                if (z.radius <= 0f || z.height <= 0f) e.Add("zone " + z.label + " has no size");
                if (Math.Abs(z.center.x) + z.radius > map.halfX || Math.Abs(z.center.z) + z.radius > map.halfZ) e.Add("zone " + z.label + " runs off the map");
            }
            var match = map.ToMatchMap();
            e.AddRange(match.Validate());
            var points = match.ZoneSet("points");
            if (points == null || points.zones.Count != 3) e.Add("capture and hold needs a 'points' set of three zones");
            else
            {
                bool fa = false, fb = false;
                foreach (var z in points.zones) { if (z.forwardSpawnFor == TeamId.A) fa = true; if (z.forwardSpawnFor == TeamId.B) fb = true; }
                if (!fa || !fb) e.Add("each team needs a forward spawn point in 'points'");
                var mid = map.Zone(points.zones[1].id);
                if (mid == null || Math.Abs(mid.center.x) > 0.01f || Math.Abs(mid.center.z) > 0.01f) e.Add("the middle point (second in 'points') must sit on the centre");
            }
            var hills = match.ZoneSet("hills");
            if (hills == null || hills.zones.Count != 4) e.Add("king of the hill needs a 'hills' set of four zones");
            foreach (var mode in new[] { ModeDef.CaptureAndHold(), ModeDef.KingOfTheHill() })
                foreach (var problem in mode.Validate(match)) e.Add(mode.id + ": " + problem);
        }

        static bool Near(Vec3 a, Vec3 b, float tol) => Math.Abs(a.x - b.x) <= tol && Math.Abs(a.y - b.y) <= tol && Math.Abs(a.z - b.z) <= tol;

        static void CheckSymmetry(MapDef map, MapCheckSettings s, List<string> e)
        {
            float tol = s.symmetryTolerance;
            foreach (var b in map.blocks)
            {
                if (b == null) continue;
                var want = MapDef.Turn(b.center);
                bool found = false;
                foreach (var o in map.blocks)
                    if (o != null && o.kind == b.kind && o.shift == b.shift && Near(o.center, want, tol) && Near(o.size, b.size, tol) &&
                        (b.kind != BlockKind.Ramp || o.rampRisesToward == MapDef.TurnYaw(b.rampRisesToward)))
                    { found = true; break; }
                if (!found) e.Add("block " + b.name + " has no twin turned about the centre");
            }
            foreach (var z in map.zones)
            {
                var want = MapDef.Turn(z.center);
                bool found = false;
                foreach (var o in map.zones)
                    if (o.set == z.set && Near(o.center, want, tol) && Math.Abs(o.radius - z.radius) <= tol && Math.Abs(o.height - z.height) <= tol &&
                        o.startOwner == z.startOwner.Other() && o.forwardSpawnFor == z.forwardSpawnFor.Other())
                    { found = true; break; }
                if (!found) e.Add("zone " + z.label + " has no twin turned about the centre (with the teams swapped)");
            }
            if (map.spawnsA.Count != map.spawnsB.Count) e.Add("the teams have different numbers of spawn spots");
            foreach (var a in map.spawnsA)
            {
                var want = MapDef.Turn(a, 0);
                bool found = false;
                foreach (var b in map.spawnsB)
                    if (Near(b.position, want.position, tol) && Math.Abs(b.yaw - want.yaw) <= 0.5f) { found = true; break; }
                if (!found) e.Add("team A's spawn spot at " + a.position + " has no twin in team B's room");
            }
            foreach (var f in map.forward)
            {
                var zone = map.Zone(f.zone);
                if (zone == null) { e.Add("forward spawn spot at " + f.position + " names no zone"); continue; }
                bool found = false;
                foreach (var o in map.forward)
                {
                    var oz = map.Zone(o.zone);
                    if (oz != null && oz.forwardSpawnFor == zone.forwardSpawnFor.Other() && Near(o.position, MapDef.Turn(f.position), tol)) { found = true; break; }
                }
                if (!found) e.Add("forward spawn spot at " + f.position + " has no twin");
            }
        }

        static void CheckSpawns(MapDef map, MapCheckSettings s, List<string> e)
        {
            if (map.spawnsA.Count < s.spawnsPerTeam || map.spawnsB.Count < s.spawnsPerTeam)
                e.Add("each team needs " + s.spawnsPerTeam + " spawn spots");
            foreach (var f in map.forward)
            {
                var zone = map.Zone(f.zone);
                if (zone != null && zone.forwardSpawnFor == TeamId.None) e.Add("forward spawn spot at " + f.position + " is at a zone nobody spawns at");
            }
            foreach (var z in map.zones)
                if (z.forwardSpawnFor != TeamId.None)
                {
                    int n = 0;
                    foreach (var f in map.forward) if (f.zone == z.id) n++;
                    if (n == 0) e.Add("zone " + z.label + " is a forward spawn with no spawn spots");
                }
            var all = new List<SpawnPoint>();
            all.AddRange(map.spawnsA); all.AddRange(map.spawnsB); all.AddRange(map.forward); all.AddRange(map.dummies);
            foreach (var p in all)
            {
                float floor = SurfaceAt(map, p.position.x, p.position.z, false);
                if (Math.Abs(floor - p.position.y) > 0.05f) e.Add("spot at " + p.position + " is not on open floor");
            }
        }

        /// <summary>The top of the highest solid block at a point.</summary>
        internal static float SurfaceAt(MapDef map, float x, float z, bool shifted)
        {
            float top = float.NegativeInfinity;
            foreach (var b in map.blocks)
            {
                if (!b.Solid || !b.Present(shifted)) continue;
                Vec3 lo = b.Min, hi = b.Max;
                if (x < lo.x || x > hi.x || z < lo.z || z > hi.z) continue;
                float t = b.TopAt(x, z);
                if (t > top) top = t;
            }
            return top;
        }

        /// <summary>No spawn room sees into the other, and the middle point doesn't see into either.</summary>
        static void CheckSightlines(MapDef map, Ruleset rules, List<string> e)
        {
            var eye = Vec3.Up * rules.eyeHeight;
            foreach (bool shifted in new[] { false, true })
            {
                string when = shifted ? " after the shift" : "";
                foreach (var a in map.spawnsA)
                    foreach (var b in map.spawnsB)
                        if (Clear(map, a.position + eye, b.position + eye, shifted))
                        {
                            e.Add("spawn spots " + a.position + " and " + b.position + " can see each other" + when);
                            return;
                        }
                foreach (var z in map.zones)
                {
                    if (!(Math.Abs(z.center.x) < 0.01f && Math.Abs(z.center.z) < 0.01f)) continue;
                    var spots = new List<SpawnPoint>(map.spawnsA);
                    spots.AddRange(map.spawnsB);
                    foreach (var p in spots)
                        for (float dx = -z.radius; dx <= z.radius; dx += z.radius)
                            for (float dz = -z.radius; dz <= z.radius; dz += z.radius)
                                if (Clear(map, z.center + new Vec3(dx, 0f, dz) + eye, p.position + eye, shifted))
                                {
                                    e.Add("the middle point can see the spawn spot at " + p.position + when);
                                    return;
                                }
                }
            }
        }

        /// <summary>True when the segment touches no solid block (ramps count as their whole box).</summary>
        public static bool Clear(MapDef map, Vec3 from, Vec3 to, bool shifted)
        {
            foreach (var b in map.blocks)
                if (b.Solid && b.Present(shifted) && b.kind != BlockKind.Ground && SegmentHitsBox(from, to, b.Min, b.Max)) return false;
            return true;
        }

        static bool SegmentHitsBox(Vec3 p, Vec3 q, Vec3 lo, Vec3 hi)
        {
            float t0 = 0f, t1 = 1f;
            Vec3 d = q - p;
            for (int axis = 0; axis < 3; axis++)
            {
                float o = axis == 0 ? p.x : axis == 1 ? p.y : p.z;
                float v = axis == 0 ? d.x : axis == 1 ? d.y : d.z;
                float min = axis == 0 ? lo.x : axis == 1 ? lo.y : lo.z;
                float max = axis == 0 ? hi.x : axis == 1 ? hi.y : hi.z;
                if (Math.Abs(v) < 1e-7f) { if (o < min || o > max) return false; continue; }
                float a = (min - o) / v, b = (max - o) / v;
                if (a > b) { var t = a; a = b; b = t; }
                if (a > t0) t0 = a;
                if (b < t1) t1 = b;
                if (t0 > t1) return false;
            }
            return true;
        }

        static Vec3 BaseCentre(List<SpawnPoint> spots)
        {
            var c = Vec3.Zero;
            foreach (var s in spots) c += s.position;
            return spots.Count == 0 ? c : c / spots.Count;
        }

        static void CheckTravel(MapDef map, Ruleset rules, MapCheckSettings s, MapReport report)
        {
            var e = report.errors;
            var navs = new[] { new MapNav(map, rules, false), new MapNav(map, rules, true) };
            var times = new Dictionary<int, float[]>(); // zone id -> a, b, a shifted, b shifted
            for (int n = 0; n < 2; n++)
            {
                var fa = navs[n].TravelFrom(BaseCentre(map.spawnsA));
                var fb = navs[n].TravelFrom(BaseCentre(map.spawnsB));
                foreach (var z in map.zones)
                {
                    if (!times.TryGetValue(z.id.value, out var t)) times[z.id.value] = t = new float[4];
                    t[n * 2] = navs[n].TimeToZone(fa, z);
                    t[n * 2 + 1] = navs[n].TimeToZone(fb, z);
                }
            }
            foreach (var z in map.zones)
            {
                var t = times[z.id.value];
                report.fromSpawns.Add(new TravelRow { zone = z.id, label = z.label, a = t[0], b = t[1], aShifted = t[2], bShifted = t[3] });
                for (int k = 0; k < 4; k++)
                    if (float.IsInfinity(t[k]))
                        e.Add("team " + (k % 2 == 0 ? "A" : "B") + " can't walk to " + z.label + (k >= 2 ? " after the shift" : ""));
            }
            if (e.Count > 0) return;

            // Matching zones, matching walks.
            foreach (var z in map.zones)
            {
                MapZone twin = null;
                foreach (var o in map.zones)
                    if (o.set == z.set && Near(o.center, MapDef.Turn(z.center), s.symmetryTolerance)) twin = o;
                if (twin == null) continue;
                var t = times[z.id.value];
                var u = times[twin.id.value];
                for (int n = 0; n < 2; n++)
                    if (Math.Abs(t[n * 2] - u[n * 2 + 1]) > s.travelTolerance)
                        e.Add("team A walks " + t[n * 2].ToString("0.0") + " s to " + z.label + " but team B walks " + u[n * 2 + 1].ToString("0.0") + " s to " + twin.label + (n == 1 ? " after the shift" : ""));
            }

            // The first hill should not hand one team the opening.
            var hills = map.ToMatchMap().ZoneSet("hills");
            if (hills != null && hills.zones.Count > 0)
            {
                var first = map.Zone(hills.zones[0].id);
                var t = times[first.id.value];
                if (Math.Abs(t[0] - t[1]) > s.firstHillSkew)
                    e.Add("the first hill (" + first.label + ") is " + Math.Abs(t[0] - t[1]).ToString("0.0") + " s closer for one team");
            }

            // The shift has to open something that matters: a quicker way from your own point to the middle.
            var points = map.ToMatchMap().ZoneSet("points");
            var midZone = map.Zone(points.zones[1].id);
            MapZone own(TeamId team) { foreach (var z in map.zones) if (z.set == "points" && z.forwardSpawnFor == team) return z; return null; }
            float PointToMid(int n, TeamId team) => navs[n].TimeToZone(navs[n].TravelFrom(own(team).center), midZone);
            report.pointToMidA = PointToMid(0, TeamId.A);
            report.pointToMidB = PointToMid(0, TeamId.B);
            report.pointToMidAShifted = PointToMid(1, TeamId.A);
            report.pointToMidBShifted = PointToMid(1, TeamId.B);
            if (report.pointToMidA - report.pointToMidAShifted < s.minShiftGain || report.pointToMidB - report.pointToMidBShifted < s.minShiftGain)
                e.Add("the map shift doesn't shorten the walk from a team's point to the middle by " + s.minShiftGain + " s");
        }
    }
}
