using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>What a map block is. Drives its colour, whether it is solid, and how the validator walks it.</summary>
    public enum BlockKind
    {
        /// <summary>The floor slab.</summary>
        Ground = 0,
        /// <summary>Outer and building walls: too tall to vault.</summary>
        Wall = 1,
        /// <summary>Waist-high cover: crouch behind it or vault it.</summary>
        LowCover = 2,
        /// <summary>Crates and containers: cover to stand behind.</summary>
        Crate = 3,
        /// <summary>A raised floor to stand on.</summary>
        Platform = 4,
        /// <summary>A slope up to a platform (see <see cref="MapBlock.rampRisesToward"/>).</summary>
        Ramp = 5,
        /// <summary>A rolling door that is shut until the map shift, then lifts out of the way.</summary>
        Shutter = 6,
        /// <summary>Paint on the floor (spawn pads). Not solid.</summary>
        Decal = 7,
    }

    /// <summary>What the map shift does to a block.</summary>
    public enum ShiftRole
    {
        None = 0,
        /// <summary>There before the shift, gone after it (shutters lift, gates open).</summary>
        RemovedOnShift = 1,
        /// <summary>Gone before the shift, there after it (a bridge drops).</summary>
        AddedOnShift = 2,
    }

    /// <summary>
    /// One axis-aligned box of the greybox. <see cref="center"/> is the box centre in metres; a ramp's
    /// box is its footprint and height, and its walking surface climbs from the floor (bottom of the
    /// box) at one end to the top of the box at the end it rises toward.
    /// </summary>
    [Serializable]
    public class MapBlock
    {
        public string name = "";
        public BlockKind kind;
        public Vec3 center;
        public Vec3 size;
        /// <summary>Ramps only: the compass way the slope climbs, as a yaw (0 = +z, 90 = +x, 180 = -z, 270 = -x).</summary>
        public int rampRisesToward;
        public ShiftRole shift;

        public MapBlock() { }
        public MapBlock(string name, BlockKind kind, Vec3 center, Vec3 size, ShiftRole shift = ShiftRole.None, int rampRisesToward = 0)
        {
            this.name = name;
            this.kind = kind;
            this.center = center;
            this.size = size;
            this.shift = shift;
            this.rampRisesToward = rampRisesToward;
        }

        public Vec3 Min => center - size * 0.5f;
        public Vec3 Max => center + size * 0.5f;
        public bool Solid => kind != BlockKind.Decal;

        /// <summary>Is the block in the world, before or after the shift?</summary>
        public bool Present(bool shifted) =>
            shift == ShiftRole.None || (shift == ShiftRole.RemovedOnShift ? !shifted : shifted);

        /// <summary>Height of the walking surface on top of the block at (x, z), which must be inside its footprint.</summary>
        public float TopAt(float x, float z)
        {
            if (kind != BlockKind.Ramp) return Max.y;
            float t;
            switch (((rampRisesToward % 360) + 360) % 360)
            {
                case 0: t = (z - Min.z) / size.z; break;
                case 90: t = (x - Min.x) / size.x; break;
                case 180: t = (Max.z - z) / size.z; break;
                default: t = (Max.x - x) / size.x; break;
            }
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            return Min.y + t * size.y;
        }

        /// <summary>
        /// Art only: how the dresser draws this block, for example "treeline" or "rocks" for natural walls
        /// that frame a lane. Empty draws it by its kind. Rules never read it.
        /// </summary>
        public string style = "";

        public MapBlock Clone() => (MapBlock)MemberwiseClone();
    }

    /// <summary>A footprint (x and z, any height) dressed in its own environment theme. Art only: rules never read it.</summary>
    [Serializable]
    public class ArtRegion
    {
        public string theme = "";
        public float minX, minZ, maxX, maxZ;

        public ArtRegion() { }
        public ArtRegion(string theme, float minX, float minZ, float maxX, float maxZ)
        {
            this.theme = theme; this.minX = minX; this.minZ = minZ; this.maxX = maxX; this.maxZ = maxZ;
        }

        public bool Contains(float x, float z) => x >= minX && x <= maxX && z >= minZ && z <= maxZ;
    }

    /// <summary>
    /// A path painted flat on the ground along a line of points (x and z; y is ignored). Art only.
    /// Styles: "lane" (a wide dirt lane with darker verges) and "trail" (a narrow mossy trail that
    /// starts with a leafy arch overhead, marking a way into the jungle).
    /// </summary>
    [Serializable]
    public class ArtPath
    {
        public string style = "lane";
        public float width = 8f;
        public List<Vec3> points = new List<Vec3>();

        public ArtPath() { }
        public ArtPath(string style, float width, params Vec3[] points)
        {
            this.style = style; this.width = width; this.points.AddRange(points);
        }

        /// <summary>Distance on the ground from (x, z) to the path's centre line.</summary>
        public float DistanceTo(float x, float z)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < points.Count; i++)
            {
                Vec3 a = points[i], b = points[i + 1];
                float dx = b.x - a.x, dz = b.z - a.z;
                float len2 = dx * dx + dz * dz;
                float t = len2 < 1e-6f ? 0f : ((x - a.x) * dx + (z - a.z) * dz) / len2;
                t = t < 0f ? 0f : t > 1f ? 1f : t;
                float px = a.x + dx * t - x, pz = a.z + dz * t - z;
                float d = (float)Math.Sqrt(px * px + pz * pz);
                if (d < best) best = d;
            }
            if (points.Count == 1) best = (float)Math.Sqrt((points[0].x - x) * (points[0].x - x) + (points[0].z - z) * (points[0].z - z));
            return best;
        }
    }

    /// <summary>A zone on the map: a standing cylinder. Its id is the one the mode's zone set uses.</summary>
    [Serializable]
    public class MapZone
    {
        public ZoneId id;
        /// <summary>The zone set it belongs to ("points" or "hills").</summary>
        public string set = "";
        /// <summary>Short name for the HUD and the map ("A", "Mid", "B", "Hill 1").</summary>
        public string label = "";
        /// <summary>Centre of the zone's floor.</summary>
        public Vec3 center;
        public float radius = 5f;
        /// <summary>The zone counts players whose feet are from 1 m below its floor to this far above it.</summary>
        public float height = 3f;
        public TeamId startOwner = TeamId.None;
        public TeamId forwardSpawnFor = TeamId.None;

        public bool Contains(Vec3 feet)
        {
            float dx = feet.x - center.x, dz = feet.z - center.z;
            if (dx * dx + dz * dz > radius * radius) return false;
            return feet.y >= center.y - 1f && feet.y <= center.y + height;
        }

        public MapZone Clone() => (MapZone)MemberwiseClone();
    }

    /// <summary>
    /// A whole map as data: the greybox blocks, the zones both modes play on, the spawn spots and
    /// the map shift. Game builds the scene from it (MapBuilder), the rules get the objective layout
    /// (<see cref="ToMatchMap"/>) and spawn spots (<see cref="ToLayout"/>) from it, and
    /// <see cref="MapValidator"/> checks it is fair. Maps are 180° turn symmetric about the origin:
    /// team A's half is at -z, team B's is the same half turned around.
    /// </summary>
    [Serializable]
    public partial class MapDef
    {
        public string id = "";
        public string displayName = "";
        /// <summary>Half the playable size: the map spans -halfX..halfX and -halfZ..halfZ.</summary>
        public float halfX;
        public float halfZ;
        public List<MapBlock> blocks = new List<MapBlock>();
        public List<MapZone> zones = new List<MapZone>();
        public List<SpawnPoint> spawnsA = new List<SpawnPoint>();
        public List<SpawnPoint> spawnsB = new List<SpawnPoint>();
        public List<SpawnPoint> forward = new List<SpawnPoint>();
        public List<SpawnPoint> dummies = new List<SpawnPoint>();
        /// <summary>
        /// Where a Flag Rush flag may appear (feet height). Each is the same walk from both bases, and
        /// the list holds every spot's turned twin; <see cref="MapValidator"/> checks both.
        /// </summary>
        public List<Vec3> flagSpots = new List<Vec3>();
        /// <summary>What the shift does, for the HUD and the docs.</summary>
        public string shiftDescription = "";
        /// <summary>Where the camera sits when nobody is playing (looks at <see cref="overviewTarget"/>).</summary>
        public Vec3 overviewPosition;
        public Vec3 overviewTarget;
        /// <summary>
        /// The environment theme the game dresses this layout in (an id the Game assembly knows, such as
        /// "jungle_moon"). Art only: rules and the validator never read it. Empty keeps the plain greybox.
        /// </summary>
        public string theme = "";
        /// <summary>Parts of the map dressed in another theme (a temple quarter in a jungle). Art only, like <see cref="theme"/>.</summary>
        public List<ArtRegion> artRegions = new List<ArtRegion>();
        /// <summary>Art only: painted ground paths, such as a dirt lane or a trail off into the jungle.</summary>
        public List<ArtPath> paths = new List<ArtPath>();

        /// <summary>The theme for something standing at (x, z): the last region holding it, else the map's theme.</summary>
        public string ThemeAt(float x, float z)
        {
            string found = theme;
            foreach (var r in artRegions) if (r.Contains(x, z)) found = r.theme;
            return found;
        }

        public MapZone Zone(ZoneId id)
        {
            foreach (var z in zones) if (z.id == id) return z;
            return null;
        }

        public MapBlock Block(string name)
        {
            foreach (var b in blocks) if (b.name == name) return b;
            return null;
        }

        /// <summary>The objective layout for MatchRules: one zone set per set name, in the order the zones were added.</summary>
        public MatchMap ToMatchMap()
        {
            var map = new MatchMap { mapId = id };
            map.flagSpots.AddRange(flagSpots);
            foreach (var z in zones)
            {
                var set = map.ZoneSet(z.set);
                if (set == null) { set = new ZoneSetDef { name = z.set }; map.zoneSets.Add(set); }
                set.zones.Add(new ZoneDef(z.id.value, z.startOwner, z.forwardSpawnFor));
            }
            return map;
        }

        /// <summary>The spawn spots for the movement rules.</summary>
        public ArenaLayout ToLayout()
        {
            var layout = new ArenaLayout();
            layout.spawnsA.AddRange(spawnsA);
            layout.spawnsB.AddRange(spawnsB);
            layout.forward.AddRange(forward);
            layout.dummies.AddRange(dummies);
            foreach (var pad in launchPads) layout.launchPads.Add(pad.Clone());
            return layout;
        }

        /// <summary>Who stands in a zone: the ids of players whose feet are inside it, in id order.</summary>
        public int[] PlayersInZone(ZoneId zone, IEnumerable<PlayerMotion> motions)
        {
            var z = Zone(zone);
            if (z == null || motions == null) return Array.Empty<int>();
            var ids = new List<int>();
            foreach (var m in motions) if (m != null && z.Contains(m.position)) ids.Add(m.playerId);
            ids.Sort();
            return ids.ToArray();
        }

        // ---- The 180° turn that makes team B's half from team A's ----

        public static Vec3 Turn(Vec3 p) => new Vec3(-p.x, p.y, -p.z);
        public static float TurnYaw(float yaw) => ((yaw + 180f) % 360f + 360f) % 360f;
        public static int TurnYaw(int yaw) => ((yaw + 180) % 360 + 360) % 360;

        public static MapBlock Turn(MapBlock b, string name)
        {
            var t = b.Clone();
            t.name = name;
            t.center = Turn(b.center);
            t.rampRisesToward = TurnYaw(b.rampRisesToward);
            return t;
        }

        public static SpawnPoint Turn(SpawnPoint s, int zone) => new SpawnPoint(-s.position.x, s.position.y, -s.position.z, TurnYaw(s.yaw), zone);
    }

    /// <summary>
    /// The maps the game knows. Each mode plays on one map: Overrun needs a lane map (Breakwater), every
    /// other mode plays Gantry Yard. Every machine picks the map from the match's mode, so the host
    /// never has to send it.
    /// </summary>
    public static class MapCatalog
    {
        public static MapDef Default => GantryYard.Build();

        /// <summary>The id of the map a mode plays on.</summary>
        public static string MapIdForMode(string modeId) => modeId == "overrun" ? Breakwater.Id : GantryYard.Id;

        /// <summary>A map's name for menus, without building it.</summary>
        public static string DisplayName(string id) => id == Breakwater.Id ? "Breakwater" : id == GantryYard.Id ? "Gantry Yard" : id;

        /// <summary>The map a mode plays on, built fresh.</summary>
        public static MapDef ForMode(string modeId) => ById(MapIdForMode(modeId));

        public static MapDef ById(string id)
        {
            if (id == GantryYard.Id) return GantryYard.Build();
            if (id == Breakwater.Id) return Breakwater.Build();
            return null;
        }
    }
}
