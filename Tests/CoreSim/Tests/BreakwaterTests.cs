using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DuckAndCover.Core;

/// <summary>Breakwater (WP18): the lane map's data, its validator, and launch pads in the movement check.</summary>
static class BreakwaterTests
{
    const int A1 = CombatHarness.A1;

    [Test]
    static void BreakwaterPassesValidator()
    {
        var report = LaneMapValidator.Check(Breakwater.Build());
        foreach (var e in report.errors) Check.Fail(e);
        Check.True(report.Ok, "Breakwater is valid");
        if (!report.Ok) Console.WriteLine(report.Table());
    }

    [Test]
    static void BreakwaterIsInTheCatalogAndLaidOutAsPlanned()
    {
        var map = MapCatalog.ById(Breakwater.Id);
        Check.True(map != null && map.IsLaneMap, "found by id, with a lane");
        Check.Equal(MapSymmetry.MirrorZ, map.symmetry, "mirrored end to end");
        Check.True(map.halfX * 2f == 120f && map.halfZ * 2f == 210f, "120 m by 210 m (widened Nov 18)");
        var report = LaneMapValidator.Check(map);
        Check.True(report.laneLength > 230f && report.laneLength < 260f, "lane about 245 m base to base (" + report.laneLength.ToString("0") + ")");
        Check.True(report.longestSight <= report.laneLength / 3f, "no sight line down more than a third of it");
        Check.Equal(1, map.lanes.Count, "one lane");
        Check.Equal(7, map.camps.Count, "seven camps");
        Check.Equal(1, map.camps.Count(c => c.kind == CampKind.Juggernaut), "one Juggernaut camp");
        foreach (var kind in new[] { CampKind.Damage, CampKind.Experience, CampKind.Speed })
        {
            Check.Equal(1, map.camps.Count(c => c.kind == kind && c.half == TeamId.A), "a " + kind + " camp in A's half");
            Check.Equal(1, map.camps.Count(c => c.kind == kind && c.half == TeamId.B), "a " + kind + " camp in B's half");
        }
        Check.Equal(4, map.bastions.Count, "a Forward and a Core Bastion per team");
        Check.Equal(2, map.launchPads.Count, "a launch pad per base");
        Check.True(map.camps.First(c => c.kind == CampKind.Juggernaut).pad.y >= 3.5f, "the Juggernaut camp is raised");
        // The short jungle is west of the lane, the long one east, as in the sketch.
        Check.True(map.routes.Any(r => r.kind == RouteKind.Jungle && r.points.All(p => p.x < 0f)), "short jungle on the west");
        Check.True(map.routes.Any(r => r.kind == RouteKind.Jungle && r.points.Skip(1).Take(r.points.Count - 2).All(p => p.x > 0f)), "long jungle on the east");
        var walk = report.walks.ToDictionary(w => w.label, w => w.a);
        Check.True(walk["own Core Bastion"] < walk["own Forward Bastion"] && walk["own Forward Bastion"] < walk["mid-lane"], "Core, then Forward, then the middle");
        // Walking pace 5.2 m/s; a sprint is faster. The plan: about 28 s base to base at a sprint, about 32 s since
        // the Nov 18 widening stretched the zigzag.
        float sprintBaseToBase = report.laneLength / Ruleset.Default.sprintSpeed;
        Check.True(sprintBaseToBase > 26f && sprintBaseToBase < 35f, "base to base at a sprint: " + sprintBaseToBase.ToString("0") + " s");
        // The walls along the lane are tree lines and rocks (the lane look), and solid.
        Check.True(map.blocks.Count(b => b.style == "treeline" && b.Solid) > 50, "tree line walls");
        Check.True(map.blocks.Count(b => b.style == "rocks" && b.Solid) > 20, "rock walls");
        Check.True(map.paths.Any(p => p.style == "lane") && map.paths.Count(p => p.style == "trail") >= map.routes.Count, "the lane and every route are painted");
    }

    [Test]
    static void FlagRushFitsBreakwater()
    {
        var map = Breakwater.Build();
        var match = map.ToMatchMap();
        Check.Equal(0, ModeDef.FlagRush().Validate(match).Count, "Flag Rush fits the map");
        Check.Equal(4, match.flagSpots.Count, "four flag spots reach the rules");
        var report = LaneMapValidator.Check(map);
        Check.Equal(4, report.flagSpots.Count, "every spot walked");
        foreach (var f in report.flagSpots) Check.True(f.Skew <= 0.05f, "spot " + f.spot + " is fair (" + (f.Skew * 100f).ToString("0.0") + "%)");
        var baseA = map.Zone(new ZoneId(Breakwater.BaseA));
        Check.True(baseA != null && baseA.startOwner == TeamId.A && baseA.center.z < -80f, "team A's capture zone is in A's base");
        Check.True(map.entrances.Count(x => x.team == TeamId.A) >= 2, "two ways into the base for a carrier");
    }

    [Test]
    static void LedgesAndTheShelfAreOneWay()
    {
        var report = LaneMapValidator.Check(Breakwater.Build());
        Check.Equal(3, report.drops.Count, "two ledges and the Juggernaut drop");
        foreach (var d in report.drops)
        {
            Check.True(d.down < 2.5f, d.name + ": dropping is quick (" + d.down.ToString("0.0") + " s)");
            Check.True(d.up > d.down + 5f, d.name + ": back up only the long way (" + d.up.ToString("0.0") + " s)");
        }
        // Standing in the lane under a ledge: its top is out of reach by a vault.
        var map = Breakwater.Build();
        var rules = Ruleset.Default;
        Check.True(Breakwater.LedgeHeight > rules.vaultMaxHeight + rules.jumpHeight, "ledges are taller than a jump and a vault");
        Check.True(Breakwater.ShelfHeight > rules.vaultMaxHeight + rules.jumpHeight, "so is the shelf");
    }

    // ---- The validator catches broken lane maps ----

    static void Rejects(Action<MapDef> change, string expected, string what)
    {
        var m = Breakwater.Build();
        change(m);
        var report = LaneMapValidator.Check(m);
        Check.False(report.Ok, what + " fails");
        Check.True(report.errors.Any(e => e.Contains(expected)), what + ": says '" + expected + "' (got: " + string.Join("; ", report.errors.Take(3)) + ")");
    }

    [Test]
    static void LaneValidatorCatchesBrokenSymmetry()
    {
        Rejects(m => m.Block("LaneLowZigA").center += new Vec3(1f, 0f, 0f), "no mirror twin", "a moved low wall");
        Rejects(m => m.Block("ShelfStairsB").rampRisesToward = 0, "no mirror twin", "stairs facing the wrong way");
        Rejects(m => m.camps.First(c => c.kind == CampKind.Speed && c.half == TeamId.B).pad += new Vec3(1f, 0f, 0f), "no mirror twin", "a moved camp");
        Rejects(m => m.bastions.First(b => b.team == TeamId.B && b.tier == BastionTier.Forward).position += new Vec3(0f, 0f, 2f), "no mirror twin", "a moved Bastion");
        Rejects(m => m.launchPads[1].apexHeight = 10f, "no mirror twin", "a lower launch arc for one side");
        Rejects(m => m.symmetry = MapSymmetry.HalfTurn, "MirrorZ", "a lane map turned instead of mirrored");
    }

    [Test]
    static void LaneValidatorCatchesUnfairOrBrokenLayouts()
    {
        // A wall across the lane: minions can't get through, and the base can't be reached along it.
        Rejects(m =>
        {
            m.blocks.Add(new MapBlock("PlugA", BlockKind.Wall, new Vec3(-18f, 3f, -75f), new Vec3(18f, 6f, 1f)));
            m.blocks.Add(new MapBlock("PlugB", BlockKind.Wall, new Vec3(-18f, 3f, 75f), new Vec3(18f, 6f, 1f)));
        }, "minions can't", "a blocked lane");
        // Straighten the lane's middle: a long view straight down it.
        Rejects(m =>
        {
            m.blocks.RemoveAll(b => b.name.StartsWith("Jungle") && b.center.x > -5f && b.center.x < 30f && Math.Abs(b.center.z) < 45f);
        }, "down the lane", "the zigzag's walls cut away");
        // Rails gone and the shelf walls lowered: the lane sees the Juggernaut camp.
        Rejects(m =>
        {
            m.blocks.RemoveAll(b => b.name.StartsWith("ShelfRail") || b.name.StartsWith("ShelfRock") || b.name.StartsWith("ShelfScreen") || b.name.StartsWith("PocketCover"));
            foreach (var b in m.blocks.Where(b => b.name.StartsWith("Jungle") && b.center.x < 0f && b.center.x > -15f * Breakwater.StretchX && Math.Abs(b.center.z) < 16f)) { b.size = new Vec3(b.size.x, 1f, b.size.z); b.center = new Vec3(b.center.x, 0.5f, b.center.z); }
            var shelf = m.blocks.First(b => b.name == "Shelf");
            shelf.size = new Vec3(shelf.size.x, 1f, shelf.size.z);
            shelf.center = new Vec3(shelf.center.x, 0.5f, shelf.center.z);
            foreach (var c in m.camps.Where(c => c.kind == CampKind.Juggernaut)) { c.pad = new Vec3(c.pad.x, 1f, c.pad.z); for (int i = 0; i < c.guardSpots.Count; i++) c.guardSpots[i] = new Vec3(c.guardSpots[i].x, 1f, c.guardSpots[i].z); }
        }, "Juggernaut camp can be seen", "an open shelf");
        // Stairs from the lane straight up the ledge: it can be climbed back.
        Rejects(m =>
        {
            m.blocks.Add(new MapBlock("CheatStairsA", BlockKind.Ramp, new Vec3(-24.75f, 1.5f, -52f), new Vec3(3f, 3f, 4f), ShiftRole.None, 270));
            m.blocks.Add(new MapBlock("CheatStairsB", BlockKind.Ramp, new Vec3(-24.75f, 1.5f, 52f), new Vec3(3f, 3f, 4f), ShiftRole.None, 270));
        }, "can be climbed back up", "stairs up a ledge from the lane");
        // The side gate never shut: the side route is open from the start.
        Rejects(m => m.blocks.RemoveAll(b => b.kind == BlockKind.Shutter), "no shift", "no side gate");
        Rejects(m =>
        {
            m.blocks.Add(new MapBlock("BehindGateA", BlockKind.Wall, new Vec3(-44.75f, 3f, -84f), new Vec3(2f, 6f, 12f)));
            m.blocks.Add(new MapBlock("BehindGateB", BlockKind.Wall, new Vec3(-44.75f, 3f, 84f), new Vec3(2f, 6f, 12f)));
        }, "something is in the way", "a side route walled off behind its gate");
        // The jungle door walled up: one way into the base.
        Rejects(m =>
        {
            foreach (var z in new[] { -86f, 86f }) m.blocks.Add(new MapBlock("Door" + z, BlockKind.Wall, new Vec3(7.5f, 3f, z), new Vec3(2f, 6f, 12f)));
        }, "has no way out", "a base with one way in");
        // A launch pad aimed into the jungle wall.
        Rejects(m => { m.launchPads[0].apexHeight = 2f; m.launchPads[1].apexHeight = 2f; }, "arc hits", "a launch arc too low to clear the walls");
        Rejects(m => { m.launchPads[0].landing = new Vec3(-18f, 0f, -70f); m.launchPads[1].landing = new Vec3(-18f, 0f, 70f); }, "middle of the lane", "a pad that lands by its own base");
        Rejects(m => m.camps.RemoveAll(c => c.kind == CampKind.Speed), "Speed camp", "no speed camps");
        Rejects(m => { m.flagSpots.Clear(); m.flagSpots.Add(new Vec3(12f, 0f, -30f)); m.flagSpots.Add(new Vec3(12f, 0f, 30f)); }, "closer to one base", "flag spots nearer one base");
        Rejects(m => m.zones.RemoveAll(z => z.set == "bases"), "'bases'", "no Flag Rush capture zones");
        Rejects(m => { foreach (var r in m.routes.Where(r => r.kind == RouteKind.Branch)) r.points.RemoveAt(2); }, "must end at a camp", "a branch that stops short of its camp");
    }

    // ---- Launch pads in the movement check ----

    static LaunchPad PadAt(CombatHarness h, int id, float ahead)
    {
        var start = h.M(id).position;
        var pad = new LaunchPad("TestPad", TeamId.A, start + Vec3.Forward * ahead, start + new Vec3(10f, 0f, 60f), 12f, 2.5f, 1f);
        h.Combat.Movement.Layout.launchPads.Add(pad);
        return pad;
    }

    [Test]
    static void LaunchPadArcIsAcceptedAndLands()
    {
        var h = new CombatHarness();
        var pad = PadAt(h, A1, 2f);
        Check.True(h.Move(A1, pad.center).IsAccepted, "step onto the pad");
        Check.True(h.Combat.Movement.Flying(A1), "launched");
        Check.True(h.Of<LaunchedEvent>().Any(e => e.playerId == A1 && e.pad == "TestPad"), "everyone hears about it");
        // Fly the arc a tick at a time: 60 m in 2.5 s and 12 m up, far beyond a sprint or a jump.
        int ticks = (int)Math.Ceiling(pad.seconds / CombatHarness.Dt);
        for (int i = 1; i <= ticks; i++)
        {
            h.Tick();
            var p = pad.PointAt(i * CombatHarness.Dt);
            var cmd = new MoveCommand { playerId = A1, position = p, placement = h.M(A1).placement, grounded = i == ticks };
            var r = h.Combat.Apply(cmd);
            if (!r.IsAccepted) { Check.Fail("arc step " + i + " refused: " + r.reason); break; }
        }
        Check.True(Vec3.Distance(h.M(A1).position, pad.landing) < 0.01f, "the server has the player at the landing");
        Check.False(h.Combat.Movement.Flying(A1), "landed");
        Check.Equal(0, h.Of<PlayerPlacedEvent>().Count(e => e.correction), "no corrections on the way");
        // Back on foot, the speed check is as strict as ever.
        h.Tick();
        var cheat = h.Move(A1, h.M(A1).position + Vec3.Forward * 20f);
        Check.False(cheat.IsAccepted, "20 m in a step after landing is refused");
    }

    [Test]
    static void LaunchPadCantBeUsedToGoElsewhere()
    {
        var h = new CombatHarness();
        var pad = PadAt(h, A1, 2f);
        Check.True(h.Move(A1, pad.center).IsAccepted, "step onto the pad");
        h.Tick();
        var sideways = h.Move(A1, pad.PointAt(CombatHarness.Dt) + new Vec3(8f, 0f, 0f));
        Check.False(sideways.IsAccepted, "8 m off the arc is refused");
        Check.True(sideways.reason.Contains("arc"), "and says why: " + sideways.reason);
        Check.False(h.Combat.Movement.Flying(A1), "the correction ends the flight");

        // Skipping straight to the landing at take-off is too far ahead of the server's clock.
        var h2 = new CombatHarness();
        var pad2 = PadAt(h2, A1, 2f);
        Check.True(h2.Move(A1, pad2.center).IsAccepted, "step onto the pad");
        h2.Tick();
        Check.False(h2.Move(A1, pad2.landing).IsAccepted, "jumping to the landing at once is refused");

        // A flight nobody finishes ends on its own, and the speed check comes back.
        var h3 = new CombatHarness();
        var pad3 = PadAt(h3, A1, 2f);
        Check.True(h3.Move(A1, pad3.center).IsAccepted, "step onto the pad");
        h3.Run(pad3.seconds + h3.R.launchGraceSeconds + 0.2f);
        Check.False(h3.Combat.Movement.Flying(A1), "the flight timed out");
        Check.False(h3.Move(A1, pad3.landing).IsAccepted, "and the landing is too far to walk to in a step");
    }

    [Test]
    static void BreakwaterPadsFlyClearIntoTheMovementCheck()
    {
        var map = Breakwater.Build();
        var layout = map.ToLayout();
        Check.Equal(2, layout.launchPads.Count, "the map gives the movement rules its pads");
        var a = map.Pad("LaunchA");
        Check.True(a != null && a.team == TeamId.A, "team A's pad");
        Check.True(a.PointAt(a.seconds * 0.5f).y > Breakwater.FillHeight + 5f, "the arc goes well over the jungle walls");
        Check.True(Vec3.Distance(a.PointAt(0f), a.center) < 1e-3f && Vec3.Distance(a.PointAt(a.seconds), a.landing) < 1e-3f, "from the pad to the landing");
        float speed = a.FlatLength / a.seconds;
        Check.True(speed > Ruleset.Default.sprintSpeed * 2f, "much faster than running (" + speed.ToString("0") + " m/s)");
    }

    // ---- Report: dotnet run --project Tests/CoreSim -- report breakwater [out.svg] ----

    public static int Report(string[] args)
    {
        var map = Breakwater.Build();
        var report = LaneMapValidator.Check(map);
        Console.WriteLine(map.displayName + " (" + map.id + "): " + (report.Ok ? "valid" : report.errors.Count + " problems") + ", " + map.blocks.Count + " blocks");
        foreach (var e in report.errors) Console.WriteLine("  " + e);
        Console.WriteLine(report.Table());
        Console.WriteLine(ClearWidths(map));
        if (args.Length > 0)
        {
            File.WriteAllText(args[0], Svg(map, report));
            Console.WriteLine("Wrote " + args[0]);
        }
        return report.Ok ? 0 : 1;
    }

    /// <summary>
    /// How wide the lane and the jungle trails really are between walls (Nov 18 widening): every 2 m along each
    /// centre line, the free width at chest height across it (cover a hero can't see over counts as wall).
    /// Prints the narrowest tenth and the median per path.
    /// </summary>
    public static string ClearWidths(MapDef map)
    {
        var sb = new StringBuilder("Clear width between walls, chest height (10th percentile / median, metres)\n");
        void Measure(string label, List<Vec3> pts)
        {
            var art = new ArtPath("measure", 1f) { points = pts };
            var widths = new List<float>();
            float len = 0f;
            for (int i = 1; i < pts.Count; i++) len += Vec3.Distance(pts[i - 1], pts[i]);
            for (float d = 1f; d < len - 1f; d += 2f)
            {
                // Walk the polyline to d.
                float left = d; Vec3 at = pts[0], dir = Vec3.Forward;
                for (int i = 1; i < pts.Count; i++)
                {
                    float seg = Vec3.Distance(pts[i - 1], pts[i]);
                    if (left <= seg) { dir = (pts[i] - pts[i - 1]) * (1f / seg); at = pts[i - 1] + dir * left; break; }
                    left -= seg;
                }
                var side = new Vec3(dir.z, 0f, -dir.x);
                var chest = new Vec3(at.x, MapValidator.SurfaceAt(map, at.x, at.z, false) + 1.3f, at.z);
                float Reach(Vec3 s1)
                {
                    float lo = 0f, hi = 40f;
                    if (MapValidator.Clear(map, chest, chest + s1 * hi, false)) return hi;
                    for (int k = 0; k < 14; k++) { float mid = (lo + hi) * 0.5f; if (MapValidator.Clear(map, chest, chest + s1 * mid, false)) lo = mid; else hi = mid; }
                    return lo;
                }
                float w = Reach(side) + Reach(side * -1f);
                if (w > 0.5f) widths.Add(w); // 0: the sample sits inside a ramp or rail, not on the trail
            }
            widths.Sort();
            if (widths.Count == 0) return;
            sb.Append("  ").Append(label.PadRight(22)).Append(widths[widths.Count / 10].ToString("0.0")).Append(" / ").Append(widths[widths.Count / 2].ToString("0.0")).Append('\n');
        }
        foreach (var lane in map.lanes) Measure("lane", lane.points);
        foreach (var r in map.routes) Measure(r.name, r.points);
        return sb.ToString();
    }

    /// <summary>A top-down plan: blocks by kind and height, the lane, routes, Bastions, camps, launch arcs and drops.</summary>
    public static string Svg(MapDef map, LaneMapReport report)
    {
        const float S = 6f; // pixels per metre
        float W = map.halfX * 2f * S, H = map.halfZ * 2f * S;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        // Team A's end at the bottom of the picture: +z is up.
        string X(float x) => ((x + map.halfX) * S).ToString("0.#", inv);
        string Y(float z) => ((map.halfZ - z) * S).ToString("0.#", inv);
        string N(float v) => (v * S).ToString("0.#", inv);
        var sb = new StringBuilder();
        sb.Append("<svg xmlns='http://www.w3.org/2000/svg' width='").Append((W + 300).ToString(inv)).Append("' height='").Append(H.ToString(inv)).Append("' font-family='sans-serif'>");
        sb.Append("<rect width='100%' height='100%' fill='#1d2026'/>");
        string Fill(MapBlock b)
        {
            if (b.style == "treeline") return "#2f6b3a";
            if (b.style == "rocks" && b.kind == BlockKind.Wall) return "#575b63";
            switch (b.kind)
            {
                case BlockKind.Ground: return "#8a7a5c";
                case BlockKind.Wall: return b.size.y >= 5f ? "#2b2f38" : "#c9bfa6";
                case BlockKind.LowCover: return "#e8c447";
                case BlockKind.Crate: return "#4f9e5c";
                case BlockKind.Platform: return "#8a6cb3";
                case BlockKind.Ramp: return "#b49ad6";
                case BlockKind.Shutter: return "#e0503c";
                default: return b.center.z < 0 ? "#3d99f2" : "#f28c33";
            }
        }
        void Poly(IEnumerable<Vec3> pts, string col, float width, string extra = "")
        {
            sb.Append("<polyline fill='none' stroke='").Append(col).Append("' stroke-width='").Append(N(width)).Append("' stroke-linejoin='round' stroke-linecap='round' ").Append(extra).Append(" points='");
            foreach (var p in pts) sb.Append(X(p.x)).Append(',').Append(Y(p.z)).Append(' ');
            sb.Append("'/>");
        }
        sb.Append("<rect x='0' y='0' width='").Append(W.ToString(inv)).Append("' height='").Append(H.ToString(inv)).Append("' fill='#8a7a5c'/>");
        foreach (var lane in map.lanes) Poly(lane.points, "#b8996a", map.laneWidth);
        foreach (var r in map.routes) Poly(r.points, r.kind == RouteKind.SideRoute ? "#a0644f" : "#7c8f52", r.width);
        foreach (var b in map.blocks.Where(b => b.kind != BlockKind.Ground).OrderBy(b => b.kind == BlockKind.Decal ? 0 : 1).ThenBy(b => b.center.y))
            sb.Append("<rect x='").Append(X(b.Min.x)).Append("' y='").Append(Y(b.Max.z)).Append("' width='").Append(N(b.size.x)).Append("' height='").Append(N(b.size.z))
              .Append("' fill='").Append(Fill(b)).Append("' stroke='#000' stroke-opacity='0.25'/>");
        foreach (var lane in map.lanes) Poly(lane.points, "#ffffff", 0.3f, "stroke-dasharray='6 6' stroke-opacity='0.8'");
        foreach (var c in map.camps)
        {
            string col = c.kind == CampKind.Juggernaut ? "#4aa3ff" : c.kind == CampKind.Damage ? "#ff4a4a" : c.kind == CampKind.Experience ? "#ff9ad5" : "#7dffb0";
            sb.Append("<circle cx='").Append(X(c.pad.x)).Append("' cy='").Append(Y(c.pad.z)).Append("' r='").Append(N(c.padRadius))
              .Append("' fill='").Append(col).Append("' fill-opacity='0.35' stroke='").Append(col).Append("' stroke-width='2'/>");
            string label = c.kind == CampKind.Juggernaut ? "JUG" : c.kind == CampKind.Damage ? "DMG" : c.kind == CampKind.Experience ? "XP" : "SPD";
            sb.Append("<text x='").Append(X(c.pad.x)).Append("' y='").Append(Y(c.pad.z)).Append("' fill='#fff' font-size='13' font-weight='bold' text-anchor='middle' dy='5'>").Append(label).Append("</text>");
        }
        foreach (var b in map.bastions)
        {
            string col = b.team == TeamId.A ? "#3d99f2" : "#f28c33";
            sb.Append("<rect x='").Append(X(b.position.x - b.radius)).Append("' y='").Append(Y(b.position.z + b.radius)).Append("' width='").Append(N(b.radius * 2f)).Append("' height='").Append(N(b.radius * 2f))
              .Append("' fill='").Append(col).Append("' stroke='#fff' stroke-width='2' transform='rotate(45 ").Append(X(b.position.x)).Append(' ').Append(Y(b.position.z)).Append(")'/>");
            sb.Append("<text x='").Append(X(b.position.x + b.radius + 1.5f)).Append("' y='").Append(Y(b.position.z)).Append("' fill='#fff' font-size='12' dy='4'>").Append(b.tier).Append("</text>");
        }
        foreach (var p in map.launchPads)
        {
            var pts = new List<Vec3>();
            for (int k = 0; k <= 30; k++) pts.Add(p.PointAt(p.seconds * k / 30f));
            Poly(pts, "#ffe14d", 0.4f, "stroke-dasharray='5 4'");
            sb.Append("<circle cx='").Append(X(p.center.x)).Append("' cy='").Append(Y(p.center.z)).Append("' r='").Append(N(p.radius)).Append("' fill='#ffe14d'/>");
            sb.Append("<circle cx='").Append(X(p.landing.x)).Append("' cy='").Append(Y(p.landing.z)).Append("' r='").Append(N(1f)).Append("' fill='none' stroke='#ffe14d' stroke-width='2'/>");
        }
        foreach (var d in map.drops)
        {
            Poly(new[] { d.top, d.bottom }, "#ff6b5b", 0.5f);
            sb.Append("<circle cx='").Append(X(d.bottom.x)).Append("' cy='").Append(Y(d.bottom.z)).Append("' r='4' fill='#ff6b5b'/>");
        }
        foreach (var p in map.spawnsA) sb.Append("<circle cx='").Append(X(p.position.x)).Append("' cy='").Append(Y(p.position.z)).Append("' r='4' fill='#3d99f2' stroke='#fff'/>");
        foreach (var p in map.spawnsB) sb.Append("<circle cx='").Append(X(p.position.x)).Append("' cy='").Append(Y(p.position.z)).Append("' r='4' fill='#f28c33' stroke='#fff'/>");
        foreach (var z in map.zones)
            sb.Append("<circle cx='").Append(X(z.center.x)).Append("' cy='").Append(Y(z.center.z)).Append("' r='").Append(N(z.radius))
              .Append("' fill='none' stroke='").Append(z.startOwner == TeamId.A ? "#3d99f2" : "#f28c33").Append("' stroke-width='2' stroke-dasharray='6 4'/>");
        foreach (var f in map.flagSpots)
            sb.Append("<circle cx='").Append(X(f.x)).Append("' cy='").Append(Y(f.z)).Append("' r='6' fill='#ffe14d' stroke='#000' stroke-width='2'/>");
        foreach (var x in map.entrances) sb.Append("<circle cx='").Append(X(x.position.x)).Append("' cy='").Append(Y(x.position.z)).Append("' r='5' fill='none' stroke='#fff' stroke-dasharray='3 2'/>");

        // Legend and table.
        float lx = W + 14, ly = 26;
        void Line(string text, string col = "#e6e6e6", int size = 14) { sb.Append("<text x='").Append(lx.ToString(inv)).Append("' y='").Append(ly.ToString(inv)).Append("' fill='").Append(col).Append("' font-size='").Append(size).Append("'>").Append(System.Security.SecurityElement.Escape(text)).Append("</text>"); ly += size + 6; }
        Line(map.displayName, "#ffffff", 22);
        Line("Team A (blue) at the bottom", "#3d99f2");
        Line("Team B (orange) at the top", "#f28c33");
        Line("Lane (dashed centre line)", "#b8996a");
        Line("Jungle routes / side route", "#7c8f52");
        Line("Tree line walls", "#4f9a5c"); Line("Rock walls", "#8d929b");
        Line("Ledges, shelf / stairs", "#b49ad6");
        Line("Low cover", "#e8c447"); Line("Side gate (opens at shift)", "#e0503c");
        Line("Bastions: Forward, Core", "#ffffff");
        Line("Camps: JUG, DMG, XP, SPD", "#ffffff");
        Line("Launch pad and arc", "#ffe14d"); Line("One-way drops (dot: landing)", "#ff6b5b");
        Line("Base entrances: dashed rings", "#ffffff");
        Line("Flag Rush: flag spots, base zones", "#ffe14d");
        ly += 10;
        Line("Lane " + report.laneLength.ToString("0") + " m; longest sight " + report.longestSight.ToString("0") + " m", "#ffffff", 14);
        Line("Walk seconds from spawn (A / B)", "#ffffff", 14);
        foreach (var w in report.walks) Line(w.label + ": " + w.a.ToString("0.0") + " / " + w.b.ToString("0.0"), "#e6e6e6", 12);
        Line("Drops: down / back up (s)", "#ffffff", 14);
        foreach (var d in report.drops) Line(d.name + ": " + d.down.ToString("0.0") + " / " + (float.IsInfinity(d.up) ? "none" : d.up.ToString("0.0")), "#e6e6e6", 12);
        sb.Append("</svg>");
        return sb.ToString();
    }
}
