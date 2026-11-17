using System;

namespace DuckAndCover.Core
{
    /// <summary>Whether a surface stands up (walls, door boards) or lies flat (floor hatches).</summary>
    public enum SurfaceKind : byte
    {
        Wall = 0,
        Floor = 1,
    }

    /// <summary>
    /// One breakable surface as the map authors it (WP5's BreakableSurface writes these). Size is in
    /// cells; the surface's local coordinates (u across, v up, metres) start at its bottom-left corner
    /// seen from the front, the same corner as cell (0,0).
    /// </summary>
    [Serializable]
    public class SurfaceDef
    {
        /// <summary>Unique on the map, starting at 1.</summary>
        public int id;
        public int width;
        public int height;
        public SurfaceMaterial material = SurfaceMaterial.Soft;
        public SurfaceKind kind = SurfaceKind.Wall;
        /// <summary>The structure zone the surface belongs to. Soft and hard surfaces need one; door and
        /// window boards (barricade) may use 0, which keeps them out of zone integrity.</summary>
        public int zoneId;
        /// <summary>Starts with every cell open: a bare doorway or window that defenders can board up with a patch.</summary>
        public bool startOpen;
    }

    /// <summary>A structure zone: a few walls whose integrity is tracked together for the whole match.</summary>
    [Serializable]
    public class ZoneDef
    {
        /// <summary>Unique on the map, starting at 1.</summary>
        public int id;
        /// <summary>Zones around charge sites use the higher site floor.</summary>
        public bool nearSite;
        /// <summary>Overrides the ruleset floor when above 0 (for a special wall the map wants tougher or looser).</summary>
        public float floorOverride;
    }

    /// <summary>Everything breakable on one map. Built once from the scene and identical on every machine.</summary>
    [Serializable]
    public class MapDestructionDef
    {
        public SurfaceDef[] surfaces = new SurfaceDef[0];
        public ZoneDef[] zones = new ZoneDef[0];
    }
}
