using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// Breakwater, the Overrun map (WP18): one winding minion lane, 120 m by 210 m, team A's base at the
    /// -z end and team B's at +z, mirrored end to end (east stays east), drawn from Zach's Nov 18 sketch.
    ///
    /// - The lane runs straight out of each base, past the Forward Bastion, then bends east and zigzags
    ///   through the middle, so no one can see down more than a third of it. Continuous tree and rock
    ///   walls line it; the only ways off it are the gaps where jungle routes meet it.
    /// - West (the short jungle): a route leaves the lane past each Forward Bastion and climbs to a raised
    ///   shelf over the middle of the map, the Juggernaut camp. The shelf is walled from the lane except
    ///   one railed edge over a pocket beside mid-lane: drop down it (one way) and you land behind cover.
    ///   Anyone shooting from that rail stands in the open above the pocket.
    /// - East (the long jungle): a route from each base's side door winds up the map past that team's
    ///   damage and experience camps, with a dead-end branch to a speed camp, a cut-through to mid-lane,
    ///   and joins the other team's half the same way.
    /// - Each base: a spawn room, a plaza with the Core Bastion, a launch pad that throws you to your
    ///   side of mid-lane, and a ledge 3 m over the lane in front of the Forward Bastion, reached by stairs
    ///   from behind it. A side gate from the west jungle into the plaza opens with the map shift.
    ///
    /// Team A's half is written out below; <see cref="Pair"/> adds every piece and its mirror image.
    /// The layout is written in plan units from the sketch and stretched across the map by <see cref="StretchX"/>
    /// (Nov 18 rework: the first cut felt cramped). Lane and trail widths are real metres, chosen from body sizes:
    /// every hero's collision capsule is 0.7 m across, the widest model (Bulwark) about 2.4 m at the shoulders,
    /// the others about 1 m. In the 16 m lane (about 18 m clear between the walls) three heroes side by side,
    /// tank included, use about 4.4 m and still have roughly 4 m each to strafe in. Jungle routes are 6 m and
    /// trails 5.5 m, which the 2 m wall tiles leave at about 8 m clear: a tank and one other hero abreast with a
    /// couple of metres of strafe each. `report breakwater` prints the measured widths.
    /// Everything that isn't lane, route, plaza or clearing is filled with tree and rock walls (2 m tiles),
    /// so the jungle is a maze of paths rather than open ground. The layout is our own, not from any game.
    /// </summary>
    public static class Breakwater
    {
        public const string Id = "breakwater";
        public const float HalfX = 60f, HalfZ = 105f;
        /// <summary>Plan x coordinates (from the sketch) times this are metres. Plan z is metres already.</summary>
        public const float StretchX = 1.5f;
        /// <summary>Flag Rush capture zones: the one in team A's base (team B scores there) and B's.</summary>
        public const int BaseA = 21, BaseB = 22;
        public const float LaneWidth = 16f;
        /// <summary>Main jungle routes and the cut-through.</summary>
        public const float JungleWidth = 6f;
        /// <summary>Loops, the speed-camp branch and the side route.</summary>
        public const float TrailWidth = 5.5f;
        /// <summary>Camp clearings are this much bigger than in the first cut, so a camp fight has room.</summary>
        const float ClearingScale = 1.25f;
        /// <summary>Jungle walls: too tall to see over from the ledges, the shelf or a jump.</summary>
        public const float FillHeight = 6.5f;
        public const float LedgeHeight = 3f;
        public const float ShelfHeight = 4f;
        const float Tile = 2f;
        /// <summary>Longest run of fill tiles merged into one block (8 m), so the dressing spreads its trees along the walls.</summary>
        const int MaxRun = 4;
        const float RoomHeight = 4f;
        const float OuterHeight = 9f;

        /// <summary>Team A's half of the lane, base end first; the middle point sits on the mirror line.</summary>
        static readonly Vec3[] LaneHalf =
        {
            P(-12f, 0f, -92f), P(-12f, 0f, -46f), P(8f, 0f, -30f), P(18f, 0f, -15f), P(8f, 0f, 0f),
        };

        public static MapDef Build()
        {
            var m = new MapDef
            {
                id = Id,
                displayName = "Breakwater",
                halfX = HalfX,
                halfZ = HalfZ,
                symmetry = MapSymmetry.MirrorZ,
                laneWidth = LaneWidth,
                shiftDescription = "A gate from the west jungle into each base's plaza opens. In Overrun it opens when that base's Forward Bastion falls; in Flag Rush, at the map shift.",
                overviewPosition = new Vec3(-100f, 105f, -70f),
                overviewTarget = new Vec3(0f, 0f, -20f),
                theme = "jungle_moon",
            };
            // The Juggernaut shelf is a sandstone ruin in the jungle: the map's landmark.
            m.artRegions.Add(new ArtRegion("jungle_temple", -HalfX - 0.5f, -16f, -10f * StretchX, 16f));

            // ---- The lane ----
            var lane = new LaneDef { id = "lane", leash = 8f };
            lane.points.AddRange(LaneHalf);
            for (int i = LaneHalf.Length - 2; i >= 0; i--) lane.points.Add(MapDef.Mirror(LaneHalf[i]));
            m.lanes.Add(lane);
            m.paths.Add(new ArtPath("lane", LaneWidth, lane.points.ToArray()));

            // ---- Routes (heroes only). Team A's half; the mirror makes B's, and routes that cross the
            // middle are written whole. ----
            var shortJungle = Route(m, "ShortJungle", RouteKind.Jungle, JungleWidth, true,
                P(-4f, 0f, -35f), P(-14f, 0f, -30f), P(-21f, 0f, -25f), P(-21f, ShelfHeight, -11f), P(-19f, ShelfHeight, 0f));
            var longJungle = Route(m, "LongJungle", RouteKind.Jungle, JungleWidth, true,
                P(-3f, 0f, -86f), P(14f, 0f, -86f), P(26f, 0f, -76f), P(32f, 0f, -58f), P(28f, 0f, -44f),
                P(34f, 0f, -30f), P(30f, 0f, -14f), P(34f, 0f, 0f));
            Route(m, "CutThrough", RouteKind.CutThrough, JungleWidth, false, P(34f, 0f, 0f), P(13f, 0f, 0f));
            PairRoute(m, "JungleLoop", RouteKind.Loop, TrailWidth, P(28f, 0f, -44f), P(17f, 0f, -41f), P(20f, 0f, -31f), P(32.5f, 0f, -24f));
            PairRoute(m, "SpeedBranch", RouteKind.Branch, TrailWidth, P(31f, 0f, -53f), P(22f, 0f, -56f), P(15f, 0f, -58f));
            PairRoute(m, "SideRoute", RouteKind.SideRoute, TrailWidth, P(-14f, 0f, -30f), P(-35f, 0f, -36f), P(-36f, 0f, -62f), P(-35f, 0f, -84f), P(-24f, 0f, -84f));

            // ---- Open ground the jungle fill leaves alone (team A's half, mirrored) ----
            var open = new List<Func<float, float, bool>>();
            void Rect(float x0, float z0, float x1, float z1)
            {
                x0 *= StretchX; x1 *= StretchX;
                open.Add((x, z) => x > x0 && x < x1 && z > z0 && z < z1);
                open.Add((x, z) => x > x0 && x < x1 && -z > z0 && -z < z1);
            }
            void Disc(float cx, float cz, float r)
            {
                cx *= StretchX;
                open.Add((x, z) => (x - cx) * (x - cx) + (z - cz) * (z - cz) < r * r);
                open.Add((x, z) => (x - cx) * (x - cx) + (z + cz) * (z + cz) < r * r);
            }
            Rect(-27f, -105f, 3f, -73f);      // spawn room and core plaza
            Rect(-29f, -71f, 1f, -55f);       // forward plaza behind the Forward Bastion, and the ledge stairs
            Rect(-29f, -59f, -16f, -45f);     // the ledge
            Rect(-37f, -13f, -11f, 0f);       // the Juggernaut shelf (both halves meet on the mirror line)
            Rect(-26f, -27f, -16f, -11f);     // the shelf stairs
            Rect(-12f, -7f, 4f, 0f);          // the drop pocket beside mid-lane
            Disc(14f, -95f, 7.5f * ClearingScale);   // damage camp
            Disc(34f, -80f, 6.5f * ClearingScale);   // experience camp
            Disc(14f, -58f, 5.5f * ClearingScale);   // speed camp
            Disc(-36f, -64f, 5f);                    // a bend in the side route, to fight round

            bool Open(float x, float z)
            {
                foreach (var o in open) if (o(x, z)) return true;
                if (lane.Project(new Vec3(x, 0f, z), 0f, float.MaxValue, out float off) >= 0f && off < LaneWidth * 0.5f + 0.6f) return true;
                foreach (var r in m.routes)
                    if (PathDistance(r.points, x, z) < r.width * 0.5f + 0.6f) return true;
                return false;
            }

            // ---- The jungle fill: 2 m tiles of tree line and rock, merged into runs of up to 8 m along x ----
            int cols = (int)Math.Round(HalfX * 2f / Tile);
            int rows = (int)Math.Round(HalfZ * 2f / Tile); // odd: the middle row sits on the mirror line
            int mid = rows / 2;
            for (int j = 0; j <= mid; j++)
            {
                float z = -HalfZ + (j + 0.5f) * Tile;
                int i = 0, run = 0;
                while (i < cols)
                {
                    float x0 = -HalfX + i * Tile;
                    // A tile is filled when none of it is open: test its centre and corners, pulled in a little.
                    if (TileOpen(Open, x0, z)) { i++; continue; }
                    string style = StyleAt(x0 + Tile * 0.5f, z);
                    int k = i + 1;
                    while (k < cols && k - i < MaxRun && !TileOpen(Open, -HalfX + k * Tile, z) && StyleAt(-HalfX + (k + 0.5f) * Tile, z) == style) k++;
                    float sx = (k - i) * Tile;
                    var b = new MapBlock("Jungle" + j + "_" + (run++), BlockKind.Wall, new Vec3(x0 + sx * 0.5f, FillHeight * 0.5f, z), new Vec3(sx, FillHeight, Tile)) { style = style };
                    if (j == mid) m.blocks.Add(b);
                    else { b.name += "A"; m.blocks.Add(b); m.blocks.Add(MapDef.Mirror(b, b.name.Substring(0, b.name.Length - 1) + "B")); }
                    i = k;
                }
            }

            // ---- Floor and outer walls ----
            m.blocks.Add(new MapBlock("Ground", BlockKind.Ground, new Vec3(0f, -0.5f, 0f), new Vec3(HalfX * 2f, 1f, HalfZ * 2f)));
            m.blocks.Add(Box("OuterWallEast", BlockKind.Wall, HalfX + 0.25f, 0f, 0.5f, OuterHeight, HalfZ * 2f + 1f, 0f, "rocks"));
            m.blocks.Add(Box("OuterWallWest", BlockKind.Wall, -HalfX - 0.25f, 0f, 0.5f, OuterHeight, HalfZ * 2f + 1f, 0f, "rocks"));
            var south = Box("OuterWallSouthA", BlockKind.Wall, 0f, -HalfZ - 0.25f, HalfX * 2f + 1f, OuterHeight, 0.5f, 0f);
            m.blocks.Add(south);
            m.blocks.Add(MapDef.Mirror(south, "OuterWallSouthB"));

            // ---- Spawn room: 16 m by 9 m against the back wall, two doors out to the plaza ----
            Pair(m, "SpawnWest", BlockKind.Wall, -20.25f, -100.5f, 0.5f, RoomHeight, 9f);
            Pair(m, "SpawnEast", BlockKind.Wall, -3.75f, -100.5f, 0.5f, RoomHeight, 9f);
            Pair(m, "SpawnFrontWest", BlockKind.Wall, -19.25f, -95.75f, 2.5f, RoomHeight, 0.5f);
            Pair(m, "SpawnFrontMiddle", BlockKind.Wall, -12f, -95.75f, 6f, RoomHeight, 0.5f);
            Pair(m, "SpawnFrontEast", BlockKind.Wall, -4.75f, -95.75f, 2.5f, RoomHeight, 0.5f);
            Pair(m, "SpawnPad", BlockKind.Decal, -12f, -101f, 12f, 0.02f, 3f);
            for (int s = 0; s < 5; s++)
            {
                var spot = new SpawnPoint((-16f + 2f * s) * StretchX, 0f, -101f, 0f);
                m.spawnsA.Add(spot);
                m.spawnsB.Add(MapDef.Mirror(spot));
            }

            // Practice dummies stand on the lane just past the middle on B's side, facing A's half.
            m.dummies.Add(new SpawnPoint(11f * StretchX, 0f, 6f, 200f));
            m.dummies.Add(new SpawnPoint(14f * StretchX, 0f, 9f, 200f));
            m.dummies.Add(new SpawnPoint(12f * StretchX, 0f, 12f, 200f));

            // ---- Core plaza: the Core Bastion on the lane, cover, the launch pad, the side gate ----
            m.bastions.Add(new MapBastion { team = TeamId.A, tier = BastionTier.Core, position = P(-12f, 0f, -84f), radius = 2.5f });
            Cover(m, "PlazaLowWest", BlockKind.LowCover, -20f, -90f, 3f, 1f, 0.5f);
            Cover(m, "PlazaLowEast", BlockKind.LowCover, -4f, -90f, 3f, 1f, 0.5f);
            Cover(m, "PlazaCrate", BlockKind.Crate, -4f, -79f, 1.5f, 1.5f, 1.5f);
            Cover(m, "LaunchPadMark", BlockKind.Decal, -23f, -86f, 3f, 0.02f, 3f);
            var pad = new LaunchPad("LaunchA", TeamId.A, P(-23f, 0f, -86f), P(14f, 0f, -12f), 18f, 3.4f, 1.5f);
            m.launchPads.Add(pad);
            m.launchPads.Add(new LaunchPad("LaunchB", TeamId.B, MapDef.Mirror(pad.center), MapDef.Mirror(pad.landing), pad.apexHeight, pad.seconds, pad.radius));
            // The side gate: shut until the shift, in the plaza's west edge where the side route arrives.
            Pair(m, "SideGate", BlockKind.Shutter, -27.5f, -84f, 1f, 4f, 6f, 0f, ShiftRole.RemovedOnShift);
            Pair(m, "SideGateLintel", BlockKind.Wall, -27.5f, -84f, 1f, FillHeight - 4f, 6f, 4f, ShiftRole.None, 0, "rocks");
            Pair(m, "SideGateJambSouth", BlockKind.Wall, -27.5f, -88f, 1f, FillHeight, 2f, 0f, ShiftRole.None, 0, "rocks");
            Pair(m, "SideGateJambNorth", BlockKind.Wall, -27.5f, -80f, 1f, FillHeight, 2f, 0f, ShiftRole.None, 0, "rocks");
            m.entrances.Add(new MapEntrance { name = "LaneMouthA", team = TeamId.A, position = P(-12f, 0f, -73f), halfWidth = LaneWidth * 0.5f + 1.5f });
            m.entrances.Add(new MapEntrance { name = "JungleDoorA", team = TeamId.A, position = P(3f, 0f, -86f), halfWidth = JungleWidth * 0.5f });
            m.entrances.Add(new MapEntrance { name = "LaneMouthB", team = TeamId.B, position = P(-12f, 0f, 73f), halfWidth = LaneWidth * 0.5f + 1.5f });
            m.entrances.Add(new MapEntrance { name = "JungleDoorB", team = TeamId.B, position = P(3f, 0f, 86f), halfWidth = JungleWidth * 0.5f });

            // ---- Forward Bastion, and the ledge over its approach ----
            m.bastions.Add(new MapBastion { team = TeamId.A, tier = BastionTier.Forward, position = P(-12f, 0f, -60f), radius = 2.5f });
            Pair(m, "Ledge", BlockKind.Platform, -23f, -52f, 11f, LedgeHeight, 13f);
            Pair(m, "LedgeStairs", BlockKind.Ramp, -26f, -63.5f, 4f, LedgeHeight, 10f, 0f, ShiftRole.None, 0);
            Pair(m, "LedgeRail", BlockKind.LowCover, -17.75f, -51f, 0.5f, 1f, 9f, LedgeHeight);
            Pair(m, "LedgeRailNorth", BlockKind.LowCover, -23f, -45.75f, 11f, 1f, 0.5f, LedgeHeight);
            m.drops.Add(new MapDrop { name = "LedgeA", top = P(-19f, LedgeHeight, -52f), bottom = P(-14.5f, 0f, -52f) });
            m.drops.Add(new MapDrop { name = "LedgeB", top = P(-19f, LedgeHeight, 52f), bottom = P(-14.5f, 0f, 52f) });

            // ---- Lane cover: low walls to move between on the way in ----
            // A tall rock outcrop on the inside of the first bend (placed in metres): cover in the wider lane, and it
            // stops the straight run out of the base from seeing round the bend.
            PairRaw(m, "LaneOutcrop", BlockKind.Wall, 0.5f, -41f, 3f, 3.2f, 3.5f, 0f, ShiftRole.None, 0, "rocks");
            // Tall rocks either side of mid-lane break the long look down the middle zigzag.
            PairRaw(m, "LaneMidRock", BlockKind.Wall, 21f, -7f, 3f, 3.2f, 3f, 0f, ShiftRole.None, 0, "rocks");
            // A rock spur on the inside of each middle bend, so the two bends either side of mid-lane can't see each other.
            PairRaw(m, "LaneBendSpur", BlockKind.Wall, 13f, -15f, 3f, FillHeight, 4f, 0f, ShiftRole.None, 0, "rocks");
            // More to move between now that the lane is wider (placed in metres).
            PairRaw(m, "LaneLowFwdEast", BlockKind.LowCover, -11f, -64f, 0.5f, 1f, 3f, 0f, ShiftRole.None, 0, "");
            PairRaw(m, "LaneCrateBend", BlockKind.Crate, -23f, -41f, 1.5f, 1.5f, 1.5f, 0f, ShiftRole.None, 0, "");
            PairRaw(m, "LaneLowZigWest", BlockKind.LowCover, 9f, -24f, 3f, 1f, 0.5f, 0f, ShiftRole.None, 0, "");
            // Nov 18 (Zach: "pretty open"): a cover-to-cover line down the lane, alternating sides so three abreast
            // still fit between pieces. Low walls to crouch and peek over, tall crate stacks and rocks to hide behind.
            PairRaw(m, "LaneStackCore", BlockKind.Crate, -12.5f, -76f, 1.6f, 2.6f, 1.6f, 0f, ShiftRole.None, 0, "");
            PairRaw(m, "LaneLowCoreWest", BlockKind.LowCover, -24f, -79f, 3f, 1f, 0.5f, 0f, ShiftRole.None, 0, "");
            PairRaw(m, "LaneLowFwdWest", BlockKind.LowCover, -24f, -63f, 0.5f, 1f, 3f, 0f, ShiftRole.None, 0, "");
            PairRaw(m, "LaneStackFwd", BlockKind.Crate, -13f, -55f, 1.6f, 2.6f, 1.6f, 0f, ShiftRole.None, 0, "");
            PairRaw(m, "LaneRockBendWest", BlockKind.Wall, -10f, -42f, 2.5f, 2.6f, 2.5f, 0f, ShiftRole.None, 0, "rocks");
            PairRaw(m, "LaneStackZig", BlockKind.Crate, 18f, -28f, 1.6f, 2.6f, 1.6f, 0f, ShiftRole.None, 0, "");
            PairRaw(m, "LaneLowZigEast", BlockKind.LowCover, 27f, -23f, 0.5f, 1f, 3f, 0f, ShiftRole.None, 0, "");
            PairRaw(m, "LaneLowMidEast", BlockKind.LowCover, 26f, -5f, 0.5f, 1f, 3f, 0f, ShiftRole.None, 0, "rocks");
            Cover(m, "LaneLowPlaza", BlockKind.LowCover, -15f, -69f, 3f, 1f, 0.5f);
            Cover(m, "LaneLowLedge", BlockKind.LowCover, -9f, -50f, 0.5f, 1f, 3f);
            Cover(m, "LaneRockBend", BlockKind.LowCover, 1f, -35f, 2f, 1.1f, 2f, 0f, ShiftRole.None, 0, "rocks");
            Cover(m, "LaneLowZig", BlockKind.LowCover, 15f, -22f, 3f, 1f, 0.5f);
            Cover(m, "LaneLowMid", BlockKind.LowCover, 10f, -8f, 0.5f, 1f, 3f);

            // ---- The Juggernaut shelf: raised over the middle, stairs up from each half's short jungle ----
            Self(m, "Shelf", BlockKind.Platform, -24f, 0f, 24f, ShelfHeight, 24f);
            Pair(m, "ShelfStairs", BlockKind.Ramp, -21f, -18f, 6f, ShelfHeight, 12f, 0f, ShiftRole.None, 0);
            // The edge over the drop pocket: a waist-high rail (exposed shooters, a vault to drop), with
            // rock walls either side that hide the rest of the shelf from the lane.
            Self(m, "ShelfRail", BlockKind.LowCover, -12.25f, 0f, 0.5f, 1f, 12f, ShelfHeight);
            Pair(m, "ShelfRockEdge", BlockKind.Wall, -12.5f, -9f, 1f, FillHeight, 6f, 0f, ShiftRole.None, 0, "rocks");
            CoverSelf(m, "PocketCover", BlockKind.Crate, -3f, 0f, 2f, 2.6f, 6f);
            m.drops.Add(new MapDrop { name = "JuggernautDrop", top = P(-13f, ShelfHeight, 0f), bottom = P(-9.5f, 0f, 0f) });

            // ---- Camps: seven ----
            m.camps.Add(Camp("Juggernaut", CampKind.Juggernaut, TeamId.None, P(-27f, ShelfHeight, 0f), 4f, 3.5f, 4));
            foreach (var team in new[] { TeamId.A, TeamId.B })
            {
                float s = team == TeamId.A ? 1f : -1f;
                m.camps.Add(Camp("Damage" + team, CampKind.Damage, team, P(14f, 0f, -96f * s), 3.5f, 3f, 3));
                m.camps.Add(Camp("Experience" + team, CampKind.Experience, team, P(35f, 0f, -80f * s), 3.5f, 2.5f, 3));
                m.camps.Add(Camp("Speed" + team, CampKind.Speed, team, P(13f, 0f, -58f * s), 3f, 2f, 1));
            }
            // A few rocks to fight around in the bigger clearings.
            Cover(m, "DamageRock", BlockKind.Wall, 9f, -97f, 2f, 2.4f, 3f, 0f, ShiftRole.None, 0, "rocks");
            // More to fight round in the clearings and where jungle trails meet (placed in metres).
            PairRaw(m, "DamageLow", BlockKind.LowCover, 26f, -91f, 0.5f, 1f, 3f, 0f, ShiftRole.None, 0, "rocks");
            PairRaw(m, "ExperienceRock", BlockKind.Wall, 57f, -76f, 2f, 2.4f, 2.5f, 0f, ShiftRole.None, 0, "rocks");
            PairRaw(m, "SpeedLow", BlockKind.LowCover, 23f, -62f, 2.5f, 1f, 0.5f, 0f, ShiftRole.None, 0, "rocks");
            PairRaw(m, "SideBendRock", BlockKind.Wall, -57.5f, -65f, 2f, 2.4f, 2f, 0f, ShiftRole.None, 0, "rocks");
            Cover(m, "ExperienceLow", BlockKind.LowCover, 30f, -83f, 2.5f, 1f, 1.5f, 0f, ShiftRole.None, 0, "rocks");
            // Broken temple walls at the top of each stair keep the camp out of sight from the lane below.
            PairRaw(m, "ShelfScreen", BlockKind.Wall, -37.5f, -7.5f, 7f, 3f, 1.5f, ShelfHeight, ShiftRole.None, 0, "rocks");
            CoverSelf(m, "JuggernautRock", BlockKind.Wall, -34f, 0f, 2f, 2.2f, 3f, ShelfHeight, "rocks");

            // ---- Flag Rush: a capture zone in each base's plaza, in front of the spawn room (its startOwner is
            // the team whose base it is), and flag spots on the middle line, where both bases are the same walk away ----
            m.zones.Add(new MapZone { id = new ZoneId(BaseA), set = "bases", label = "Base A", center = P(-12f, 0f, -91f), radius = 4.5f, startOwner = TeamId.A });
            m.zones.Add(new MapZone { id = new ZoneId(BaseB), set = "bases", label = "Base B", center = P(-12f, 0f, 91f), radius = 4.5f, startOwner = TeamId.B });
            m.flagSpots.Add(P(8f, 0f, 0f));            // mid-lane, under the shelf
            m.flagSpots.Add(P(25f, 0f, 0f));           // the cut-through from the long jungle
            m.flagSpots.Add(P(-8f, 0f, 0f));           // the drop pocket
            m.flagSpots.Add(P(-20f, ShelfHeight, 0f)); // on the Juggernaut shelf

            // Team B's Bastions mirror team A's.
            foreach (var bastion in m.bastions.ToArray())
                m.bastions.Add(new MapBastion { team = TeamId.B, tier = bastion.tier, position = MapDef.Mirror(bastion.position), radius = bastion.radius });
            return m;
        }

        // ---- Helpers ----

        static bool TileOpen(Func<float, float, bool> open, float x0, float z)
        {
            const float inset = 0.15f;
            float h = Tile * 0.5f - inset;
            float cx = x0 + Tile * 0.5f;
            return open(cx, z) || open(cx - h, z - h) || open(cx + h, z - h) || open(cx - h, z + h) || open(cx + h, z + h)
                || open(cx, z - h) || open(cx, z + h) || open(cx - h, z) || open(cx + h, z);
        }

        /// <summary>Rocks along the bends of the lane and around the shelf, tree line elsewhere (art only).</summary>
        static string StyleAt(float x, float z)
        {
            float az = Math.Abs(z);
            foreach (var p in LaneHalf)
                if ((p.x - x) * (p.x - x) + (Math.Abs(p.z) - az) * (Math.Abs(p.z) - az) < 14f * 14f && p.z > -90f) return "rocks";
            if (x < -10f * StretchX && az < 16f) return "rocks";
            return "treeline";
        }

        static float PathDistance(List<Vec3> pts, float x, float z)
        {
            var p = new ArtPath("trail", 1f) { points = pts };
            return p.DistanceTo(x, z);
        }

        /// <summary>A route and its art trail. <paramref name="mirrorTail"/> continues it through the mirror line into B's half.</summary>
        static MapRoute Route(MapDef m, string name, RouteKind kind, float width, bool mirrorTail, params Vec3[] half)
        {
            var r = new MapRoute { name = name, kind = kind, width = width };
            r.points.AddRange(half);
            if (mirrorTail)
                for (int i = half.Length - 2; i >= 0; i--) r.points.Add(MapDef.Mirror(half[i]));
            m.routes.Add(r);
            m.paths.Add(new ArtPath("trail", width, r.points.ToArray()));
            return r;
        }

        static void PairRoute(MapDef m, string name, RouteKind kind, float width, params Vec3[] pts)
        {
            Route(m, name + "A", kind, width, false, pts);
            var twin = new Vec3[pts.Length];
            for (int i = 0; i < pts.Length; i++) twin[i] = MapDef.Mirror(pts[i]);
            Route(m, name + "B", kind, width, false, twin);
        }

        static CampDef Camp(string id, CampKind kind, TeamId half, Vec3 pad, float padRadius, float ring, int guards)
        {
            var c = new CampDef { id = id, kind = kind, half = half, pad = pad, padRadius = padRadius };
            for (int g = 0; g < guards; g++)
            {
                double a = Math.PI * 2.0 * g / guards + 0.4;
                c.guardSpots.Add(new Vec3(pad.x + ring * (float)Math.Cos(a), pad.y, pad.z + ring * (float)Math.Sin(a)));
            }
            return c;
        }

        static MapBlock Box(string name, BlockKind kind, float x, float z, float sx, float sy, float sz, float bottom, string style = "",
            ShiftRole shift = ShiftRole.None, int rises = 0) =>
            new MapBlock(name, kind, new Vec3(x, bottom + sy * 0.5f, z), new Vec3(sx, sy, sz), shift, rises) { style = style };

        /// <summary>A structural piece in plan units: its position and its x size are stretched.</summary>
        static void Self(MapDef m, string name, BlockKind kind, float x, float z, float sx, float sy, float sz, float bottom = 0f, string style = "") =>
            m.blocks.Add(Box(name, kind, x * StretchX, z, sx * StretchX, sy, sz, bottom, style));

        /// <summary>A free-standing piece of cover: its position is stretched, its size stays in metres.</summary>
        static void CoverSelf(MapDef m, string name, BlockKind kind, float x, float z, float sx, float sy, float sz, float bottom = 0f, string style = "") =>
            m.blocks.Add(Box(name, kind, x * StretchX, z, sx, sy, sz, bottom, style));

        /// <summary>A structural piece and its mirror twin, in plan units (position and x size stretched).</summary>
        static void Pair(MapDef m, string name, BlockKind kind, float x, float z, float sx, float sy, float sz,
            float bottom = 0f, ShiftRole shift = ShiftRole.None, int rises = 0, string style = "") =>
            PairRaw(m, name, kind, x * StretchX, z, sx * StretchX, sy, sz, bottom, shift, rises, style);

        /// <summary>Free-standing cover and its mirror twin: position stretched, size in metres.</summary>
        static void Cover(MapDef m, string name, BlockKind kind, float x, float z, float sx, float sy, float sz,
            float bottom = 0f, ShiftRole shift = ShiftRole.None, int rises = 0, string style = "") =>
            PairRaw(m, name, kind, x * StretchX, z, sx, sy, sz, bottom, shift, rises, style);

        static void PairRaw(MapDef m, string name, BlockKind kind, float x, float z, float sx, float sy, float sz,
            float bottom, ShiftRole shift, int rises, string style)
        {
            var a = Box(name + "A", kind, x, z, sx, sy, sz, bottom, style, shift, rises);
            m.blocks.Add(a);
            m.blocks.Add(MapDef.Mirror(a, name + "B"));
        }

        /// <summary>A point in plan units: x stretched, y and z as they are.</summary>
        static Vec3 P(float x, float y, float z) => new Vec3(x * StretchX, y, z);
    }
}
