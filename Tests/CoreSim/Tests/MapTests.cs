using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DuckAndCover.Core;

/// <summary>A world for map tests: no walls for shots, zones answered by the map from where players stand.</summary>
sealed class MapZoneWorld : IWorldQuery
{
    public MapDef Map;
    public Func<IEnumerable<PlayerMotion>> Motions;
    public int[] PlayersInZone(ZoneId zone) => Map.PlayersInZone(zone, Motions == null ? null : Motions());
    public int[] PlayersInSphere(Vec3 center, float radius, int tick) => Array.Empty<int>();
    public bool Raycast(Vec3 origin, Vec3 dir, float max, int tick, out HitInfo hit) { hit = HitInfo.Miss; return false; }
    public SurfaceCell CellAt(Vec3 point) => SurfaceCell.None;
}

/// <summary>The first map (WP9): its data, the validator, and both modes played on it.</summary>
static class MapTests
{
    [Test]
    static void FirstMapPassesValidator()
    {
        var report = MapValidator.Check(MapCatalog.Default);
        foreach (var e in report.errors) Check.Fail(e);
        Check.True(report.Ok, "Gantry Yard is valid");
        if (!report.Ok) Console.WriteLine(report.Table());
    }

    [Test]
    static void MapKeepsTheZoneIdsTheRulesUse()
    {
        var map = MapCatalog.Default.ToMatchMap();
        Check.Equal(0, map.Validate().Count, "objective layout is valid");
        Check.Equal("1,2,3", string.Join(",", map.ZoneSet("points").zones.Select(z => z.id.value)), "points A, middle, B");
        Check.Equal("11,12,13,14", string.Join(",", map.ZoneSet("hills").zones.Select(z => z.id.value)), "four hills");
        Check.Equal(TeamId.A, map.ZoneSet("points").zones[0].forwardSpawnFor, "A spawns forward at point A");
        Check.Equal(TeamId.B, map.ZoneSet("points").zones[2].forwardSpawnFor, "B spawns forward at point B");
        Check.Equal(0, ModeDef.CaptureAndHold().Validate(map).Count, "capture and hold fits");
        Check.Equal(0, ModeDef.KingOfTheHill().Validate(map).Count, "king of the hill fits");
        Check.True(MapCatalog.ById(GantryYard.Id) != null, "found by id");
        Check.True(MapCatalog.ById("nowhere") == null, "unknown id");
    }

    [Test]
    static void ZonesAreStandingCylinders()
    {
        var map = MapCatalog.Default;
        var mid = map.Zone(new ZoneId(GantryYard.PointMid));
        var motions = new[]
        {
            new PlayerMotion { playerId = 7, position = new Vec3(mid.radius - 0.1f, 0f, 0f) },
            new PlayerMotion { playerId = 3, position = new Vec3(0f, 0f, -(mid.radius - 0.1f)) },
            new PlayerMotion { playerId = 4, position = new Vec3(mid.radius + 0.1f, 0f, 0f) },
            new PlayerMotion { playerId = 5, position = new Vec3(1f, mid.height + 0.5f, 1f) },
            new PlayerMotion { playerId = 6, position = new Vec3(1f, mid.height - 0.5f, 1f) },
        };
        Check.Equal("3,6,7", string.Join(",", map.PlayersInZone(mid.id, motions)), "inside the ring and below the top, in id order");
        Check.Equal(0, map.PlayersInZone(new ZoneId(99), motions).Length, "unknown zone");
    }

    [Test]
    static void RampsClimbTheWayTheyFace()
    {
        var ramp = new MapBlock("r", BlockKind.Ramp, new Vec3(0f, 1f, 0f), new Vec3(4f, 2f, 8f), ShiftRole.None, 0);
        Check.Near(0f, ramp.TopAt(0f, -4f), "low end at -z");
        Check.Near(1f, ramp.TopAt(0f, 0f), "halfway");
        Check.Near(2f, ramp.TopAt(0f, 4f), "top at +z");
        var turned = MapDef.Turn(ramp, "t");
        Check.Equal(180, turned.rampRisesToward, "turned ramp faces the other way");
        Check.Near(2f, turned.TopAt(0f, -4f), "turned: top at -z");
        var wall = new MapBlock("w", BlockKind.Wall, new Vec3(0f, 2f, 0f), new Vec3(1f, 4f, 1f));
        Check.Near(4f, wall.TopAt(0f, 0f), "a box's top is flat");
    }

    [Test]
    static void ShiftRemovesTheShutters()
    {
        var map = MapCatalog.Default;
        var shutters = map.blocks.Where(b => b.shift == ShiftRole.RemovedOnShift).ToList();
        Check.Equal(2, shutters.Count, "one shutter per hall");
        foreach (var s in shutters)
        {
            Check.True(s.Present(false), s.name + " shut before the shift");
            Check.False(s.Present(true), s.name + " gone after it");
        }
        var report = MapValidator.Check(map);
        Check.True(report.pointToMidAShifted < report.pointToMidA - 1.5f, "A's point is closer to the middle after the shift");
        Check.Near(report.pointToMidA, report.pointToMidB, "both teams gain the same", 0.25f);
    }

    [Test]
    static void TeamsWalkTheSameToMatchingZones()
    {
        var report = MapValidator.Check(MapCatalog.Default);
        var row = report.fromSpawns.ToDictionary(r => r.zone.value);
        Check.Near(row[GantryYard.PointA].a, row[GantryYard.PointB].b, "own point", 0.25f);
        Check.Near(row[GantryYard.PointB].a, row[GantryYard.PointA].b, "enemy point", 0.25f);
        Check.Near(row[GantryYard.PointMid].a, row[GantryYard.PointMid].b, "middle", 0.25f);
        Check.True(row[GantryYard.PointA].a < row[GantryYard.PointMid].a, "own point is nearer than the middle");
        Check.True(row[GantryYard.PointMid].a < row[GantryYard.PointB].a, "middle is nearer than the enemy point");
        // Spawn to the middle in about 15 to 25 seconds of walking: long enough for fights on the way, short enough to matter.
        Check.True(row[GantryYard.PointMid].a > 12f && row[GantryYard.PointMid].a < 26f, "middle is " + row[GantryYard.PointMid].a.ToString("0.0") + " s from spawn");
    }

    // ---- The validator catches broken maps ----

    static MapDef Broken(Action<MapDef> change)
    {
        var m = MapCatalog.Default;
        change(m);
        return m;
    }

    static void Rejects(MapDef map, string expected, string what)
    {
        var report = MapValidator.Check(map);
        Check.False(report.Ok, what + " fails");
        Check.True(report.errors.Any(e => e.Contains(expected)), what + ": says '" + expected + "' (got: " + string.Join("; ", report.errors.Take(3)) + ")");
    }

    [Test]
    static void ValidatorCatchesBrokenSymmetry()
    {
        Rejects(Broken(m => m.Block("CentreBlockA").center += new Vec3(1f, 0f, 0f)), "no twin turned", "a moved crate");
        Rejects(Broken(m => m.Block("DeckRampB").rampRisesToward = 0), "no twin turned", "a ramp facing the wrong way");
        Rejects(Broken(m => m.zones[0].radius = 7f), "zone A has no twin", "a bigger point");
        Rejects(Broken(m => m.spawnsB[0].position = new Vec3(1f, 0f, 58f)), "has no twin in team B's room", "a moved spawn spot");
    }

    [Test]
    static void ValidatorCatchesUnfairOrBrokenLayouts()
    {
        Rejects(Broken(m => { foreach (var b in m.blocks) b.shift = ShiftRole.None; }), "no shift", "a map with no shift");
        Rejects(Broken(m =>
        {
            // Close both spawn rooms' doors: nobody gets out.
            foreach (var side in new[] { "A", "B" })
                                foreach (float x in new[] { -9.25f, 9.25f })
                    m.blocks.Add(new MapBlock("Seal" + side + x, BlockKind.Wall, side == "A" ? new Vec3(x, 2f, -55.75f) : new Vec3(-x, 2f, 55.75f), new Vec3(0.5f, 4f, 3f)));
        }), "can't walk to", "sealed spawn rooms");
        Rejects(Broken(m => m.blocks.RemoveAll(b => (b.kind == BlockKind.Wall && !b.name.StartsWith("Outer")) || b.kind == BlockKind.Crate || b.kind == BlockKind.Shutter)),
            "can see", "open spawn rooms");
        Rejects(Broken(m => m.forward.Clear()), "forward spawn with no spawn spots", "no forward spawn spots");
        Rejects(Broken(m => m.dummies.Add(new SpawnPoint(0f, 0f, 0f, 0f))), "not on open floor", "a dummy inside the pillar");
        Rejects(Broken(m => m.zones.RemoveAll(z => z.set == "hills")), "hills", "no hills");
        Rejects(Broken(m =>
        {
            // Walling off the shutters' new route on both sides: the shift opens nothing worth having.
            m.blocks.Add(new MapBlock("PlugA", BlockKind.Wall, new Vec3(-14f, 2.5f, -10.5f), new Vec3(6f, 5f, 2f)));
            m.blocks.Add(new MapBlock("PlugB", BlockKind.Wall, new Vec3(14f, 2.5f, 10.5f), new Vec3(6f, 5f, 2f)));
        }), "doesn't shorten", "a shift that opens nothing");
    }

    // ---- Both modes, played on the map ----

    sealed class MapMatch
    {
        public readonly MapDef Map = MapCatalog.Default;
        public readonly GameEventBus Events = new GameEventBus { Record = true };
        public readonly MatchRules Match;
        public readonly CombatServer Combat;
        public const float Dt = 0.05f;

        public MapMatch(ModeDef mode, int perTeam)
        {
            var world = new MapZoneWorld { Map = Map };
            var rules = Ruleset.Default;
            rules.heroPickSeconds = 0.5f;
            rules.deploySeconds = 0.5f;
            Match = new MatchRules(rules, mode, Map.ToMatchMap(), world, Events);
            Combat = new CombatServer(Match, world, Events, Dt, Map.ToLayout());
            world.Motions = () => Combat.Movement.Motions;
            for (int i = 1; i <= perTeam; i++) { Match.AddPlayer(i, TeamId.A); Match.AddPlayer(100 + i, TeamId.B); }
            Check.True(Match.StartMatch().IsAccepted, "match starts");
        }

        public void Tick() { Match.Tick(Dt); Combat.Tick(Dt); }

        public void Stand(int id, ZoneId zone, int slot)
        {
            var z = Map.Zone(zone);
            var m = Combat.Movement.Motion(id);
            var spot = z.center + new Vec3(-1.5f + slot, 0f, z.center.z < 0f ? -2.5f : 2.5f);
            if (m != null && Vec3.Distance(m.position, spot) > 0.01f) Combat.Movement.Teleport(id, spot, 0f);
        }
    }

    [Test]
    static void CaptureAndHoldPlaysOnTheMap()
    {
        var g = new MapMatch(ModeDef.CaptureAndHold(), 3);
        while (g.Match.State.phase != MatchPhase.Play) g.Tick();

        // Everyone starts in their own spawn room.
        foreach (var p in g.Match.State.players)
        {
            var at = g.Combat.Movement.Motion(p.id).position;
            var room = p.team == TeamId.A ? g.Map.spawnsA : g.Map.spawnsB;
            Check.True(room.Any(s => Vec3.Distance(s.position, at) < 0.01f), "player " + p.id + " starts on a spawn spot of their room");
        }

        // Team A walks onto its own point and the middle; B stays home. The map's zones see them.
        int ticks = 0;
        while (g.Match.State.phase != MatchPhase.MatchEnd && ticks++ < 20 * 60 * 20)
        {
            g.Stand(1, new ZoneId(GantryYard.PointA), 0);
            g.Stand(2, new ZoneId(GantryYard.PointMid), 0);
            g.Stand(3, new ZoneId(GantryYard.PointMid), 1);
            g.Tick();
        }
        var s = g.Match.State;
        Check.Equal(MatchPhase.MatchEnd, s.phase, "match ends");
        Check.Equal(TeamId.A, s.winner, "A wins by holding two points");
        Check.Equal(TeamId.A, s.Zone(new ZoneId(GantryYard.PointA)).owner, "A holds its point");
        Check.Equal(TeamId.A, s.Zone(new ZoneId(GantryYard.PointMid)).owner, "A holds the middle");
        var shift = g.Events.History.OfType<MapShiftEvent>().ToList();
        Check.Equal(1, shift.Count, "the map shifted once");
        if (shift.Count == 1) Check.Equal(TeamId.A, shift[0].team, "when A reached the shift score");
        Check.True(s.mapShifted, "state says shifted (what the scene follows)");
    }

    [Test]
    static void KingOfTheHillPlaysOnTheMap()
    {
        var g = new MapMatch(ModeDef.KingOfTheHill(), 2);
        while (g.Match.State.phase != MatchPhase.Play) g.Tick();
        var hills = new HashSet<int>();
        int ticks = 0;
        while (g.Match.State.phase != MatchPhase.MatchEnd && ticks++ < 20 * 60 * 20)
        {
            var live = g.Match.State.zones.FirstOrDefault(z => z.active);
            if (live != null)
            {
                hills.Add(live.id.value);
                g.Stand(1, live.id, 0);
                g.Stand(2, live.id, 1);
            }
            g.Tick();
        }
        var s = g.Match.State;
        Check.Equal(MatchPhase.MatchEnd, s.phase, "match ends");
        Check.Equal(TeamId.A, s.winner, "A wins by standing on the hill");
        Check.True(hills.Count >= 3, "the hill moved round the map (" + hills.Count + " spots)");
        Check.Equal(1, g.Events.History.OfType<MapShiftEvent>().Count(), "the map shifted once");
    }

    // ---- Report: dotnet run --project Tests/CoreSim -- report map [out.svg] ----

    public static int Report(string[] args)
    {
        var map = MapCatalog.Default;
        var report = MapValidator.Check(map);
        Console.WriteLine(map.displayName + " (" + map.id + "): " + (report.Ok ? "valid" : report.errors.Count + " problems"));
        foreach (var e in report.errors) Console.WriteLine("  " + e);
        Console.WriteLine(report.Table());
        if (args.Length > 0)
        {
            File.WriteAllText(args[0], Svg(map));
            Console.WriteLine("Wrote " + args[0]);
        }
        return report.Ok ? 0 : 1;
    }

    /// <summary>A top-down plan: blocks by kind, zones, spawns, and team A's walks to each point before (solid) and after the shift (dashed).</summary>
    static string Svg(MapDef map)
    {
        const float S = 8f; // pixels per metre
        float W = map.halfX * 2f * S, H = map.halfZ * 2f * S;
        // Team A's end at the bottom of the picture: +z is up.
        string X(float x) => ((x + map.halfX) * S).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        string Y(float z) => ((map.halfZ - z) * S).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        string N(float v) => (v * S).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.Append("<svg xmlns='http://www.w3.org/2000/svg' width='").Append(W + 220).Append("' height='").Append(H).Append("' font-family='sans-serif'>");
        sb.Append("<rect width='100%' height='100%' fill='#1d2026'/>");
        string Fill(MapBlock b)
        {
            switch (b.kind)
            {
                case BlockKind.Ground: return "#5b6068";
                case BlockKind.Wall: return b.size.y >= 5f ? "#2b2f38" : "#c9bfa6";
                case BlockKind.LowCover: return "#e8c447";
                case BlockKind.Crate: return b.size.y > 3f || b.center.y > 2f ? "#2f7a44" : "#4f9e5c";
                case BlockKind.Platform: return "#8a6cb3";
                case BlockKind.Ramp: return "#b49ad6";
                case BlockKind.Shutter: return "#e0503c";
                default: return b.center.z < 0 ? "#3d99f2" : "#f28c33";
            }
        }
        foreach (var b in map.blocks.OrderBy(b => b.kind == BlockKind.Ground ? 0 : b.kind == BlockKind.Decal ? 1 : 2).ThenBy(b => b.center.y))
            sb.Append("<rect x='").Append(X(b.Min.x)).Append("' y='").Append(Y(b.Max.z)).Append("' width='").Append(N(b.size.x)).Append("' height='").Append(N(b.size.z))
              .Append("' fill='").Append(Fill(b)).Append("' stroke='#000' stroke-opacity='0.35'/>");
        foreach (var z in map.zones)
        {
            string col = z.set == "points" ? "#ffffff" : "#7fe0ff";
            sb.Append("<circle cx='").Append(X(z.center.x)).Append("' cy='").Append(Y(z.center.z)).Append("' r='").Append(N(z.radius))
              .Append("' fill='").Append(col).Append("' fill-opacity='0.12' stroke='").Append(col).Append("' stroke-width='2'").Append(z.set == "hills" ? " stroke-dasharray='6 4'" : "").Append("/>");
            sb.Append("<text x='").Append(X(z.center.x)).Append("' y='").Append(Y(z.center.z) ).Append("' fill='").Append(col).Append("' font-size='16' text-anchor='middle' dy='5'>").Append(z.label).Append("</text>");
        }
        void Spot(SpawnPoint p, string col) =>
            sb.Append("<circle cx='").Append(X(p.position.x)).Append("' cy='").Append(Y(p.position.z)).Append("' r='4' fill='").Append(col).Append("'/>");
        foreach (var p in map.spawnsA) Spot(p, "#3d99f2");
        foreach (var p in map.spawnsB) Spot(p, "#f28c33");
        foreach (var p in map.forward) Spot(p, map.Zone(p.zone).forwardSpawnFor == TeamId.A ? "#3d99f2" : "#f28c33");
        foreach (var p in map.dummies) Spot(p, "#ff5bd0");

        var rules = Ruleset.Default;
        foreach (bool shifted in new[] { false, true })
        {
            var nav = new MapNav(map, rules, shifted);
            var start = map.spawnsA[2].position;
            var field = nav.TravelFrom(start);
            var own = map.Zone(new ZoneId(GantryYard.PointA));
            var fromOwn = nav.TravelFrom(own.center);
            void Path(List<(int i, int j)> path, string col)
            {
                if (path.Count < 2) return;
                sb.Append("<polyline fill='none' stroke='").Append(col).Append("' stroke-width='2.5' stroke-opacity='0.85'").Append(shifted ? " stroke-dasharray='8 5'" : "").Append(" points='");
                foreach (var (i, j) in path) sb.Append(X(nav.X(i))).Append(',').Append(Y(nav.Z(j))).Append(' ');
                sb.Append("'/>");
            }
            if (!shifted)
                foreach (var z in map.zones.Where(z => z.set == "points")) Path(nav.PathToZone(field, z), "#9fd3ff");
            else Path(nav.PathToZone(fromOwn, map.Zone(new ZoneId(GantryYard.PointMid))), "#ff6b5b");
        }

        // Legend and travel table.
        float lx = W + 12; float ly = 24;
        void Line(string text, string col = "#e6e6e6", int size = 14) { sb.Append("<text x='").Append(lx).Append("' y='").Append(ly).Append("' fill='").Append(col).Append("' font-size='").Append(size).Append("'>").Append(System.Security.SecurityElement.Escape(text)).Append("</text>"); ly += size + 6; }
        Line(map.displayName, "#ffffff", 20);
        Line("Team A (blue) at the bottom", "#3d99f2");
        Line("Team B (orange) at the top", "#f28c33");
        Line("Tall wall", "#8d95a5"); Line("Low wall / short wall", "#c9bfa6"); Line("Vaultable cover", "#e8c447");
        Line("Crates, containers", "#4f9e5c"); Line("Crane deck 2.5 m / ramp", "#b49ad6"); Line("Shutter (lifts at shift)", "#e0503c");
        Line("Points: solid rings", "#ffffff"); Line("Hills: dashed rings", "#7fe0ff"); Line("Dummy spots", "#ff5bd0");
        Line("A spawn to each point", "#9fd3ff"); Line("A point to mid, after shift", "#ff6b5b");
        ly += 10;
        var rep = MapValidator.Check(map);
        Line("Walk seconds A / B", "#ffffff", 15);
        foreach (var r in rep.fromSpawns) Line(r.label + ": " + r.a.ToString("0.0") + " / " + r.b.ToString("0.0"), "#e6e6e6", 13);
        Line("Point to mid: " + rep.pointToMidA.ToString("0.0") + " -> " + rep.pointToMidAShifted.ToString("0.0"), "#e6e6e6", 13);
        sb.Append("</svg>");
        return sb.ToString();
    }
}
