using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>
    /// An art mock-up, not a playable map and not in <see cref="MapCatalog"/>: a winding dirt lane walled in
    /// by tree lines and rocks, with gaps where trails lead off into jungle pockets. It shows how the
    /// environment kit dresses lanes, lane edges and jungle entrances before the lane maps for the new
    /// modes exist. The screenshot run builds it off to one side of the real map.
    ///
    /// The lane walls are real blocks (with the art-only <see cref="MapBlock.style"/> "treeline" or
    /// "rocks"), because in a shooter anything that frames a lane must also stop players and shots.
    /// </summary>
    public static class LaneStudy
    {
        public const string Id = "lane_study";
        public const float LaneWidth = 9f;
        const float TreeHeight = 4.5f, RockHeight = 3.2f, WallHeight = 6f;
        const float Post = 2.8f, Step = 2.2f;

        public static MapDef Build()
        {
            var m = new MapDef
            {
                id = Id,
                displayName = "Lane study",
                halfX = 30f,
                halfZ = 56f,
                theme = "jungle_moon",
                overviewPosition = new Vec3(0f, 60f, -60f),
                overviewTarget = new Vec3(0f, 0f, -8f),
            };

            // The lane: team A's half from its end to the centre, then the same turned about the centre.
            var half = new[] { new Vec3(0f, 0f, -56f), new Vec3(0f, 0f, -42f), new Vec3(-10f, 0f, -28f), new Vec3(-10f, 0f, -12f), new Vec3(0f, 0f, 0f) };
            var lane = new List<Vec3>(half);
            for (int i = half.Length - 2; i >= 0; i--) lane.Add(MapDef.Turn(half[i]));
            m.paths.Add(new ArtPath("lane", LaneWidth, lane.ToArray()));

            // Trails through gaps in the lane walls into jungle pockets (team A's two, then turned).
            var trails = new List<ArtPath>
            {
                new ArtPath("trail", 3.5f, new Vec3(-14.6f, 0f, -20f), new Vec3(-22f, 0f, -20f), new Vec3(-23f, 0f, -34f)),
                new ArtPath("trail", 3.5f, new Vec3(4.2f, 0f, -47f), new Vec3(16f, 0f, -45f), new Vec3(19f, 0f, -33f)),
            };
            foreach (var t in trails.ToArray())
            {
                var turned = new List<Vec3>();
                foreach (var p in t.points) turned.Add(MapDef.Turn(p));
                trails.Add(new ArtPath(t.style, t.width, turned.ToArray()));
            }
            m.paths.AddRange(trails);

            // Lane walls: a post every couple of metres along both edges of team A's half, skipped where a
            // trail leaves the lane or where a post would stand in the lane. Rocks at the bends, trees elsewhere.
            var mainLane = m.paths[0];
            var posts = new List<Vec3>();
            // Candidate spots: along both edges of each straight, and around the outside of each bend.
            var spots = new List<(float x, float z, float nx, float nz)>();
            for (int i = 0; i + 1 < half.Length; i++)
            {
                Vec3 a = half[i], b = half[i + 1];
                float dx = b.x - a.x, dz = b.z - a.z;
                float len = (float)Math.Sqrt(dx * dx + dz * dz);
                float nx = -dz / len, nz = dx / len;
                for (float d = 0f; d <= len; d += Step)
                    for (int side = -1; side <= 1; side += 2)
                        spots.Add((a.x + dx * d / len, a.z + dz * d / len, nx * side, nz * side));
            }
            for (int i = 1; i + 1 < half.Length; i++)
                for (int k = 0; k < 18; k++)
                {
                    float ang = k * (float)Math.PI * 2f / 18f;
                    spots.Add((half[i].x, half[i].z, (float)Math.Cos(ang), (float)Math.Sin(ang)));
                }
            int n = 0;
            foreach (var sp in spots)
            {
                // Start just outside the lane and step outward until the whole post clears it.
                float off = LaneWidth * 0.5f + Post * 0.5f;
                float x = 0f, z = 0f;
                bool clear = false;
                for (int tries = 0; tries < 8 && !clear; tries++, off += 0.35f)
                {
                    x = sp.x + sp.nx * off; z = sp.z + sp.nz * off;
                    clear = Nearest(mainLane, x, z, Post * 0.5f) >= LaneWidth * 0.5f;
                }
                if (!clear) continue;
                if (x * x + z * z < Post * Post) continue;                           // would overlap its own turned twin
                if (Math.Abs(x) > m.halfX - 2f || z < -m.halfZ + 1.5f) continue;
                bool skip = false;
                foreach (var t in trails) if (t.DistanceTo(x, z) < t.width * 0.5f + Post * 0.5f + 0.3f) skip = true;
                // Not on top of a post already placed, or of that post's turned twin on B's half.
                foreach (var p in posts)
                    if ((p.x - x) * (p.x - x) + (p.z - z) * (p.z - z) < 1.7f * 1.7f || (p.x + x) * (p.x + x) + (p.z + z) * (p.z + z) < 1.7f * 1.7f) skip = true;
                if (skip) continue;
                posts.Add(new Vec3(x, 0f, z));
                bool bend = NearBend(half, x, z, 5f);
                Pair(m, "Edge" + (n++), BlockKind.Wall, x, z, Post, bend ? RockHeight : TreeHeight, Post, bend ? "rocks" : "treeline");
            }

            // Jungle pockets at the trail ends: a few rocks to stand behind.
            Pair(m, "PocketRockWest", BlockKind.Wall, -25f, -38f, 3f, 2.2f, 3f, "rocks");
            Pair(m, "PocketRockWestLow", BlockKind.LowCover, -20f, -36f, 2.5f, 1f, 1.5f, "rocks");
            Pair(m, "PocketRockEast", BlockKind.Wall, 22f, -30f, 3f, 2.4f, 3.5f, "rocks");
            Pair(m, "PocketTreesEast", BlockKind.Wall, 26f, -40f, 3f, TreeHeight, 6f, "treeline");
            Pair(m, "PocketTreesWest", BlockKind.Wall, -27f, -24f, 3f, TreeHeight, 5f, "treeline");

            // Floor and outer walls.
            m.blocks.Add(new MapBlock("Ground", BlockKind.Ground, new Vec3(0f, -0.5f, 0f), new Vec3(m.halfX * 2f, 1f, m.halfZ * 2f)));
            Pair(m, "OuterWallSouth", BlockKind.Wall, 0f, -m.halfZ - 0.25f, m.halfX * 2f + 1f, WallHeight, 0.5f, "");
            Pair(m, "OuterWallWest", BlockKind.Wall, -m.halfX - 0.25f, 0f, 0.5f, WallHeight, m.halfZ * 2f + 1f, "");

            // A point in the middle of the lane, so the paving and ring show how a zone sits in it.
            m.zones.Add(new MapZone { id = new ZoneId(1), set = "points", label = "Mid", center = Vec3.Zero, radius = 4f });
            return m;
        }

        /// <summary>The nearest distance from a square post (centre and corners) to the path's centre line.</summary>
        static float Nearest(ArtPath path, float x, float z, float half)
        {
            float best = float.MaxValue;
            for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
                best = Math.Min(best, path.DistanceTo(x + i * half, z + j * half));
            return best;
        }

        static bool NearBend(Vec3[] line, float x, float z, float within)
        {
            for (int i = 1; i + 1 < line.Length; i++)
            {
                float dx = line[i].x - x, dz = line[i].z - z;
                if (dx * dx + dz * dz < within * within * 2.5f) return true;
            }
            return false;
        }

        static void Pair(MapDef m, string name, BlockKind kind, float x, float z, float sx, float sy, float sz, string style)
        {
            var a = new MapBlock(name + "A", kind, new Vec3(x, sy * 0.5f, z), new Vec3(sx, sy, sz)) { style = style };
            m.blocks.Add(a);
            m.blocks.Add(MapDef.Turn(a, name + "B"));
        }
    }
}
