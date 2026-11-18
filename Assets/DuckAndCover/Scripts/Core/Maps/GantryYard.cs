namespace DuckAndCover.Core
{
    /// <summary>
    /// The first map, a greybox: a freight yard 72 m by 128 m. Each team's spawn room sits at its
    /// end; its own point (A or B) is out to one side in front of it, next to a warehouse hall; the
    /// middle point is in an open square between two raised crane decks. Three routes run up the
    /// map: a long outer lane, a centre lane with screens against long sightlines, and a container
    /// yard to stand and turn corners in.
    ///
    /// Who it serves: vanguards fight in the halls and around the containers, strikers hold the
    /// outer lanes and the crane decks (2.5 m up, looking over the middle), controllers have the
    /// chokes where each outer lane squeezes past a deck, and supports have low walls on every point
    /// to work from.
    ///
    /// The map shift: each hall's shutter on the side facing the middle lifts, so a team holding its
    /// own point gets a short covered route to the middle, and the other team gets a new way in.
    ///
    /// Everything below is team A's half (z below 0). <see cref="Pair"/> adds each piece and the same
    /// piece turned 180° about the centre for team B; <see cref="Self"/> pieces sit on the centre and
    /// must already be symmetric. The layout is our own; it is not taken from any other game.
    /// </summary>
    public static class GantryYard
    {
        public const string Id = "gantry_yard";

        public const int PointA = 1, PointMid = 2, PointB = 3;
        public const int Hill1 = 11, Hill2 = 12, Hill3 = 13, Hill4 = 14;

        const float WallHeight = 6f;
        const float RoomHeight = 4f;
        const float HallHeight = 5f;
        const float ShutterHeight = 4f;
        const float ContainerHeight = 2.6f;
        const float DeckHeight = 2.5f;

        public static MapDef Build()
        {
            var m = new MapDef
            {
                id = Id,
                displayName = "Gantry Yard",
                halfX = 36f,
                halfZ = 64f,
                shiftDescription = "The shutters at the far end of both warehouse halls lift, opening a covered route from each side's point to the middle.",
                overviewPosition = new Vec3(0f, 70f, -62f),
                overviewTarget = new Vec3(0f, 0f, -6f),
                theme = "jungle_moon",
            };

            // ---- Floor and outer walls ----
            Self(m, "Ground", BlockKind.Ground, 0f, 0f, 72f, 1f, 128f, -1f);
            Pair(m, "OuterWallSouth", BlockKind.Wall, 0f, -64.25f, 73f, WallHeight, 0.5f);
            Pair(m, "OuterWallWest", BlockKind.Wall, -36.25f, 0f, 0.5f, WallHeight, 129f);

            // ---- Spawn room: 18 m by 12 m against the back wall, a door in each side wall, a solid front ----
            Pair(m, "SpawnWestBack", BlockKind.Wall, -9.25f, -60.5f, 0.5f, RoomHeight, 7f);
            Pair(m, "SpawnWestFront", BlockKind.Wall, -9.25f, -53.25f, 0.5f, RoomHeight, 2.5f);
            Pair(m, "SpawnEastBack", BlockKind.Wall, 9.25f, -60.5f, 0.5f, RoomHeight, 7f);
            Pair(m, "SpawnEastFront", BlockKind.Wall, 9.25f, -53.25f, 0.5f, RoomHeight, 2.5f);
            Pair(m, "SpawnFront", BlockKind.Wall, 0f, -51.75f, 19f, RoomHeight, 0.5f);
            Pair(m, "SpawnPad", BlockKind.Decal, 0f, -58f, 12f, 0.02f, 3f);
            // Cover just outside each door, so leaving the room isn't walking into the open.
            Pair(m, "SpawnDoorCoverWest", BlockKind.LowCover, -13f, -53f, 0.4f, 1f, 3f);
            Pair(m, "SpawnDoorCoverEast", BlockKind.LowCover, 13f, -53f, 0.4f, 1f, 3f);

            // ---- Centre lane: low walls and screens so the spawn never sees the middle ----
            Pair(m, "CentreLow", BlockKind.LowCover, -4f, -38f, 4f, 1f, 0.4f);
            Pair(m, "CentreBlock", BlockKind.Crate, 3.5f, -31f, 3f, 3f, 3f);
            Pair(m, "CentreScreen", BlockKind.Wall, 0f, -24f, 6f, 3.5f, 0.5f);
            Pair(m, "CentreCrate", BlockKind.Crate, -3f, -16f, 1.2f, 1.2f, 1.2f);

            // ---- Point A: open ground beside the hall, with cover on every side ----
            Pair(m, "PointLowNorth", BlockKind.LowCover, -16f, -26f, 4f, 1f, 0.4f);
            Pair(m, "PointCrateWest", BlockKind.Crate, -21f, -31f, 1.5f, 1.5f, 1.5f);
            Pair(m, "PointCrateEast", BlockKind.Crate, -10.5f, -34f, 1.2f, 1.2f, 1.2f);
            Pair(m, "PointWallWest", BlockKind.Wall, -24f, -34f, 0.5f, 3f, 8f);
            Pair(m, "PointLowSouth", BlockKind.LowCover, -15f, -38.5f, 3f, 1f, 0.4f);

            // ---- The hall: 12 m by 10 m, door to the point, door to the outer lane, shutter to the middle ----
            const float hx0 = -20f, hx1 = -8f, hz0 = -22f, hz1 = -12f;
            Pair(m, "HallSouthWest", BlockKind.Wall, -18f, hz0, 4f, HallHeight, 0.5f);
            Pair(m, "HallSouthEast", BlockKind.Wall, -10f, hz0, 4f, HallHeight, 0.5f);
            Pair(m, "HallWestSouth", BlockKind.Wall, hx0, -20.5f, 0.5f, HallHeight, 3f);
            Pair(m, "HallWestNorth", BlockKind.Wall, hx0, -14.25f, 0.5f, HallHeight, 4.5f);
            Pair(m, "HallEast", BlockKind.Wall, hx1, -17f, 0.5f, HallHeight, 10.5f);
            Pair(m, "HallNorthWest", BlockKind.Wall, -18.5f, hz1, 3f, HallHeight, 0.5f);
            Pair(m, "HallNorthEast", BlockKind.Wall, -9.5f, hz1, 3f, HallHeight, 0.5f);
            Pair(m, "HallLintel", BlockKind.Wall, -14f, hz1, 6f, HallHeight - ShutterHeight, 0.5f, ShutterHeight);
            Pair(m, "HallShutter", BlockKind.Shutter, -14f, hz1, 6f, ShutterHeight, 0.3f, 0f, ShiftRole.RemovedOnShift);
            Pair(m, "HallCrate", BlockKind.Crate, -17.5f, -18.5f, 1.2f, 1.2f, 1.2f);
            Pair(m, "HallLow", BlockKind.LowCover, -11f, -16.5f, 0.4f, 1f, 3f);

            // ---- The compound wall: the middle is walled off from each team's half, entered from the outer lanes ----
            Pair(m, "CompoundWest", BlockKind.Wall, -23f, hz1, 6f, HallHeight, 0.5f);
            Pair(m, "CompoundEast", BlockKind.Wall, 9f, hz1, 34f, HallHeight, 0.5f);

            // ---- The middle: a square with a pillar, two crane decks at the sides ----
            Self(m, "MidPillar", BlockKind.Wall, 0f, 0f, 1.5f, 3f, 1.5f);
            Pair(m, "MidLow", BlockKind.LowCover, -2.5f, -6f, 4f, 1f, 0.4f);
            Pair(m, "MidCrate", BlockKind.Crate, 5f, -2f, 1.2f, 1.2f, 1.2f);
            Pair(m, "MidContainer", BlockKind.Crate, 13f, -8f, 6f, ContainerHeight, 2.5f);
            // Team A's deck is on the west side, reached by a ramp from A's half; B's is the same turned round.
            Pair(m, "Deck", BlockKind.Platform, -20f, 0f, 6f, DeckHeight, 8f);
            Pair(m, "DeckRamp", BlockKind.Ramp, -20f, -7.5f, 4f, DeckHeight, 7f, 0f, ShiftRole.None, 0);
            Pair(m, "DeckRail", BlockKind.LowCover, -17.25f, 0f, 0.5f, 0.9f, 8f, DeckHeight);

            // ---- Outer lane: long sightlines broken by containers, squeezed past the deck at the middle ----
            Pair(m, "LaneContainerSouth", BlockKind.Crate, -30f, -44f, 2.5f, ContainerHeight, 6f);
            Pair(m, "LaneLow", BlockKind.LowCover, -32f, -30f, 4f, 1f, 0.4f);
            Pair(m, "LaneContainerNorth", BlockKind.Crate, -28f, -17f, 2.5f, ContainerHeight, 6f);
            Pair(m, "LaneBreak", BlockKind.Wall, -31.5f, -1f, 9f, 3f, 1f);

            // ---- Container yard on the other side ----
            Pair(m, "YardContainer1", BlockKind.Crate, 14f, -44f, 6f, ContainerHeight, 2.5f);
            Pair(m, "YardContainer2", BlockKind.Crate, 22f, -36f, 2.5f, ContainerHeight, 6f);
            Pair(m, "YardContainer3", BlockKind.Crate, 13f, -27f, 2.5f, ContainerHeight, 6f);
            Pair(m, "YardStackLow", BlockKind.Crate, 21f, -21f, 6f, ContainerHeight, 2.5f);
            Pair(m, "YardStackHigh", BlockKind.Crate, 21f, -21f, 6f, ContainerHeight, 2.5f, ContainerHeight);
            Pair(m, "YardLow", BlockKind.LowCover, 29f, -28f, 4f, 1f, 0.4f);

            // ---- Zones: points A, middle, B; four hill spots in two turned pairs ----
            Zone(m, PointA, "points", "A", new Vec3(-16f, 0f, -32f), 5.5f, TeamId.None, TeamId.A);
            Zone(m, PointMid, "points", "Mid", Vec3.Zero, 6f, TeamId.None, TeamId.None);
            Zone(m, PointB, "points", "B", MapDef.Turn(new Vec3(-16f, 0f, -32f)), 5.5f, TeamId.None, TeamId.B);
            Zone(m, Hill1, "hills", "Hill 1", new Vec3(-11f, 0f, -6.5f), 4f, TeamId.None, TeamId.None);
            Zone(m, Hill2, "hills", "Hill 2", MapDef.Turn(new Vec3(-11f, 0f, -6.5f)), 4f, TeamId.None, TeamId.None);
            Zone(m, Hill3, "hills", "Hill 3", new Vec3(16f, 0f, -18f), 4f, TeamId.None, TeamId.None);
            Zone(m, Hill4, "hills", "Hill 4", MapDef.Turn(new Vec3(16f, 0f, -18f)), 4f, TeamId.None, TeamId.None);

            // ---- Spawn spots ----
            for (int i = 0; i < 5; i++)
            {
                var s = new SpawnPoint(-4f + 2f * i, 0f, -58f, 0f);
                m.spawnsA.Add(s);
                m.spawnsB.Add(MapDef.Turn(s, 0));
            }
            // Coming back at your own point: just behind it, toward your base.
            foreach (float x in new[] { -18f, -14f })
            {
                var s = new SpawnPoint(x, 0f, -42f, 0f, PointA);
                m.forward.Add(s);
                m.forward.Add(MapDef.Turn(s, PointB));
            }
            // Practice dummies stand just past the middle on B's side, facing A.
            m.dummies.Add(new SpawnPoint(-3f, 0f, 9f, 180f));
            m.dummies.Add(new SpawnPoint(3f, 0f, 10.5f, 180f));
            m.dummies.Add(new SpawnPoint(7f, 0f, 8.5f, 200f));
            return m;
        }

        /// <summary>A box by footprint centre, size and the height its bottom sits at.</summary>
        static MapBlock Box(string name, BlockKind kind, float x, float z, float sx, float sy, float sz, float bottom, ShiftRole shift, int rises) =>
            new MapBlock(name, kind, new Vec3(x, bottom + sy * 0.5f, z), new Vec3(sx, sy, sz), shift, rises);

        static void Self(MapDef m, string name, BlockKind kind, float x, float z, float sx, float sy, float sz, float bottom = 0f) =>
            m.blocks.Add(Box(name, kind, x, z, sx, sy, sz, bottom, ShiftRole.None, 0));

        static void Pair(MapDef m, string name, BlockKind kind, float x, float z, float sx, float sy, float sz,
            float bottom = 0f, ShiftRole shift = ShiftRole.None, int rises = 0)
        {
            var a = Box(name + "A", kind, x, z, sx, sy, sz, bottom, shift, rises);
            m.blocks.Add(a);
            m.blocks.Add(MapDef.Turn(a, name + "B"));
        }

        static void Zone(MapDef m, int id, string set, string label, Vec3 center, float radius, TeamId startOwner, TeamId forwardFor) =>
            m.zones.Add(new MapZone
            {
                id = new ZoneId(id), set = set, label = label, center = center, radius = radius,
                startOwner = startOwner, forwardSpawnFor = forwardFor,
            });
    }
}
