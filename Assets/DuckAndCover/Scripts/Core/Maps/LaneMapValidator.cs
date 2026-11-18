using System;
using System.Collections.Generic;
using System.Text;

namespace DuckAndCover.Core
{
    /// <summary>How strict <see cref="LaneMapValidator"/> is. Tooling numbers, not game rules.</summary>
    public class LaneMapCheckSettings
    {
        public float symmetryTolerance = 0.01f;
        /// <summary>Matching walks for the two teams may differ by this much (seconds).</summary>
        public float travelTolerance = 0.25f;
        public int spawnsPerTeam = 5;
        /// <summary>No straight, clear sight line may join two lane points further apart along the lane than this share of it.</summary>
        public float maxSightShare = 1f / 3f;
        /// <summary>Lane points are sampled this far apart (metres) for the sight line checks.</summary>
        public float sightStep = 3f;
        /// <summary>A drop from a ledge must take at most this long (seconds).</summary>
        public float maxDropSeconds = 2.5f;
        /// <summary>Climbing back up to a ledge must take at least this much longer than dropping from it.</summary>
        public float minClimbDetour = 5f;
        /// <summary>A route's end must be this short a walk from the lane (seconds).</summary>
        public float routeJoinSeconds = 3f;
        /// <summary>A route may take at most this much longer to walk than its centre line (share).</summary>
        public float routeSlack = 0.35f;
        /// <summary>Launch pads must land within this share of the lane's length from its middle.</summary>
        public float launchMidShare = 0.2f;
        /// <summary>Bastions must stand within this far (metres) of the lane's centre line.</summary>
        public float bastionOffLane = 2f;
        /// <summary>Flag Rush: each flag spot's walks from the two bases may differ by this share of the longer one.</summary>
        public float flagFairness = 0.05f;
        public int minFlagSpots = 2;
    }

    /// <summary>The walks the lane-map report lists.</summary>
    public sealed class LaneMapReport
    {
        public readonly List<string> errors = new List<string>();
        public bool Ok => errors.Count == 0;
        public float laneLength;
        /// <summary>Lane points that see furthest along the lane, and how far (metres along it).</summary>
        public float longestSight;
        /// <summary>Walking seconds from each team's spawn room to things on the map: (label, from A, from B).</summary>
        public readonly List<(string label, float a, float b)> walks = new List<(string, float, float)>();
        /// <summary>Each drop: seconds down, seconds back up (infinity when there is no way).</summary>
        public readonly List<(string name, float down, float up)> drops = new List<(string, float, float)>();
        public readonly List<(string name, float seconds, float lineSeconds)> routes = new List<(string, float, float)>();
        /// <summary>Flag Rush: each flag spot's walk from team A's base and team B's, before the shift and after it.</summary>
        public readonly List<FlagSpotRow> flagSpots = new List<FlagSpotRow>();

        public string Table()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Lane: " + laneLength.ToString("0") + " m base to base; the longest clear sight line along it covers " + longestSight.ToString("0") + " m");
            sb.AppendLine("Walking time from each spawn room (seconds at walk speed)");
            sb.AppendLine("  to                      team A    team B");
            foreach (var w in walks) sb.AppendLine("  " + w.label.PadRight(24) + Fmt(w.a).PadRight(10) + Fmt(w.b));
            sb.AppendLine("One-way drops (seconds down / back up)");
            foreach (var d in drops) sb.AppendLine("  " + d.name.PadRight(24) + Fmt(d.down) + " / " + Fmt(d.up));
            sb.AppendLine("Routes (seconds to walk / along the centre line)");
            foreach (var r in routes) sb.AppendLine("  " + r.name.PadRight(24) + Fmt(r.seconds) + " / " + Fmt(r.lineSeconds));
            if (flagSpots.Count > 0)
            {
                sb.AppendLine("Flag spots: walk from each base's capture zone (after the shift in brackets)");
                foreach (var f in flagSpots)
                    sb.AppendLine("  " + f.spot.ToString().PadRight(24) + Fmt(f.a) + " (" + Fmt(f.aShifted) + ") / " + Fmt(f.b) + " (" + Fmt(f.bShifted) + ")  gap " + (f.Skew * 100f).ToString("0.0") + "%");
            }
            return sb.ToString();
        }

        static string Fmt(float s) => float.IsInfinity(s) ? "none" : s.ToString("0.0");
    }

    /// <summary>
    /// Checks a lane map (Overrun) is complete and fair: mirror symmetry, one lane the same from both
    /// ends that minions can walk base to base without leaving it, Bastions on it, no sight line down
    /// more than a third of it, jungle routes that join the lane at both ends, branches that end at
    /// camps, one-way ledges and drops, a hidden Juggernaut shelf, launch pads that land at mid-lane on
    /// their own side along a clear arc, two ways into every base, side routes shut until the shift,
    /// safe spawns, and equal walks for both teams.
    /// </summary>
    public static class LaneMapValidator
    {
        public static LaneMapReport Check(MapDef map, Ruleset rules = null, LaneMapCheckSettings settings = null)
        {
            rules = rules ?? Ruleset.Default;
            settings = settings ?? new LaneMapCheckSettings();
            var report = new LaneMapReport();
            var e = report.errors;
            if (map == null) { e.Add("no map"); return report; }
            if (!map.IsLaneMap) { e.Add("map " + map.id + " has no lane"); return report; }

            CheckBasics(map, e);
            CheckSymmetry(map, settings, e);
            CheckLaneData(map, settings, report);
            CheckSpawns(map, rules, settings, e);
            if (e.Count > 0) return report;

            var nav = new MapNav(map, rules, false);
            var navShifted = new MapNav(map, rules, true);
            CheckLaneWalk(map, nav, settings, e);
            CheckSightlines(map, rules, settings, report);
            CheckRoutes(map, nav, navShifted, rules, settings, report);
            CheckDrops(map, nav, settings, report);
            CheckCamps(map, rules, e);
            CheckLaunchPads(map, rules, settings, e);
            CheckEntrances(map, rules, e);
            CheckWalks(map, nav, settings, report);
            CheckFlagRush(map, nav, navShifted, settings, report);
            return report;
        }

        // ---- Data ----

        static void CheckBasics(MapDef map, List<string> e)
        {
            if (string.IsNullOrEmpty(map.id)) e.Add("map needs an id");
            if (map.halfX <= 0f || map.halfZ <= 0f) e.Add("map needs a positive size");
            if (map.symmetry != MapSymmetry.MirrorZ) e.Add("a lane map is mirrored end to end (symmetry MirrorZ)");
            var names = new HashSet<string>();
            foreach (var b in map.blocks)
            {
                if (b == null) { e.Add("a block is missing"); continue; }
                if (string.IsNullOrEmpty(b.name)) e.Add("a block has no name");
                else if (!names.Add(b.name)) e.Add("block name '" + b.name + "' is used twice");
                if (b.size.x <= 0f || b.size.y <= 0f || b.size.z <= 0f) e.Add("block " + b.name + " has no size");
                if (Math.Abs(b.center.x) > map.halfX + 1f || Math.Abs(b.center.z) > map.halfZ + 1f) e.Add("block " + b.name + " is outside the map");
                if (b.kind == BlockKind.Ramp && b.rampRisesToward % 90 != 0) e.Add("ramp " + b.name + " must rise along an axis");
            }
            bool shifts = false;
            foreach (var b in map.blocks) if (b != null && b.shift != ShiftRole.None) shifts = true;
            if (!shifts) e.Add("the map has no shift: mark the blocks it removes or adds");
            if (string.IsNullOrEmpty(map.shiftDescription)) e.Add("the map shift needs a description");
        }

        static bool Near(Vec3 a, Vec3 b, float tol) => Math.Abs(a.x - b.x) <= tol && Math.Abs(a.y - b.y) <= tol && Math.Abs(a.z - b.z) <= tol;

        static void CheckSymmetry(MapDef map, LaneMapCheckSettings s, List<string> e)
        {
            float tol = s.symmetryTolerance;
            foreach (var b in map.blocks)
            {
                if (b == null) continue;
                var want = MapDef.Mirror(b.center);
                bool found = false;
                foreach (var o in map.blocks)
                    if (o != null && o.kind == b.kind && o.shift == b.shift && Near(o.center, want, tol) && Near(o.size, b.size, tol) &&
                        (b.kind != BlockKind.Ramp || o.rampRisesToward == MapDef.MirrorYaw(b.rampRisesToward)))
                    { found = true; break; }
                if (!found) e.Add("block " + b.name + " has no mirror twin");
            }
            foreach (var lane in map.lanes)
            {
                int n = lane.points.Count;
                for (int i = 0; i < n; i++)
                    if (!Near(MapDef.Mirror(lane.points[i]), lane.points[n - 1 - i], tol)) { e.Add("lane " + lane.id + " isn't the same seen from both ends"); break; }
            }
            foreach (var b in map.bastions)
            {
                bool found = false;
                foreach (var o in map.bastions)
                    if (o.team == b.team.Other() && o.tier == b.tier && Near(o.position, MapDef.Mirror(b.position), tol) && Math.Abs(o.radius - b.radius) <= tol) found = true;
                if (!found) e.Add("team " + b.team + "'s " + b.tier + " Bastion has no mirror twin");
            }
            foreach (var c in map.camps)
            {
                bool found = false;
                foreach (var o in map.camps)
                    if (o.kind == c.kind && o.half == c.half.Other() && Near(o.pad, MapDef.Mirror(c.pad), tol) && Math.Abs(o.padRadius - c.padRadius) <= tol &&
                        o.guardSpots.Count == c.guardSpots.Count)
                        found = true;
                if (!found) e.Add("camp " + c.id + " has no mirror twin");
            }
            foreach (var p in map.launchPads)
            {
                bool found = false;
                foreach (var o in map.launchPads)
                    if (o.team == p.team.Other() && Near(o.center, MapDef.Mirror(p.center), tol) && Near(o.landing, MapDef.Mirror(p.landing), tol) &&
                        Math.Abs(o.apexHeight - p.apexHeight) <= tol && Math.Abs(o.seconds - p.seconds) <= tol && Math.Abs(o.radius - p.radius) <= tol)
                        found = true;
                if (!found) e.Add("launch pad " + p.name + " has no mirror twin");
            }
            foreach (var d in map.drops)
            {
                bool found = false;
                foreach (var o in map.drops) if (Near(o.top, MapDef.Mirror(d.top), tol) && Near(o.bottom, MapDef.Mirror(d.bottom), tol)) found = true;
                if (!found) e.Add("drop " + d.name + " has no mirror twin");
            }
            foreach (var r in map.routes)
            {
                bool found = false;
                foreach (var o in map.routes)
                {
                    if (o.kind != r.kind || o.points.Count != r.points.Count || Math.Abs(o.width - r.width) > tol) continue;
                    bool same = true, reversed = true;
                    for (int i = 0; i < r.points.Count; i++)
                    {
                        if (!Near(o.points[i], MapDef.Mirror(r.points[i]), tol)) same = false;
                        if (!Near(o.points[r.points.Count - 1 - i], MapDef.Mirror(r.points[i]), tol)) reversed = false;
                    }
                    if (same || reversed) found = true;
                }
                if (!found) e.Add("route " + r.name + " has no mirror twin");
            }
            foreach (var x in map.entrances)
            {
                bool found = false;
                foreach (var o in map.entrances) if (o.team == x.team.Other() && Near(o.position, MapDef.Mirror(x.position), tol)) found = true;
                if (!found) e.Add("entrance " + x.name + " has no mirror twin");
            }
            if (map.spawnsA.Count != map.spawnsB.Count) e.Add("the teams have different numbers of spawn spots");
            foreach (var a in map.spawnsA)
            {
                var want = MapDef.Mirror(a);
                bool found = false;
                foreach (var b in map.spawnsB)
                    if (Near(b.position, want.position, tol) && Math.Abs(b.yaw - want.yaw) <= 0.5f) found = true;
                if (!found) e.Add("team A's spawn spot at " + a.position + " has no mirror twin in team B's room");
            }
        }

        static void CheckLaneData(MapDef map, LaneMapCheckSettings s, LaneMapReport report)
        {
            var e = report.errors;
            if (map.lanes.Count != 1) e.Add("a lane map has one lane (found " + map.lanes.Count + ")");
            var lane = map.lanes[0];
            e.AddRange(lane.Validate());
            report.laneLength = lane.Length;
            if (lane.points.Count >= 2)
            {
                if (lane.points[0].z >= 0f || lane.points[lane.points.Count - 1].z <= 0f) e.Add("the lane runs from team A's end (-z) to team B's (+z)");
                foreach (var p in lane.points)
                    if (Math.Abs(p.x) > map.halfX || Math.Abs(p.z) > map.halfZ) e.Add("lane point " + p + " is off the map");
            }
            foreach (var team in new[] { TeamId.A, TeamId.B })
                foreach (var tier in new[] { BastionTier.Forward, BastionTier.Core })
                    if (map.Bastion(team, tier) == null) e.Add("team " + team + " has no " + tier + " Bastion");
            if (e.Count > 0) return;
            foreach (var b in map.bastions)
            {
                lane.Project(b.position, 0f, float.MaxValue, out float off);
                if (off > s.bastionOffLane) e.Add("team " + b.team + "'s " + b.tier + " Bastion stands " + off.ToString("0.0") + " m off the lane");
            }
            // Core nearer its own end than Forward, and both in their own half.
            foreach (var team in new[] { TeamId.A, TeamId.B })
            {
                float Along(MapBastion b) => lane.Project(b.position, 0f, float.MaxValue, out _);
                float core = Along(map.Bastion(team, BastionTier.Core)), fwd = Along(map.Bastion(team, BastionTier.Forward));
                float half = lane.Length * 0.5f;
                bool ok = team == TeamId.A ? core < fwd && fwd < half : core > fwd && fwd > half;
                if (!ok) e.Add("team " + team + "'s Bastions must stand in its own half, the Core nearer its base than the Forward");
            }
            var kinds = new Dictionary<CampKind, int>();
            foreach (var c in map.camps)
            {
                kinds.TryGetValue(c.kind, out int n);
                kinds[c.kind] = n + 1;
                if (c.kind == CampKind.Juggernaut ? c.half != TeamId.None : c.half == TeamId.None) e.Add("camp " + c.id + ": the Juggernaut camp belongs to no half, every other camp to one");
                if (c.half == TeamId.A && c.pad.z >= 0f || c.half == TeamId.B && c.pad.z <= 0f) e.Add("camp " + c.id + " isn't in its own half");
                if (c.kind == CampKind.Juggernaut && Math.Abs(c.pad.z) > 0.01f) e.Add("the Juggernaut camp must sit on the middle line");
            }
            foreach (var want in new[] { (CampKind.Juggernaut, 1), (CampKind.Damage, 2), (CampKind.Experience, 2), (CampKind.Speed, 2) })
            {
                kinds.TryGetValue(want.Item1, out int n);
                if (n != want.Item2) e.Add("Breakwater-style lane maps have " + want.Item2 + " " + want.Item1 + " camp(s) (found " + n + ")");
            }
            if (map.launchPads.Count != 2) e.Add("one launch pad per base (found " + map.launchPads.Count + ")");
        }

        static void CheckSpawns(MapDef map, Ruleset rules, LaneMapCheckSettings s, List<string> e)
        {
            if (map.spawnsA.Count < s.spawnsPerTeam || map.spawnsB.Count < s.spawnsPerTeam) e.Add("each team needs " + s.spawnsPerTeam + " spawn spots");
            var all = new List<SpawnPoint>();
            all.AddRange(map.spawnsA); all.AddRange(map.spawnsB); all.AddRange(map.dummies);
            foreach (var p in all) OnFloor(map, "spot", p.position, e);
            foreach (var b in map.bastions) OnFloor(map, b.team + " " + b.tier + " Bastion", b.position, e);
            foreach (var c in map.camps)
            {
                OnFloor(map, "camp " + c.id + "'s pad", c.pad, e);
                foreach (var g in c.guardSpots) OnFloor(map, "camp " + c.id + "'s guard", g, e);
            }
            foreach (var p in map.launchPads) { OnFloor(map, "launch pad " + p.name, p.center, e); OnFloor(map, "launch pad " + p.name + "'s landing", p.landing, e); }
            foreach (var p in map.lanes[0].points) OnFloor(map, "lane point", p, e);
            var eye = Vec3.Up * rules.eyeHeight;
            foreach (bool shifted in new[] { false, true })
                foreach (var a in map.spawnsA)
                    foreach (var b in map.spawnsB)
                        if (MapValidator.Clear(map, a.position + eye, b.position + eye, shifted))
                        {
                            e.Add("spawn spots " + a.position + " and " + b.position + " can see each other");
                            return;
                        }
        }

        static void OnFloor(MapDef map, string what, Vec3 p, List<string> e)
        {
            foreach (bool shifted in new[] { false, true })
            {
                float floor = MapValidator.SurfaceAt(map, p.x, p.z, shifted);
                if (Math.Abs(floor - p.y) > 0.05f) { e.Add(what + " at " + p + " is not on open floor" + (shifted ? " after the shift" : "")); return; }
            }
        }

        // ---- The lane ----

        static float CellTime(MapNav nav, MapNav.Field f, Vec3 p) => f.seconds[nav.J(p.z) * nav.width + nav.I(p.x)];

        /// <summary>Minions get from one base to the other without leaving the lane, past every Bastion.</summary>
        static void CheckLaneWalk(MapDef map, MapNav nav, LaneMapCheckSettings s, List<string> e)
        {
            var lane = map.lanes[0];
            float half = map.laneWidth * 0.5f;
            bool OnLane(int i, int j)
            {
                lane.Project(new Vec3(nav.X(i), 0f, nav.Z(j)), 0f, float.MaxValue, out float off);
                return off <= half;
            }
            var start = lane.points[0];
            var end = lane.points[lane.points.Count - 1];
            var f = nav.TravelFrom(start, OnLane);
            if (float.IsInfinity(CellTime(nav, f, end))) { e.Add("minions can't walk the lane from end to end without leaving it"); return; }
            foreach (var b in map.bastions)
                if (float.IsInfinity(CellTime(nav, f, b.position))) e.Add("minions can't reach team " + b.team + "'s " + b.tier + " Bastion along the lane");
            // And the same seen from team B's end (the mirror makes it so; this catches a broken mirror of the walk grid).
            var back = nav.TravelFrom(end, OnLane);
            float ab = CellTime(nav, f, end), ba = CellTime(nav, back, start);
            if (Math.Abs(ab - ba) > s.travelTolerance) e.Add("the lane is " + ab.ToString("0.0") + " s from A's end but " + ba.ToString("0.0") + " s from B's");
        }

        static void CheckSightlines(MapDef map, Ruleset rules, LaneMapCheckSettings s, LaneMapReport report)
        {
            var lane = map.lanes[0];
            float len = lane.Length;
            var eye = Vec3.Up * rules.eyeHeight;
            var samples = new List<(float along, Vec3 p)>();
            for (float d = 0f; d <= len; d += s.sightStep) samples.Add((d, lane.PointAt(d) + eye));
            float limit = len * s.maxSightShare;
            foreach (bool shifted in new[] { false, true })
                for (int i = 0; i < samples.Count; i++)
                    for (int j = i + 1; j < samples.Count; j++)
                    {
                        float span = samples[j].along - samples[i].along;
                        if (span <= report.longestSight && span <= limit) continue;
                        if (!MapValidator.Clear(map, samples[i].p, samples[j].p, shifted)) continue;
                        if (span > report.longestSight) report.longestSight = span;
                        if (span > limit)
                        {
                            report.errors.Add("from " + samples[i].p + " you can see " + span.ToString("0") + " m down the lane (at most " + limit.ToString("0") + ")");
                            return;
                        }
                    }
            // The Juggernaut shelf can't be seen from anywhere on the lane.
            foreach (var c in map.camps)
            {
                if (c.kind != CampKind.Juggernaut) continue;
                var target = c.pad + eye;
                foreach (var sp in samples)
                    for (float dx = -c.padRadius; dx <= c.padRadius; dx += c.padRadius)
                        for (float dz = -c.padRadius; dz <= c.padRadius; dz += c.padRadius)
                            if (MapValidator.Clear(map, sp.p, target + new Vec3(dx, 0f, dz), false))
                            {
                                report.errors.Add("the Juggernaut camp can be seen from the lane at " + sp.p);
                                return;
                            }
            }
        }

        // ---- Routes, drops and camps ----

        static void CheckRoutes(MapDef map, MapNav nav, MapNav navShifted, Ruleset rules, LaneMapCheckSettings s, LaneMapReport report)
        {
            var e = report.errors;
            var lane = map.lanes[0];
            bool sawJungleWest = false, sawJungleEast = false;
            foreach (var r in map.routes)
            {
                if (r.points.Count < 2) { e.Add("route " + r.name + " needs two points"); continue; }
                var walkNav = r.kind == RouteKind.SideRoute ? navShifted : nav;
                var first = r.points[0];
                var last = r.points[r.points.Count - 1];
                // The whole route walks about as quickly as its centre line: nothing blocks it.
                float line = 0f;
                for (int i = 1; i < r.points.Count; i++) line += LaneDef.Flat(r.points[i] - r.points[i - 1]).Magnitude;
                float lineSeconds = line / rules.walkSpeed;
                float walked = 0f;
                for (int i = 1; i < r.points.Count && !float.IsInfinity(walked); i++)
                    walked += CellTime(walkNav, walkNav.TravelFrom(r.points[i - 1]), r.points[i]);
                report.routes.Add((r.name, walked, lineSeconds));
                if (float.IsInfinity(walked)) e.Add("route " + r.name + " is blocked" + (r.kind == RouteKind.SideRoute ? " even after the shift" : ""));
                else if (walked > lineSeconds * (1f + s.routeSlack) + 1f) e.Add("route " + r.name + " takes " + walked.ToString("0.0") + " s to walk, against " + lineSeconds.ToString("0.0") + " s along it: something is in the way");

                float JoinSeconds(Vec3 p)
                {
                    var f = nav.TravelFrom(p);
                    float best = float.PositiveInfinity;
                    for (float d = 0f; d <= lane.Length; d += 1f) best = Math.Min(best, CellTime(nav, f, lane.PointAt(d)));
                    return best;
                }
                switch (r.kind)
                {
                    case RouteKind.Jungle:
                        if (JoinSeconds(first) > s.routeJoinSeconds || JoinSeconds(last) > s.routeJoinSeconds) e.Add("jungle route " + r.name + " must join the lane at both ends");
                        else
                        {
                            float a0 = lane.Project(first, 0f, float.MaxValue, out _), a1 = lane.Project(last, 0f, float.MaxValue, out _);
                            if (Math.Abs(a1 - a0) < lane.Length * 0.25f) e.Add("jungle route " + r.name + " joins the lane twice in nearly the same place");
                        }
                        // Which side of the lane it runs on, judged at its middle.
                        var midPoint = r.points[r.points.Count / 2];
                        var lanePoint = lane.PointAt(lane.Project(midPoint, 0f, float.MaxValue, out _));
                        if (midPoint.x < lanePoint.x) sawJungleWest = true; else sawJungleEast = true;
                        break;
                    case RouteKind.CutThrough:
                        if (JoinSeconds(last) > s.routeJoinSeconds) e.Add("cut-through " + r.name + " must end on the lane");
                        if (!OnAnotherRoute(map, r, first)) e.Add("cut-through " + r.name + " must start on a jungle route");
                        break;
                    case RouteKind.Branch:
                        if (!OnAnotherRoute(map, r, first)) e.Add("branch " + r.name + " must start on another route");
                        bool camp = false;
                        foreach (var c in map.camps) if (LaneDef.Flat(c.pad - last).Magnitude <= c.padRadius + 2f) camp = true;
                        if (!camp) e.Add("branch " + r.name + " must end at a camp");
                        break;
                    case RouteKind.Loop:
                        if (!OnAnotherRoute(map, r, first) || !OnAnotherRoute(map, r, last)) e.Add("loop " + r.name + " must start and end on other routes");
                        break;
                    case RouteKind.SideRoute:
                        // Walking only along the route itself, its far end is out of reach until the shift.
                        var along = new ArtPath("trail", r.width) { points = r.points };
                        bool OnRoute(int i, int j) => along.DistanceTo(nav.X(i), nav.Z(j)) <= r.width * 0.5f + 1f;
                        if (!float.IsInfinity(CellTime(nav, nav.TravelFrom(first, OnRoute), last))) e.Add("side route " + r.name + " is open before the shift");
                        break;
                }
            }
            if (!sawJungleWest || !sawJungleEast) e.Add("the lane needs a jungle route on each side");
        }

        static bool OnAnotherRoute(MapDef map, MapRoute r, Vec3 p)
        {
            foreach (var o in map.routes)
                if (o != r && new ArtPath("trail", o.width) { points = o.points }.DistanceTo(p.x, p.z) <= o.width * 0.5f) return true;
            return false;
        }

        static void CheckDrops(MapDef map, MapNav nav, LaneMapCheckSettings s, LaneMapReport report)
        {
            foreach (var d in map.drops)
            {
                float down = CellTime(nav, nav.TravelFrom(d.top), d.bottom);
                float up = CellTime(nav, nav.TravelFrom(d.bottom), d.top);
                report.drops.Add((d.name, down, up));
                if (d.top.y - d.bottom.y < 2f) report.errors.Add("drop " + d.name + " is less than 2 m high");
                if (down > s.maxDropSeconds) report.errors.Add("drop " + d.name + " takes " + down.ToString("0.0") + " s to drop down: it should be a quick way off the ledge");
                else if (!float.IsInfinity(up) && up < down + s.minClimbDetour) report.errors.Add("drop " + d.name + " can be climbed back up in " + up.ToString("0.0") + " s");
            }
        }

        static void CheckCamps(MapDef map, Ruleset rules, List<string> e)
        {
            for (int i = 0; i < map.camps.Count; i++)
                for (int j = i + 1; j < map.camps.Count; j++)
                    if (LaneDef.Flat(map.camps[i].pad - map.camps[j].pad).Magnitude < map.camps[i].padRadius + map.camps[j].padRadius + 4f)
                        e.Add("camps " + map.camps[i].id + " and " + map.camps[j].id + " are too close together");
            var lane = map.lanes[0];
            foreach (var c in map.camps)
            {
                lane.Project(c.pad, 0f, float.MaxValue, out float off);
                if (off < map.laneWidth * 0.5f + c.padRadius + 2f) e.Add("camp " + c.id + " sits on the lane: camps are in the jungle");
            }
        }

        static void CheckLaunchPads(MapDef map, Ruleset rules, LaneMapCheckSettings s, List<string> e)
        {
            var lane = map.lanes[0];
            foreach (var p in map.launchPads)
            {
                if (p.seconds <= 0f || p.apexHeight <= 0f || p.radius <= 0f) { e.Add("launch pad " + p.name + " needs a flight time, a height and a size"); continue; }
                bool ownHalf = p.team == TeamId.A ? p.center.z < 0f && p.landing.z < 0f : p.center.z > 0f && p.landing.z > 0f;
                if (!ownHalf) e.Add("launch pad " + p.name + " must stand and land in its own team's half");
                lane.Project(p.landing, 0f, float.MaxValue, out float off);
                float along = lane.Project(p.landing, 0f, float.MaxValue, out _);
                if (off > map.laneWidth * 0.5f) e.Add("launch pad " + p.name + " must land on the lane");
                if (Math.Abs(along - lane.Length * 0.5f) > lane.Length * s.launchMidShare) e.Add("launch pad " + p.name + " must land near the middle of the lane");
                var core = map.Bastion(p.team, BastionTier.Core);
                if (core != null && Vec3.Distance(core.position, p.center) > 20f) e.Add("launch pad " + p.name + " must be in its base's plaza");
                // The body (feet to head) flies clear of every block, after the take-off and before the landing.
                int steps = 40;
                for (int k = 1; k < steps; k++)
                {
                    float t0 = p.seconds * k / steps, t1 = p.seconds * (k + 1) / steps;
                    if (k + 1 == steps) break;
                    var a = p.PointAt(t0); var b = p.PointAt(t1);
                    foreach (float h in new[] { 0.3f, rules.standHeight })
                        if (!MapValidator.Clear(map, a + Vec3.Up * h, b + Vec3.Up * h, false))
                        {
                            e.Add("launch pad " + p.name + "'s arc hits something " + (t0).ToString("0.0") + " s into the flight, near " + a);
                            k = steps;
                            break;
                        }
                }
            }
        }

        /// <summary>Every base has two ways in that are open from the start: block either one and the base is still reachable.</summary>
        static void CheckEntrances(MapDef map, Ruleset rules, List<string> e)
        {
            var mid = map.lanes[0].PointAt(map.lanes[0].Length * 0.5f);
            foreach (var team in new[] { TeamId.A, TeamId.B })
            {
                var doors = map.entrances.FindAll(x => x.team == team);
                if (doors.Count < 2) { e.Add("team " + team + "'s base needs two ways in"); continue; }
                var spawns = team == TeamId.A ? map.spawnsA : map.spawnsB;
                var home = spawns[spawns.Count / 2].position;
                foreach (var door in doors)
                {
                    var blocked = Clone(map);
                    float w = door.halfWidth * 2f + 3f;
                    blocked.blocks.Add(new MapBlock("Blocked" + door.name, BlockKind.Wall, door.position + Vec3.Up * 3f, new Vec3(w, 6f, w)));
                    var nav = new MapNav(blocked, rules, false);
                    if (float.IsInfinity(CellTime(nav, nav.TravelFrom(home), mid))) e.Add("with " + door.name + " blocked, team " + team + "'s base has no way out");
                    // The doorway really is a way in: open, it is on the floor.
                    if (Math.Abs(MapValidator.SurfaceAt(map, door.position.x, door.position.z, false) - door.position.y) > 0.05f) e.Add("entrance " + door.name + " is not on open floor");
                }
            }
        }

        static MapDef Clone(MapDef map)
        {
            var c = map.ShallowCopy();
            c.blocks = new List<MapBlock>(map.blocks);
            return c;
        }

        /// <summary>Flag Rush: a capture zone in each base, mirror twins, and flag spots the same walk from both bases.</summary>
        static void CheckFlagRush(MapDef map, MapNav nav, MapNav navShifted, LaneMapCheckSettings s, LaneMapReport report)
        {
            var e = report.errors;
            var match = map.ToMatchMap();
            var bases = match.ZoneSet("bases");
            if (bases == null || bases.zones.Count != 2) { e.Add("Flag Rush needs a 'bases' set of two capture zones, one in each base"); return; }
            foreach (var problem in ModeDef.FlagRush().Validate(match)) e.Add("flag_rush: " + problem);
            foreach (var z in map.zones)
            {
                bool found = false;
                foreach (var o in map.zones)
                    if (o.set == z.set && Near(o.center, MapDef.Mirror(z.center), s.symmetryTolerance) && Math.Abs(o.radius - z.radius) <= s.symmetryTolerance &&
                        o.startOwner == z.startOwner.Other() && o.forwardSpawnFor == z.forwardSpawnFor.Other())
                        found = true;
                if (!found) e.Add("zone " + z.label + " has no mirror twin (with the teams swapped)");
            }
            MapZone baseA = null;
            foreach (var z in map.zones) if (z.set == "bases" && z.startOwner == TeamId.A) baseA = z;
            if (baseA == null) { e.Add("Flag Rush: team A's base has no capture zone"); return; }
            if (map.flagSpots.Count < s.minFlagSpots) e.Add("Flag Rush needs at least " + s.minFlagSpots + " flag spots");
            // One field from A's base per state; mirrored, B's walk to a spot is A's walk to the spot's twin.
            var fromA = new[] { nav.TravelFrom(baseA.center), navShifted.TravelFrom(baseA.center) };
            var navs = new[] { nav, navShifted };
            foreach (var spot in map.flagSpots)
            {
                bool twin = false;
                foreach (var o in map.flagSpots) if (Near(o, MapDef.Mirror(spot), s.symmetryTolerance)) twin = true;
                if (!twin) e.Add("flag spot " + spot + " has no mirror twin");
                OnFloor(map, "flag spot", spot, e);
                var row = new FlagSpotRow { spot = spot };
                var m = MapDef.Mirror(spot);
                row.a = CellTime(navs[0], fromA[0], spot); row.b = CellTime(navs[0], fromA[0], m);
                row.aShifted = CellTime(navs[1], fromA[1], spot); row.bShifted = CellTime(navs[1], fromA[1], m);
                report.flagSpots.Add(row);
                if (float.IsInfinity(row.a) || float.IsInfinity(row.b) || float.IsInfinity(row.aShifted) || float.IsInfinity(row.bShifted))
                    e.Add("flag spot " + spot + " can't be walked to from both bases, before and after the shift");
                else if (row.Skew > s.flagFairness)
                    e.Add("flag spot " + spot + " is " + (row.Skew * 100f).ToString("0.0") + "% closer to one base (at most " + (s.flagFairness * 100f).ToString("0") + "%)");
            }
        }

        static void CheckWalks(MapDef map, MapNav nav, LaneMapCheckSettings s, LaneMapReport report)
        {
            Vec3 Centre(List<SpawnPoint> spots) { var c = Vec3.Zero; foreach (var p in spots) c += p.position; return c / spots.Count; }
            var fa = nav.TravelFrom(Centre(map.spawnsA));
            var fb = nav.TravelFrom(Centre(map.spawnsB));
            var lane = map.lanes[0];
            void Row(string label, Vec3 forA, Vec3 forB)
            {
                float a = CellTime(nav, fa, forA), b = CellTime(nav, fb, forB);
                report.walks.Add((label, a, b));
                if (float.IsInfinity(a) || float.IsInfinity(b)) report.errors.Add("a team can't walk to " + label);
                else if (Math.Abs(a - b) > s.travelTolerance) report.errors.Add("team A walks " + a.ToString("0.0") + " s to " + label + " but team B walks " + b.ToString("0.0") + " s");
            }
            Row("mid-lane", lane.PointAt(lane.Length * 0.5f), lane.PointAt(lane.Length * 0.5f));
            foreach (var tier in new[] { BastionTier.Forward, BastionTier.Core })
            {
                Row("own " + tier + " Bastion", map.Bastion(TeamId.A, tier).position, map.Bastion(TeamId.B, tier).position);
                Row("enemy " + tier + " Bastion", map.Bastion(TeamId.B, tier).position, map.Bastion(TeamId.A, tier).position);
            }
            foreach (var c in map.camps)
            {
                if (c.half == TeamId.B) continue;
                var twin = c.half == TeamId.None ? c : map.camps.Find(o => o.kind == c.kind && o.half == TeamId.B);
                if (twin == null) continue;
                Row(c.half == TeamId.None ? c.kind + " camp" : "own " + c.kind + " camp", c.pad, twin.pad);
                if (c.half != TeamId.None) Row("enemy " + c.kind + " camp", twin.pad, c.pad);
            }
        }
    }
}
