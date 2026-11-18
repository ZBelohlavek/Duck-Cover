using System;
using System.Collections.Generic;

namespace DuckAndCover.Core
{
    /// <summary>How a map makes team B's half from team A's.</summary>
    public enum MapSymmetry
    {
        /// <summary>Turned 180° about the centre (Gantry Yard): (x, z) becomes (-x, -z).</summary>
        HalfTurn = 0,
        /// <summary>Mirrored end to end (Breakwater): (x, z) becomes (x, -z), so one side of the map stays one side.</summary>
        MirrorZ = 1,
    }

    /// <summary>Which of a team's two Bastions.</summary>
    public enum BastionTier
    {
        /// <summary>Out on the lane: it must fall first.</summary>
        Forward = 0,
        /// <summary>At the base door: destroying it wins Overrun.</summary>
        Core = 1,
    }

    /// <summary>Where a Bastion stands (Overrun). Its rules (shield, health, who may hurt it) are WP16's.</summary>
    [Serializable]
    public class MapBastion
    {
        public TeamId team;
        public BastionTier tier;
        /// <summary>Feet of the Bastion, on the lane.</summary>
        public Vec3 position;
        /// <summary>Footprint radius, for drawing and spacing.</summary>
        public float radius = 2f;

        public MapBastion Clone() => (MapBastion)MemberwiseClone();
    }

    /// <summary>
    /// A one-way drop: a ledge or shelf you can drop from but not climb back up. The validator walks it
    /// both ways: from <see cref="top"/> down to <see cref="bottom"/> must be quick, and from the
    /// bottom back up must take the long way round.
    /// </summary>
    [Serializable]
    public class MapDrop
    {
        public string name = "";
        /// <summary>A spot on the ledge, at the edge (feet).</summary>
        public Vec3 top;
        /// <summary>Where you land below it (feet).</summary>
        public Vec3 bottom;

        public MapDrop Clone() => (MapDrop)MemberwiseClone();
    }

    /// <summary>What a heroes-only route through the jungle is for.</summary>
    public enum RouteKind
    {
        /// <summary>A jungle path that leaves the lane and joins it again further along (both ends on the lane).</summary>
        Jungle = 0,
        /// <summary>A short way from a jungle path to the lane (its last point is on the lane).</summary>
        CutThrough = 1,
        /// <summary>A dead end off a jungle path, to a camp (its first point is on another route).</summary>
        Branch = 2,
        /// <summary>A way into a base that opens with the map shift.</summary>
        SideRoute = 3,
        /// <summary>A second way round a stretch of a jungle path (both ends on other routes), so the jungle is a maze, not a corridor.</summary>
        Loop = 4,
    }

    /// <summary>
    /// A heroes-only route as rule data: a centre line the validator walks. The layout also paints it
    /// as a trail (<see cref="ArtPath"/>), but the checks read this, never the art.
    /// </summary>
    [Serializable]
    public class MapRoute
    {
        public string name = "";
        public RouteKind kind;
        public float width = 5f;
        public List<Vec3> points = new List<Vec3>();

        public MapRoute Clone()
        {
            var c = (MapRoute)MemberwiseClone();
            c.points = new List<Vec3>(points);
            return c;
        }
    }

    /// <summary>A way into a team's base (the spawn room and its plaza). Flag Rush needs at least two, open at all times.</summary>
    [Serializable]
    public class MapEntrance
    {
        public string name = "";
        public TeamId team;
        /// <summary>A spot in the doorway (feet).</summary>
        public Vec3 position;
        /// <summary>Half the doorway's width, for the check that blocks it.</summary>
        public float halfWidth = 3f;
    }

    // Lane-map data (WP18): Bastions, launch pads, one-way drops, jungle routes and base entrances. The
    // lane and the camps are WP15's (MapDef.Lanes.cs). Maps without lanes leave all of it empty.
    public partial class MapDef
    {
        public MapSymmetry symmetry = MapSymmetry.HalfTurn;
        /// <summary>Width of the lane's walkable floor between its walls (metres).</summary>
        public float laneWidth = 10f;
        public List<MapBastion> bastions = new List<MapBastion>();
        public List<LaunchPad> launchPads = new List<LaunchPad>();
        public List<MapDrop> drops = new List<MapDrop>();
        public List<MapRoute> routes = new List<MapRoute>();
        public List<MapEntrance> entrances = new List<MapEntrance>();

        /// <summary>A lane map (Overrun): it has a minion lane.</summary>
        public bool IsLaneMap => lanes != null && lanes.Count > 0;

        public MapBastion Bastion(TeamId team, BastionTier tier)
        {
            foreach (var b in bastions) if (b.team == team && b.tier == tier) return b;
            return null;
        }

        public LaunchPad Pad(string name)
        {
            foreach (var p in launchPads) if (p.name == name) return p;
            return null;
        }

        /// <summary>A shallow copy, for checks that add a block to a copy of the map.</summary>
        internal MapDef ShallowCopy() => (MapDef)MemberwiseClone();

        // ---- The end-to-end mirror that makes team B's half from team A's ----

        public static Vec3 Mirror(Vec3 p) => new Vec3(p.x, p.y, -p.z);
        /// <summary>A yaw seen in the mirror: looking along +z becomes looking along -z; east stays east.</summary>
        public static float MirrorYaw(float yaw) => ((180f - yaw) % 360f + 360f) % 360f;
        public static int MirrorYaw(int yaw) => ((180 - yaw) % 360 + 360) % 360;

        public static MapBlock Mirror(MapBlock b, string name)
        {
            var t = b.Clone();
            t.name = name;
            t.center = Mirror(b.center);
            t.rampRisesToward = MirrorYaw(b.rampRisesToward);
            return t;
        }

        public static SpawnPoint Mirror(SpawnPoint s) => new SpawnPoint(s.position.x, s.position.y, -s.position.z, MirrorYaw(s.yaw), s.zone.value);

        /// <summary>Team B's twin of a point under this map's symmetry.</summary>
        public Vec3 Twin(Vec3 p) => symmetry == MapSymmetry.MirrorZ ? Mirror(p) : Turn(p);
        public int TwinYaw(int yaw) => symmetry == MapSymmetry.MirrorZ ? MirrorYaw(yaw) : TurnYaw(yaw);
        public float TwinYaw(float yaw) => symmetry == MapSymmetry.MirrorZ ? MirrorYaw(yaw) : TurnYaw(yaw);
    }
}
